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
    /// 而浏览器/播放器控件都不认这个格式 —— 必须先下载解密再转 MP4 才能播。
    /// 因此“预览”本质上是「取一小段 → 解密 → 转 MP4 → 播放」。
    /// </para>
    /// <para>
    /// 为了不浪费流量，预览只处理前 N 秒（默认 30 秒），
    /// 生成的临时 MP4 会缓存复用。
    /// </para>
    /// </summary>
    public interface IPreviewService
    {
        /// <summary>
        /// 为指定歌曲准备可播放的预览文件。
        /// </summary>
        /// <param name="song">目标歌曲。</param>
        /// <param name="durationSeconds">预览时长（秒），默认 30。</param>
        /// <param name="progress">进度回调，参数为 0-100 与阶段文案。</param>
        Task<PreviewSource> PrepareAsync(
            Song song,
            int durationSeconds,
            Models.VocalOption vocal,
            IProgress<Tuple<int, string>> progress,
            CancellationToken cancellationToken = default(CancellationToken));


        /// <summary>
        /// 为 VLC 播放准备可播文件：只做「取址 → 下载 → 解密」，**不转码**。
        /// <para>
        /// libVLC 原生支持 MPEG-TS + mp2，解密后的 .ts 可以直接播放，
        /// 因此这条路径比 <see cref="PrepareAsync"/> 快一个数量级（省掉整段转码）。
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
