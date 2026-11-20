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
    /// <para>
    /// 排队与并发不在这个类里，见 <see cref="DownloadQueue"/>。
    /// 本类持有 <see cref="HttpClient"/>，因此实现了 <see cref="IDisposable"/>；
    /// 重建服务时必须 Dispose 旧实例，否则连接池会一直泄漏。
    /// </para>
    /// </summary>
    public sealed class DownloadService : IDownloadService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly ISongUrlService _urlService;
        private readonly ITsDecryptService _decryptService;
        private readonly IMediaService _mediaService;
        private HttpClient _httpClient;

        private int _maxConcurrency = 2;

        /// <summary>0 = 未释放，1 = 已释放。用 int 才能配合 <see cref="Interlocked"/> 做原子交换。</summary>
        private int _disposed;

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

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            var client = _httpClient;
            _httpClient = null;
            if (client != null)
            {
                try { client.Dispose(); } catch { }
            }
        }

        /// <summary>取当前的 HttpClient，已释放时抛异常（避免 NullReferenceException）。</summary>
        private HttpClient Client
        {
            get
            {
                var client = _httpClient;
                if (client == null) throw new ObjectDisposedException(nameof(DownloadService));
                return client;
            }
        }

        public async Task<DownloadTask> ExecuteAsync(
            DownloadTask task,
            DownloadOptions options,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(DownloadService));

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

                var safeBaseName = SongFileNaming.BuildBaseFileName(task.Song);
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

                using (var response = await Client.SendAsync(
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

            // ⚠️ 开了烧录就**不能**走"不转码直接改名"这条路：
            // 那样水印会静默不生效 —— 界面上看不出任何异常，用户拿到手才发现文件是干净的。
            // 烧录必须重编码，所以强制走 MP4 输出，并把这个覆盖行为记下来。
            if (!options.ConvertToMp4 && !options.NeedsBurnIn)
            {
                var finalPath = Path.Combine(outputDirectory,
                    safeBaseName + Path.GetExtension(sourcePath));
                MoveOver(sourcePath, finalPath);
                return finalPath;
            }

            if (!options.ConvertToMp4)
            {
                _logger.Warn("已开启叠加层烧录，本次忽略「不转码」设置，输出 MP4");
                progress?.Report(new DownloadProgress(
                    DownloadState.Converting, 0, null,
                    "烧录叠加层需要重编码，本次输出 MP4"));
            }

            if (!_mediaService.IsFfmpegAvailable)
            {
                _logger.Warn("ffmpeg 不可用，保留原始文件");
                var fallback = Path.Combine(outputDirectory,
                    safeBaseName + Path.GetExtension(sourcePath));
                MoveOver(sourcePath, fallback);
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
                    options.AudioBitrateKbps, task.Song.Accomp, cancellationToken)
                    .ConfigureAwait(false);

                if (split.HasAny)
                {
                    var produced = split.GetAllPaths();
                    _logger.Info("音轨分离产出 " + produced.Length + " 个文件: " +
                                 string.Join(" | ", produced));

                    await ApplyBurnInAsync(produced, options, progress, cancellationToken)
                        .ConfigureAwait(false);

                    // 返回给队列的那一条要稳定：优先原唱版，没有再取伴奏版。
                    // 之前是 produced[0]，顺序取决于枚举实现，界面显示的成品路径会飘。
                    // 烧录是原地替换（同名），所以这两个路径依然有效。
                    return split.OriginalPath ?? split.AccompanimentPath;
                }

                // 分离失败则退回普通转码。
                _logger.Warn("音轨分离未产出文件，退回普通转码");
            }

            var mp4Path = Path.Combine(outputDirectory, safeBaseName + ".mp4");
            var converted = await _mediaService.ConvertToMp4Async(
                sourcePath, mp4Path, VocalOption.KeepBoth,
                options.AudioBitrateKbps, cancellationToken).ConfigureAwait(false);

            if (!converted) return null;

            await ApplyBurnInAsync(new[] { mp4Path }, options, progress, cancellationToken)
                .ConfigureAwait(false);

            return mp4Path;
        }

        /// <summary>
        /// 给已经产出的 MP4 逐个烧录叠加层（原地替换）。
        /// <para>
        /// <b>为什么做成后处理，而不是在转码时一次搞定：</b>
        /// 第一步 TS → MP4 用的是 <c>-c:v copy</c>（视频不重编码，只是换容器），
        /// 第二步才把视频解码、叠加、重编码一次。两步加起来视频<b>总共只编码一遍</b>，
        /// 和"一步到位"的耗时一样；而且中间产物的音频已经是 AAC，
        /// 第二步可以直接 <c>-c:a copy</c>，不会凭空多一代音频损失。
        /// 更重要的是不用把 SplitVocal 那套音轨/声道逻辑再抄一遍。
        /// </para>
        /// <para>
        /// ⚠️ <b>烧录失败不算下载失败。</b>用户等了几分钟把歌下下来，
        /// 因为一张水印图被删了就整首丢掉，这个代价不对等。
        /// 保留没烧录的成品 + 记 WARN + 在进度消息里说清楚，让他自己决定要不要重来。
        /// </para>
        /// </summary>
        private async Task ApplyBurnInAsync(
            string[] paths,
            DownloadOptions options,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            if (paths == null || paths.Length == 0) return;
            if (!options.NeedsBurnIn) return;

            if (!_mediaService.IsFfmpegAvailable)
            {
                _logger.Warn("ffmpeg 不可用，跳过叠加层烧录");
                return;
            }

            // SplitBoth 会产出原唱版和伴奏版两个文件，而它们的**视频画面完全相同**
            // （SplitVocal 的两个抽取路径都是 -c:v copy）。各烧一遍等于把同一段视频
            // 重编码两次：一首 4 分钟的歌从 1–3 分钟变成 2–6 分钟，画质还多损失一代。
            // 所以只烧第一个，剩下的把烧好的视频流直接拷过来配上各自的音轨（秒级）。
            string burnedDonor = null;

            for (var i = 0; i < paths.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var path = paths[i];
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

                var index = i;
                var burnProgress = new Progress<double>(percent =>
                {
                    progress?.Report(new DownloadProgress(
                        DownloadState.Converting, (int)Math.Round(percent), null,
                        string.Format("正在烧录叠加层 {0}/{1}　{2:0}%",
                            index + 1, paths.Length, percent)));
                });

                // 先写到临时文件再换过去：直接写目标文件的话，中途失败会留下一个
                // 半截的 MP4，把第一步产出的好文件也毁了。
                var tempPath = path + ".burn.mp4";

                // ── 快路径：已经有一个烧好的同源视频，拷过来就行，不重编码 ──
                if (burnedDonor != null)
                {
                    try
                    {
                        var reused = await _mediaService.ReuseBurnedVideoAsync(
                            burnedDonor, path, tempPath, cancellationToken).ConfigureAwait(false);

                        if (reused)
                        {
                            MoveOver(tempPath, path);
                            _logger.Info("叠加层已复用烧好的视频流: " + path);
                            progress?.Report(new DownloadProgress(
                                DownloadState.Converting, 100, null,
                                string.Format("叠加层已应用 {0}/{1}（复用视频流，未重编码）",
                                    index + 1, paths.Length)));
                            continue;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        TryDelete(tempPath);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn("复用视频流失败，改为重新烧录: " + ex.Message);
                        TryDelete(tempPath);
                    }

                    // 复用没成（探测不出来、ffmpeg 报错、文件被占）就掉下去走正常烧录。
                    // 慢一倍总比拿不到成品好。
                }

                try
                {
                    var result = await _mediaService.BurnInAsync(
                        path, tempPath, options.Overlay, VocalOption.KeepBoth,
                        options.AudioBitrateKbps, (IProgress<double>)burnProgress, 0, cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var warning in result.Warnings) _logger.Warn("叠加层: " + warning);

                    // 警告不能只进日志：字体被兜底替换、某张图读不到被跳过，
                    // 都意味着成品和用户以为的不一样。挑第一条塞进进度消息，
                    // 界面上就看得见（全部塞进去会把状态栏挤爆）。
                    var warningHint = result.Warnings.Count > 0
                        ? "　⚠ " + result.Warnings[0]
                        : string.Empty;

                    if (result.NothingToBurn)
                    {
                        _logger.Info("叠加层无可渲染元素，保留原文件: " + result.Error);
                        TryDelete(tempPath);
                        continue;
                    }

                    if (!result.Success)
                    {
                        _logger.Warn("叠加层烧录失败，保留未烧录的成品: " + result.Error);
                        progress?.Report(new DownloadProgress(
                            DownloadState.Converting, 0, null,
                            "叠加层烧录失败，已保留无水印版本" + warningHint));

                        // ffmpeg 中途失败会留下一个半截的临时文件。不删的话
                        // 用户的输出目录里会多出 song.mp4.burn.mp4 这种垃圾，
                        // 而且它可能被曲库扫描当成一首歌捡进去。
                        TryDelete(tempPath);
                        continue;
                    }

                    // 音轨映射交给 BurnIn 内部按源文件实际情况处理，
                    // 这里只负责把烧好的文件换到位。
                    MoveOver(tempPath, path);
                    _logger.Info("叠加层已烧录: " + path);

                    // 这个文件现在带着烧好的视频流，后面同源的几个可以复用它。
                    if (burnedDonor == null) burnedDonor = path;

                    if (warningHint.Length > 0)
                    {
                        progress?.Report(new DownloadProgress(
                            DownloadState.Converting, 100, null, "叠加层已烧录" + warningHint));
                    }
                }
                catch (OperationCanceledException)
                {
                    TryDelete(tempPath);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warn("叠加层烧录异常，保留未烧录的成品: " + ex.Message);
                    TryDelete(tempPath);
                }
            }
        }

        private void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.Warn("删除临时文件失败: " + path + " - " + ex.Message);
            }
        }

        #endregion

        #region 工具

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

        /// <summary>
        /// 把 <paramref name="sourcePath"/> 移到 <paramref name="targetPath"/>，目标已存在时覆盖 ——
        /// 但**一定会记一条 WARN**。
        /// <para>
        /// 原来这里是 <c>if (File.Exists(x)) SafeDelete(x); File.Move(...);</c>：
        /// 一声不吭就把用户之前下好的同名文件删掉了。同名不同内容完全可能
        /// （曲库更新后歌号变了、用户手工改过文件名、或者上次下的是伴奏这次要原唱），
        /// 删了还查不到痕迹，只能靠用户自己发现"咦我那首歌呢"。
        /// </para>
        /// </summary>
        private void MoveOver(string sourcePath, string targetPath)
        {
            if (File.Exists(targetPath))
            {
                long oldSize = 0;
                long newSize = 0;
                try
                {
                    oldSize = new FileInfo(targetPath).Length;
                    if (File.Exists(sourcePath)) newSize = new FileInfo(sourcePath).Length;
                }
                catch
                {
                    // 拿不到大小不影响覆盖本身。
                }

                _logger.Warn(string.Format("目标文件已存在，将被覆盖: {0}（{1} → {2}）",
                    targetPath,
                    Models.DownloadProgress.FormatSize(oldSize),
                    Models.DownloadProgress.FormatSize(newSize)));

                SafeDelete(targetPath);
            }

            File.Move(sourcePath, targetPath);
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
