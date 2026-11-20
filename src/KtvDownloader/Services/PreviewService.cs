using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
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
    /// 预览服务实现。
    /// <para>
    /// 流程：取址 → 下载前 N 秒 → Thunder 解密 → 转 MP4 → 返回本地路径。
    /// 生成的 MP4 放在缓存目录，同一首歌再次预览直接复用。
    /// </para>
    /// </summary>
    public sealed class PreviewService : IPreviewService
    {
        /// <summary>预览片段默认时长（秒）。</summary>
        private const int DefaultDurationSeconds = 30;

        /// <summary>
        /// 首屏预取字节数。
        /// 480p 约 250KB/s，8MB 大致覆盖 30-60 秒，
        /// 足够「打开就能播」，后续由播放器自行缓冲。
        /// </summary>
        private const long DownloadBytesForPreview = 8L * 1024 * 1024;

        private readonly ILogger _logger;
        private readonly ISongUrlService _urlService;
        private readonly ITsDecryptService _decryptService;
        private readonly IMediaService _mediaService;
        private readonly HttpClient _httpClient;

        public PreviewService(
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
            _httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        public string CacheDirectory =>
            Path.Combine(AppConfig.AppDataDirectory, "preview-cache");

        public void ClearCache()
        {
            try
            {
                if (!Directory.Exists(CacheDirectory)) return;

                foreach (var file in Directory.GetFiles(CacheDirectory))
                {
                    try { File.Delete(file); } catch { }
                }
                _logger.Info("预览缓存已清空");
            }
            catch (Exception ex)
            {
                _logger.Warn("清空预览缓存失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 为 VLC 准备可播文件：下载 + 解密，不转码。
        /// 返回的路径是解密后的 .ts，VLC 能直接播。
        /// </summary>
        public async Task<PreviewSource> PreparePlayableAsync(
            Song song,
            int durationSeconds,
            CancellationToken cancellationToken,
            IProgress<Tuple<int, string>> progress)
        {
            if (song == null) return PreviewSource.Fail("歌曲为空");
            if (string.IsNullOrWhiteSpace(song.MusicNo)) return PreviewSource.Fail("歌号为空");

            var duration = durationSeconds < 0 ? DefaultDurationSeconds : durationSeconds;
            var fullPreview = duration == 0;

            try
            {
                if (!Directory.Exists(CacheDirectory)) Directory.CreateDirectory(CacheDirectory);

                // 缓存键不含音轨 —— TS 本身含全部音轨，切换由 VLC 完成。
                var cacheKey = BuildCacheKey(song, duration, VocalOption.KeepBoth);
                var cachedTs = Path.Combine(CacheDirectory, cacheKey + ".play.ts");

                if (File.Exists(cachedTs) && new FileInfo(cachedTs).Length > 200 * 1024)
                {
                    Report(progress, 100, "使用缓存");
                    _logger.Info("VLC 预览命中缓存: " + cachedTs);
                    return PreviewSource.Ok(cachedTs, true);
                }

                // 1. 取址
                Report(progress, 5, "正在获取地址");
                var urlResult = await _urlService.ResolveAsync(
                    song.MusicNo, "720", false, cancellationToken).ConfigureAwait(false);

                if (!urlResult.Success)
                    return PreviewSource.Fail("取址失败：" + urlResult.Error);

                // 2. 下载
                Report(progress, 15, fullPreview ? "正在下载" : "正在下载片段");
                var rawPath = Path.Combine(CacheDirectory, cacheKey + ".raw");

                long downloaded;
                try
                {
                    downloaded = await RetryPolicy.ExecuteAsync(async attempt =>
                    {
                        if (attempt > 1)
                            Report(progress, 15, string.Format("下载重试第 {0} 次", attempt - 1));

                        return await DownloadPartialAsync(
                            urlResult.Url, rawPath,
                            fullPreview ? 0 : EstimateBytes(duration),
                            fullPreview, progress, cancellationToken).ConfigureAwait(false);

                    }, RetryPolicy.DefaultMaxAttempts,
                       (attempt, reason) => _logger.Warn("预览下载将重试: " + reason),
                       cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warn("预览下载失败: " + ex.Message);
                    SafeDelete(rawPath);
                    return PreviewSource.Fail("下载失败：" + ex.Message);
                }

                if (downloaded <= 0)
                {
                    SafeDelete(rawPath);
                    return PreviewSource.Fail("下载失败");
                }

                // 3. 解密（VLC 播不了加密流）
                Report(progress, 60, "正在解密");
                var encrypted = await _decryptService.IsEncryptedAsync(rawPath, cancellationToken)
                    .ConfigureAwait(false);

                if (encrypted)
                {
                    var ok = await _decryptService.DecryptAsync(
                        rawPath, cachedTs, null, cancellationToken).ConfigureAwait(false);

                    SafeDelete(rawPath);

                    if (!ok)
                    {
                        SafeDelete(cachedTs);
                        return PreviewSource.Fail("解密失败");
                    }
                }
                else
                {
                    if (File.Exists(cachedTs)) SafeDelete(cachedTs);
                    File.Move(rawPath, cachedTs);
                }

                Report(progress, 100, "准备完成");
                _logger.Info("VLC 预览就绪（未转码）: " + cachedTs);
                return PreviewSource.Ok(cachedTs, false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error("准备播放文件失败", ex);
                return PreviewSource.Fail(ex.Message);
            }
        }

        public async Task<PreviewSource> PrepareAsync(
            Song song,
            int durationSeconds,
            VocalOption vocal,
            IProgress<Tuple<int, string>> progress,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (song == null) return PreviewSource.Fail("歌曲为空");
            if (string.IsNullOrWhiteSpace(song.MusicNo)) return PreviewSource.Fail("歌号为空");

            if (!_mediaService.IsFfmpegAvailable)
                return PreviewSource.Fail("需要 ffmpeg 才能预览（源文件是加密 TS，必须转码）");

            var duration = durationSeconds < 0 ? DefaultDurationSeconds : durationSeconds;

            try
            {
                if (!Directory.Exists(CacheDirectory)) Directory.CreateDirectory(CacheDirectory);

                var cacheKey = BuildCacheKey(song, duration, vocal);
                var cachedMp4 = Path.Combine(CacheDirectory, cacheKey + ".mp4");

                // 已有缓存直接复用。
                if (File.Exists(cachedMp4) && new FileInfo(cachedMp4).Length > 100 * 1024)
                {
                    Report(progress, 100, "使用缓存的预览");
                    _logger.Info("预览命中缓存: " + cachedMp4);
                    return PreviewSource.Ok(cachedMp4, true);
                }

                // ── 1. 取址 ──
                Report(progress, 5, "正在获取播放地址");
                var urlResult = await _urlService.ResolveAsync(
                    song.MusicNo, "720", false, cancellationToken).ConfigureAwait(false);

                if (!urlResult.Success)
                    return PreviewSource.Fail("取址失败：" + urlResult.Error);

                // ── 2. 下载 ──
                // duration == 0 表示完整预览（整首下载）；否则只取够 duration 秒的一段。
                var fullPreview = duration == 0;

                Report(progress, 15, fullPreview ? "正在下载完整文件" : "正在下载预览片段");
                var rawPath = Path.Combine(CacheDirectory, cacheKey + ".raw");

                long downloaded = 0;
                try
                {
                    // CDN 会间歇性 502，重试几次基本都能过。
                    downloaded = await RetryPolicy.ExecuteAsync(async attempt =>
                    {
                        if (attempt > 1)
                            Report(progress, 15, string.Format("下载重试第 {0} 次", attempt - 1));

                        return await DownloadPartialAsync(
                            urlResult.Url, rawPath, fullPreview ? 0 : EstimateBytes(duration),
                            fullPreview, progress, cancellationToken).ConfigureAwait(false);

                    }, RetryPolicy.DefaultMaxAttempts,
                       (attempt, reason) => _logger.Warn("预览下载失败将重试: " + reason),
                       cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warn("预览下载重试耗尽: " + ex.Message);
                    SafeDelete(rawPath);
                    return PreviewSource.Fail("下载失败（CDN 响应异常，请稍后重试）：" + ex.Message);
                }

                if (downloaded <= 0)
                {
                    SafeDelete(rawPath);
                    return PreviewSource.Fail("下载失败");
                }

                // ── 3. 解密 ──
                Report(progress, 45, "正在解密");
                var decryptedPath = Path.Combine(CacheDirectory, cacheKey + ".ts");

                var encrypted = await _decryptService.IsEncryptedAsync(rawPath, cancellationToken)
                    .ConfigureAwait(false);

                string workingPath;
                if (encrypted)
                {
                    var ok = await _decryptService.DecryptAsync(
                        rawPath, decryptedPath, null, cancellationToken).ConfigureAwait(false);

                    SafeDelete(rawPath);

                    if (!ok)
                    {
                        SafeDelete(decryptedPath);
                        return PreviewSource.Fail("解密失败，该文件可能不是已知加密格式");
                    }
                    workingPath = decryptedPath;
                }
                else
                {
                    SafeDelete(rawPath);
                    workingPath = rawPath;
                }

                // ── 4. 转 MP4 ──
                Report(progress, 70, "正在转码");
                var converted = await ConvertPreviewAsync(
                    workingPath, cachedMp4, duration, vocal, cancellationToken).ConfigureAwait(false);

                SafeDelete(workingPath);

                if (!converted)
                {
                    SafeDelete(cachedMp4);
                    return PreviewSource.Fail("转码失败");
                }

                Report(progress, 100, "预览就绪");
                _logger.Info("预览生成完成: " + cachedMp4);
                return PreviewSource.Ok(cachedMp4, false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error("生成预览失败", ex);
                return PreviewSource.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 按 480p 约 250KB/s 估算需要的字节数，留 1.5 倍余量。
        /// </summary>
        private static long EstimateBytes(int durationSeconds)
        {
            if (durationSeconds <= 0) return 0;

            var estimated = durationSeconds * 250L * 1024;
            return Math.Max(8L * 1024 * 1024, (long)(estimated * 1.5));
        }

        /// <summary>
        /// 下载文件内容。
        /// </summary>
        /// <param name="maxBytes">最多下载多少字节。0 或负数表示下载完整文件。</param>
        /// <param name="fullDownload">true 表示完整下载（不带 Range 头）。</param>
        private async Task<long> DownloadPartialAsync(
            string url,
            string destinationPath,
            long maxBytes,
            bool fullDownload,
            IProgress<Tuple<int, string>> progress,
            CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("Accept", "*/*");

                if (!fullDownload && maxBytes > 0)
                {
                    request.Headers.TryAddWithoutValidation("Range", "bytes=0-" + (maxBytes - 1));
                }

                using (var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var code = response.StatusCode;
                        _logger.Warn("预览下载 HTTP " + (int)code);

                        if (RetryPolicy.IsRetryable(code))
                            throw new HttpRequestException(
                                string.Format("CDN 返回 {0}", (int)code));

                        return 0;
                    }

                    var contentLength = response.Content.Headers.ContentLength;
                    var targetBytes = fullDownload || maxBytes <= 0
                        ? (contentLength ?? long.MaxValue)
                        : maxBytes;

                    using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var destination = new FileStream(destinationPath, FileMode.Create,
                        FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
                    {
                        var buffer = new byte[128 * 1024];
                        long received = 0;
                        var lastPercent = -1;

                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var want = (int)Math.Min(buffer.Length, Math.Max(0, targetBytes - received));
                            if (want <= 0) break;

                            var read = await source.ReadAsync(buffer, 0, want, cancellationToken)
                                .ConfigureAwait(false);
                            if (read <= 0) break;

                            await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                            received += read;

                            // 映射到 15-45% 的进度区间。
                            if (targetBytes > 0 && targetBytes != long.MaxValue)
                            {
                                var percent = (int)(received * 30 / targetBytes) + 15;
                                if (percent != lastPercent)
                                {
                                    lastPercent = percent;
                                    Report(progress, Math.Min(45, percent),
                                        fullDownload ? "正在下载完整文件" : "正在下载预览片段");
                                }
                            }
                        }

                        return received;
                    }
                }
            }
        }

        /// <summary>
        /// 转成短视频 MP4：只取前 duration 秒，音频重编码 AAC（避免静音）。
        /// </summary>
        private async Task<bool> ConvertPreviewAsync(
            string sourcePath,
            string outputPath,
            int durationSeconds,
            VocalOption vocal,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var ffmpeg = _mediaService.FfmpegPath;
                    if (string.IsNullOrEmpty(ffmpeg)) return false;

                    var arguments = new StringBuilder();
                    arguments.Append("-y -i ").Append(Quote(sourcePath));

                    // duration > 0 才裁剪；0 表示完整转码。
                    if (durationSeconds > 0)
                        arguments.Append(" -t ").Append(durationSeconds);

                    // 按音轨模式映射：第 1 条=原唱，第 2 条=伴奏。
                    arguments.Append(" -map 0:v:0");
                    switch (vocal)
                    {
                        case VocalOption.AccompanimentOnly:
                            arguments.Append(" -map 0:a:1?");
                            break;
                        default:
                            arguments.Append(" -map 0:a:0");
                            break;
                    }

                    // 关键优化：视频流直接拷贝（不重编码），只把 mp2 音频转成 AAC。
                    // 原做法用 libx264 重编码，几百帧要 3-4 秒；
                    // 流拷贝后通常 0.2-1.2 秒，画质还零损失。
                    // 音频必须转 AAC：mp2 直接塞进 mp4 会被标成 mp3，多数播放器静音。
                    arguments.Append(" -c:v copy -c:a aac -b:a 128k -ac 2");

                    // fMP4 分片：moov 前置 + 分片，播放器可以先播已下载部分。
                    arguments.Append(" -movflags +faststart+frag_keyframe+empty_moov");

                    arguments.Append(' ').Append(Quote(outputPath));

                    _logger.Info("预览转码命令: " + arguments);

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = ffmpeg,
                        Arguments = arguments.ToString(),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    using (var process = Process.Start(startInfo))
                    {
                        if (process == null) return false;

                        var stderr = process.StandardError.ReadToEnd();

                        // 因为输入是被截断的 TS，ffmpeg 退出码可能非 0，
                        // 只要输出文件有效就认为成功。
                        process.WaitForExit(120000);

                        if (!File.Exists(outputPath))
                        {
                            _logger.Warn("预览转码未生成文件 exit=" + process.ExitCode
                                         + " | " + Tail(stderr, 600));
                            return false;
                        }

                        var length = new FileInfo(outputPath).Length;
                        if (length < 50 * 1024)
                        {
                            _logger.Warn("预览文件过小: " + length + " 字节\n" + Tail(stderr, 400));
                            return false;
                        }

                        _logger.Info("预览转码完成，大小 " + (length / 1024) + "KB");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("预览转码异常", ex);
                    return false;
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        private static string BuildCacheKey(Song song, int duration, VocalOption vocal)
        {
            var raw = (song.MusicNo ?? "0") + "_" + duration + "_" + (int)vocal;
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(raw));
                var builder = new StringBuilder(16);
                for (var i = 0; i < 8; i++) builder.Append(bytes[i].ToString("x2"));
                return "pv_" + builder;
            }
        }

        private static void Report(IProgress<Tuple<int, string>> progress, int percent, string stage)
        {
            if (progress == null) return;
            progress.Report(Tuple.Create(Math.Max(0, Math.Min(100, percent)), stage));
        }

        private static string Quote(string value) => "\"" + (value ?? string.Empty) + "\"";

        private static string Tail(string value, int length)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= length ? value : value.Substring(value.Length - length);
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
    }
}
