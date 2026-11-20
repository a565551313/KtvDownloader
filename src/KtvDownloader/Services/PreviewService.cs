using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Infrastructure.Http;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Models;
using KtvDownloader.Services.Interfaces;

namespace KtvDownloader.Services
{
    /// <summary>
    /// 预览服务实现。
    /// <para>
    /// 流程：取址 → 下载前 N 秒 → Thunder 解密 → 交给 libVLC 直接播。
    /// 生成的 .ts 放在缓存目录，同一首歌再次预览直接复用。
    /// </para>
    /// <para>
    /// 不再转码：libVLC 原生支持 MPEG-TS + mp2，解密后可以直接播，
    /// 省掉整段 ffmpeg 转码，首屏从 3 秒以上降到接近 0 秒（docs §7.4）。
    /// 因此本类**不依赖** ffmpeg，也不需要 <c>IMediaService</c>。
    /// </para>
    /// <para>
    /// 历史上的 <c>PrepareAsync</c>（下载 → 解密 → 转 MP4）已删除：
    /// 它没有任何调用者，而且未加密分支写成了"先删源文件再使用源文件"，
    /// 一旦被启用必然转码失败。需要转码预览时请走 <c>MediaService</c>。
    /// </para>
    /// </summary>
    public sealed class PreviewService : IPreviewService, IDisposable
    {
        /// <summary>预览片段默认时长（秒）。</summary>
        private const int DefaultDurationSeconds = 30;

        /// <summary>缓存文件小于这个尺寸视为无效（下载中断的残片）。</summary>
        private const long MinimumValidCacheBytes = 200 * 1024;

        /// <summary>预览缓存目录的容量上限。超出后按最久未访问淘汰。</summary>
        private const long CacheBudgetBytes = 512L * 1024 * 1024;

        /// <summary>缓存文件数量上限。</summary>
        private const int CacheFileLimit = 200;

        private readonly ILogger _logger;
        private readonly ISongUrlService _urlService;
        private readonly ITsDecryptService _decryptService;
        private readonly HttpClient _httpClient;

        private int _disposed;

        public PreviewService(
            ILogger logger,
            ISongUrlService urlService,
            ITsDecryptService decryptService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _urlService = urlService ?? throw new ArgumentNullException(nameof(urlService));
            _decryptService = decryptService ?? throw new ArgumentNullException(nameof(decryptService));

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

                var deleted = 0;
                foreach (var file in Directory.GetFiles(CacheDirectory))
                {
                    try { File.Delete(file); deleted++; } catch { }
                }
                _logger.Info("预览缓存已清空，删除 " + deleted + " 个文件");
            }
            catch (Exception ex)
            {
                _logger.Warn("清空预览缓存失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 缓存目录会无限增长（每首歌 8MB 起），这里按"最久未访问优先"淘汰到预算内。
        /// 每次准备完预览顺手做一次，避免用户手动清理。
        /// </summary>
        private void TrimCache()
        {
            try
            {
                if (!Directory.Exists(CacheDirectory)) return;

                var files = new DirectoryInfo(CacheDirectory)
                    .GetFiles()
                    .OrderBy(f => f.LastAccessTimeUtc)
                    .ToList();

                var total = files.Sum(f => f.Length);
                var removed = 0;

                foreach (var file in files)
                {
                    if (total <= CacheBudgetBytes && files.Count - removed <= CacheFileLimit) break;

                    try
                    {
                        var size = file.Length;
                        file.Delete();
                        total -= size;
                        removed++;
                    }
                    catch
                    {
                        // 正在播放的文件删不掉，跳过。
                    }
                }

                if (removed > 0)
                    _logger.Info("预览缓存淘汰了 " + removed + " 个旧文件");
            }
            catch (Exception ex)
            {
                _logger.Warn("预览缓存淘汰失败: " + ex.Message);
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
            if (Volatile.Read(ref _disposed) != 0)
                return PreviewSource.Fail("预览服务已释放");

            var duration = durationSeconds < 0 ? DefaultDurationSeconds : durationSeconds;
            var fullPreview = duration == 0;

            try
            {
                if (!Directory.Exists(CacheDirectory)) Directory.CreateDirectory(CacheDirectory);

                // 缓存键不含音轨 —— TS 本身含全部音轨，切换由 VLC 完成。
                var cacheKey = BuildCacheKey(song, duration, VocalOption.KeepBoth);
                var cachedTs = Path.Combine(CacheDirectory, cacheKey + ".play.ts");

                if (File.Exists(cachedTs) && new FileInfo(cachedTs).Length > MinimumValidCacheBytes)
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
                catch (OperationCanceledException)
                {
                    throw;
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
                    // 不是我们认识的加密格式（例如 mp3、旧式 .ls）：原样改名交给播放器。
                    // ⚠️ 不能先删再改名 —— 早期版本这里写反了，未加密文件必然失败。
                    SafeDelete(cachedTs);
                    try
                    {
                        File.Move(rawPath, cachedTs);
                    }
                    catch (Exception ex)
                    {
                        SafeDelete(rawPath);
                        return PreviewSource.Fail("缓存写入失败：" + ex.Message);
                    }
                }

                TrimCache();

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

        /// <summary>
        /// 按 480p 约 250KB/s 估算需要的字节数，留 1.5 倍余量，下限 8MB。
        /// </summary>
        internal static long EstimateBytes(int durationSeconds)
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
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var code = response.StatusCode;
                        _logger.Warn("预览下载 HTTP " + (int)code);

                        if (RetryPolicy.IsRetryable(code))
                            throw new HttpRequestException(string.Format("CDN 返回 {0}", (int)code));

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

                            var remaining = targetBytes - received;
                            if (remaining <= 0) break;

                            var want = (int)Math.Min(buffer.Length, remaining);

                            var read = await source.ReadAsync(buffer, 0, want, cancellationToken)
                                .ConfigureAwait(false);
                            if (read <= 0) break;

                            await destination.WriteAsync(buffer, 0, read, cancellationToken)
                                .ConfigureAwait(false);
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
        /// 缓存键：歌号 + 时长 + 音轨模式，取 MD5 前 8 字节。
        /// 用哈希而不是原始歌名，避免非法字符与超长路径。
        /// </summary>
        internal static string BuildCacheKey(Song song, int duration, VocalOption vocal)
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

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try { _httpClient.Dispose(); } catch { }
        }
    }
}
