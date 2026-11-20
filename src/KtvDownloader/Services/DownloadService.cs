using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Infrastructure.Extensions;
using KtvDownloader.Infrastructure.Http;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Models;
using KtvDownloader.Services.Interfaces;

namespace KtvDownloader.Services
{
    /// <summary>
    /// 单曲下载服务。
    /// <para>流程：取址 → 下载 → 判断是否加密 → 解密 → 转 MP4 / 分离音轨 → 落盘。</para>
    /// <para>
    /// 每个任务持有独立的 <see cref="IProgress{T}"/> 与 <see cref="CancellationToken"/>，
    /// 因此批量下载时每首歌都有各自独立的进度，互不干扰。
    /// </para>
    /// </summary>
    public sealed class DownloadService : IDownloadService
    {
        private readonly ILogger _logger;
        private readonly ISongUrlService _urlService;
        private readonly ITsDecryptService _decryptService;
        private readonly IMediaService _mediaService;
        private readonly HttpClient _httpClient;

        private int _maxConcurrency = 2;

        public DownloadService(
            ILogger logger,
            ISongUrlService urlService,
            ITsDecryptService decryptService,
            IMediaService mediaService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _urlService = urlService ?? throw new ArgumentNullException(nameof(urlService));
            _decryptService = decryptService ?? throw new ArgumentNullException(nameof(decryptService));
            _mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        public int MaxConcurrency
        {
            get { return _maxConcurrency; }
            set { _maxConcurrency = Math.Max(1, Math.Min(8, value)); }
        }

        /// <summary>由队列维护，这里仅供界面显示用。</summary>
        public int ActiveTaskCount { get; internal set; }

        public void CancelAll()
        {
            // 实际的取消由队列对每个任务各自的 CancellationTokenSource 完成。
        }

        public async Task<DownloadTask> ExecuteAsync(
            DownloadTask task,
            DownloadOptions options,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (options == null) throw new ArgumentNullException(nameof(options));

            try
            {
                // ── 1. 取址 ──
                SetState(task, DownloadState.Resolving, "正在获取下载地址", progress);

                var urlResult = await _urlService.ResolveAsync(
                    task.Song.MusicNo, options.Resolution, options.PreferH265, cancellationToken)
                    .ConfigureAwait(false);

                if (!urlResult.Success)
                {
                    return Fail(task, "取址失败：" + urlResult.Error, progress);
                }

                var safeBaseName = BuildBaseFileName(task.Song);
                if (!Directory.Exists(task.OutputDirectory))
                    Directory.CreateDirectory(task.OutputDirectory);

                // ── 2. 下载原始文件 ──
                var extension = GuessExtension(urlResult.Url, task.Song);
                var rawPath = Path.Combine(task.OutputDirectory,
                    safeBaseName + ".download" + extension);

                SetState(task, DownloadState.Downloading, "开始下载", progress);

                var downloaded = await DownloadFileAsync(
                    urlResult.Url, rawPath, task, progress, cancellationToken).ConfigureAwait(false);

                if (!downloaded)
                    return Fail(task, "下载文件失败", progress);

                // ── 3. 解密（如果是加密 TS）──
                var workingPath = rawPath;
                var encrypted = await _decryptService.IsEncryptedAsync(rawPath, cancellationToken)
                    .ConfigureAwait(false);

                if (encrypted)
                {
                    SetState(task, DownloadState.Decrypting, "正在解密", progress);

                    var decryptedPath = Path.Combine(task.OutputDirectory, safeBaseName + ".decrypted.ts");
                    var decryptProgress = new Progress<double>(percent =>
                    {
                        progress?.Report(new DownloadProgress(
                            DownloadState.Decrypting, percent > 0 ? (long)percent : 0, 100,
                            string.Format("解密中 {0:F0}%", percent)));
                    });

                    var ok = await _decryptService.DecryptAsync(
                        rawPath, decryptedPath, decryptProgress, cancellationToken).ConfigureAwait(false);

                    if (!ok)
                    {
                        SafeDelete(rawPath);
                        return Fail(task, "解密失败，文件可能不是已知加密格式", progress);
                    }

                    SafeDelete(rawPath);
                    workingPath = decryptedPath;
                }

                // ── 4. 转 MP4 / 分离音轨 ──
                SetState(task, DownloadState.Converting, "正在转码", progress);

                var finalPath = await FinishAsync(
                    task, workingPath, safeBaseName, options, progress, cancellationToken)
                    .ConfigureAwait(false);

                if (string.IsNullOrEmpty(finalPath))
                {
                    SafeDelete(workingPath);
                    return Fail(task, "转码失败", progress);
                }

                // ── 5. 清理中间文件 ──
                if (options.DeleteIntermediateFiles && !string.Equals(workingPath, finalPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    SafeDelete(workingPath);
                }

                task.OutputPath = finalPath;
                SetState(task, DownloadState.Completed, "已完成", progress);
                _logger.Info("下载完成: " + finalPath);

                return task;
            }
            catch (OperationCanceledException)
            {
                task.State = DownloadState.Canceled;
                task.StatusText = "已取消";
                progress?.Report(new DownloadProgress(DownloadState.Canceled, 0, null, "已取消"));
                _logger.Info("下载已取消: " + task.Song.DisplayName);
                return task;
            }
            catch (Exception ex)
            {
                _logger.Error("下载异常: " + task.Song.DisplayName, ex);
                return Fail(task, ex.Message, progress);
            }
        }

        #region 下载

        /// <summary>
        /// 下载文件，带自动重试。
        /// CDN 会间歇性返回 502/503 或断连，重试通常就能成功。
        /// </summary>
        private async Task<bool> DownloadFileAsync(
            string url,
            string destinationPath,
            DownloadTask task,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            return await RetryPolicy.ExecuteAsync(async attempt =>
            {
                if (attempt > 1)
                {
                    _logger.Info(string.Format("下载重试第 {0} 次: {1}", attempt - 1, task.Song.DisplayName));
                    SetState(task, DownloadState.Downloading,
                        string.Format("重试中（第 {0} 次）", attempt - 1), progress);
                }

                return await DownloadFileOnceAsync(
                    url, destinationPath, task, progress, cancellationToken).ConfigureAwait(false);
            }, RetryPolicy.DefaultMaxAttempts,
               (attempt, reason) => _logger.Warn(string.Format(
                   "下载失败将重试 原因={0} 第{1}次", reason, attempt)),
               cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> DownloadFileOnceAsync(
            string url,
            string destinationPath,
            DownloadTask task,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            // 歌曲 CDN 用标准 HTTP，可以带 UA（它与取址接口不是同一套风控）。
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("Accept", "*/*");

                using (var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var code = response.StatusCode;
                        _logger.Warn("下载 HTTP " + (int)code + " " + url);

                        // 5xx / 429 / 408 属于临时故障，抛异常交给重试机制。
                        if (RetryPolicy.IsRetryable(code))
                        {
                            throw new HttpRequestException(
                                string.Format("CDN 返回 {0}（{1}）", (int)code, code));
                        }

                        return false;
                    }

                    var total = response.Content.Headers.ContentLength;
                    task.TotalBytes = total;

                    using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var destination = new FileStream(destinationPath, FileMode.Create,
                        FileAccess.Write, FileShare.None, 256 * 1024, useAsync: true))
                    {
                        var buffer = new byte[256 * 1024];
                        long received = 0;
                        var lastPercent = -1;

                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                                .ConfigureAwait(false);
                            if (read <= 0) break;

                            await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                            received += read;
                            task.BytesReceived = received;

                            // 每 1% 上报一次，避免 UI 被高频刷新拖垮。
                            if (total.HasValue && total.Value > 0)
                            {
                                var percent = (int)(received * 100 / total.Value);
                                if (percent == lastPercent) continue;
                                lastPercent = percent;
                            }

                            progress?.Report(new DownloadProgress(
                                DownloadState.Downloading, received, total, "下载中"));
                        }
                    }
                }
            }

            return true;
        }

        #endregion

        #region 收尾处理

        private async Task<string> FinishAsync(
            DownloadTask task,
            string sourcePath,
            string safeBaseName,
            DownloadOptions options,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            var outputDirectory = task.OutputDirectory;

            // 不需要转码：直接把原始文件改成正式名。
            if (!options.ConvertToMp4)
            {
                var finalPath = Path.Combine(outputDirectory,
                    safeBaseName + Path.GetExtension(sourcePath));
                if (File.Exists(finalPath)) SafeDelete(finalPath);
                File.Move(sourcePath, finalPath);
                return finalPath;
            }

            if (!_mediaService.IsFfmpegAvailable)
            {
                _logger.Warn("ffmpeg 不可用，保留原始文件");
                var fallback = Path.Combine(outputDirectory,
                    safeBaseName + Path.GetExtension(sourcePath));
                if (File.Exists(fallback)) SafeDelete(fallback);
                File.Move(sourcePath, fallback);
                return fallback;
            }

            // 分离音轨：产出原唱/伴奏两个文件。
            if (options.Vocal == VocalOption.SplitBoth ||
                options.Vocal == VocalOption.OriginalOnly ||
                options.Vocal == VocalOption.AccompanimentOnly)
            {
                // 先统计音轨，决定是否需要分离。
                var trackCount = await _mediaService.CountAudioTracksAsync(sourcePath, cancellationToken)
                    .ConfigureAwait(false);

                progress?.Report(new DownloadProgress(
                    DownloadState.Converting, 0, null,
                    string.Format("检测到 {0} 条音轨，正在分离", trackCount)));

                var split = await _mediaService.SplitVocalAsync(
                    sourcePath, outputDirectory, safeBaseName, options.Vocal,
                    options.AudioBitrateKbps, cancellationToken).ConfigureAwait(false);

                if (split.HasAny)
                {
                    var produced = split.GetAllPaths();
                    _logger.Info("音轨分离产出 " + produced.Length + " 个文件");
                    return produced[0];
                }

                // 分离失败则退回普通转码。
                _logger.Warn("音轨分离未产出文件，退回普通转码");
            }

            var mp4Path = Path.Combine(outputDirectory, safeBaseName + ".mp4");
            var converted = await _mediaService.ConvertToMp4Async(
                sourcePath, mp4Path, VocalOption.KeepBoth,
                options.AudioBitrateKbps, cancellationToken).ConfigureAwait(false);

            return converted ? mp4Path : null;
        }

        #endregion

        #region 工具

        private static string BuildBaseFileName(Song song)
        {
            var name = song.Singer.HasValue()
                ? song.Name + "-" + song.Singer
                : song.Name;

            var safe = name.ToSafeFileName(100);
            return safe.Length > 0 ? safe : song.MusicNo;
        }

        private static string GuessExtension(string url, Song song)
        {
            // 优先看 URL 的扩展名，其次看曲库 filename。
            try
            {
                var path = new Uri(url).AbsolutePath;
                var extension = Path.GetExtension(path);
                if (!string.IsNullOrEmpty(extension) && extension.Length <= 5) return extension;
            }
            catch
            {
                // 落到曲库扩展名。
            }

            var fileExtension = Path.GetExtension(song.FileName ?? string.Empty);
            return string.IsNullOrEmpty(fileExtension) ? ".ts" : fileExtension;
        }

        private static void SetState(
            DownloadTask task,
            DownloadState state,
            string statusText,
            IProgress<DownloadProgress> progress)
        {
            task.State = state;
            task.StatusText = statusText;
            progress?.Report(new DownloadProgress(state, task.BytesReceived, task.TotalBytes, statusText));
        }

        private static DownloadTask Fail(
            DownloadTask task,
            string message,
            IProgress<DownloadProgress> progress)
        {
            task.State = DownloadState.Failed;
            task.ErrorMessage = message;
            task.StatusText = "失败：" + message;
            progress?.Report(new DownloadProgress(DownloadState.Failed, task.BytesReceived,
                task.TotalBytes, task.StatusText));
            return task;
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 忽略。
            }
        }

        #endregion
    }
}
