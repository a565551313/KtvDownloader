using System.Threading;
using System.Threading.Tasks;

namespace KtvDownloader.Services.Interfaces
{
    /// <summary>
    /// 取址接口的返回结果。
    /// </summary>
    public sealed class SongUrlResult
    {
        private SongUrlResult(bool success, string url, string sourceHost, string error)
        {
            Success = success;
            Url = url ?? string.Empty;
            SourceHost = sourceHost ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }

        /// <summary>可直接下载的媒体地址。</summary>
        public string Url { get; }

        /// <summary>命中的源站，如 http://zk.ktv1.top:6677。</summary>
        public string SourceHost { get; }

        /// <summary>失败原因。</summary>
        public string Error { get; }

        public static SongUrlResult Ok(string url, string host) => new SongUrlResult(true, url, host, string.Empty);

        public static SongUrlResult Fail(string error) => new SongUrlResult(false, string.Empty, string.Empty, error);
    }

    /// <summary>
    /// 歌曲取址服务。还原官方 App 的 i.php + do.php 协议。
    /// </summary>
    public interface ISongUrlService
    {
        /// <summary>
        /// 按歌号解析出可下载的媒体地址。
        /// 内部会依次尝试多个源站，失败源自动降到队尾。
        /// </summary>
        Task<SongUrlResult> ResolveAsync(
            string musicNo,
            string resolution = "720",
            bool preferH265 = false,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>清空 token 缓存。断网恢复或换源后调用。</summary>
        void ResetSession();
    }
}
