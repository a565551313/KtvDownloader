using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Models;

namespace KtvDownloader.Services.Interfaces
{
    /// <summary>
    /// 预览准备结果。
    /// </summary>
    public sealed class PreviewSource
    {
        private PreviewSource(bool success, string playablePath, string error, bool fromCache)
        {
            Success = success;
            PlayablePath = playablePath;
            Error = error;
            FromCache = fromCache;
        }

        public bool Success { get; }

        /// <summary>可直接交给播放器打开的本地 MP4 路径。</summary>
        public string PlayablePath { get; }

        public string Error { get; }

        /// <summary>是否复用了已下载的文件。</summary>
        public bool FromCache { get; }

        public static PreviewSource Ok(string path, bool fromCache) =>
            new PreviewSource(true, path, string.Empty, fromCache);

        public static PreviewSource Fail(string error) =>
            new PreviewSource(false, null, error ?? "未知错误", false);
    }

    /// <summary>
    /// 预览服务。
    /// <para>
    /// 为什么需要它：源站下发的是 Thunder 加密的 MPEG-TS，
    /// 播放器不认这个格式 —— 必须先下载并解密才能播。
    /// 因此"预览"本质上是「取一小段 → 解密 → 交给 libVLC 播放」。
    /// </para>
    /// <para>
    /// 为了不浪费流量，预览只处理前 N 秒（默认 30 秒），产物会缓存复用，
    /// 缓存目录有容量上限，超出后自动淘汰最久未访问的文件。
    /// </para>
    /// <para>
    /// 早期还有一个 <c>PrepareAsync</c>（解密后再转 MP4 给 WebBrowser 播），
    /// 改用 libVLC 后已无调用者并删除 —— 转码能力在 <c>IMediaService</c> 里。
    /// </para>
    /// </summary>
    public interface IPreviewService
    {
        /// <summary>
        /// 为 VLC 播放准备可播文件：只做「取址 → 下载 → 解密」，**不转码**。
        /// <para>
        /// libVLC 原生支持 MPEG-TS + mp2，解密后的 .ts 可以直接播放，
        /// 因此这条路径比早期的「转 MP4」方案快一个数量级（省掉整段转码）。
        /// </para>
        /// </summary>
        Task<PreviewSource> PreparePlayableAsync(
            Song song,
            int durationSeconds,
            CancellationToken cancellationToken,
            IProgress<Tuple<int, string>> progress);

        /// <summary>清空预览缓存目录。</summary>
        void ClearCache();

        /// <summary>预览缓存目录。</summary>
        string CacheDirectory { get; }
    }
}
