using System.Threading;
using System.Threading.Tasks;

namespace KtvDownloader.Infrastructure.Http
{
    /// <summary>
    /// HTTP 响应结果。
    /// </summary>
    public sealed class HttpResult
    {
        public HttpResult(int statusCode, string body, string error)
        {
            StatusCode = statusCode;
            Body = body ?? string.Empty;
            Error = error ?? string.Empty;
        }

        /// <summary>HTTP 状态码。0 表示请求本身失败（网络/超时）。</summary>
        public int StatusCode { get; }

        /// <summary>响应正文。</summary>
        public string Body { get; }

        /// <summary>错误信息，仅 StatusCode=0 时有值。</summary>
        public string Error { get; }

        public bool IsSuccess => StatusCode >= 200 && StatusCode <= 299;

        /// <summary>是否被源站风控拦截（403）。</summary>
        public bool IsBlocked => StatusCode == 403;

        public static HttpResult Fail(string error) => new HttpResult(0, string.Empty, error);
    }

    /// <summary>
    /// 干净的 HTTP 通道。
    /// <para>
    /// 歌曲源（zk.ktv1.top 等）的风控只放行「干净」请求：只带 <c>Host</c> 与 <c>Accept: */*</c>。
    /// 一旦带上 <c>User-Agent</c>、<c>Connection</c>、<c>Accept-Encoding</c> 等默认头，
    /// 立即返回 403「检测到异常行为」。
    /// </para>
    /// <para>
    /// 因此这里不用 <see cref="System.Net.Http.HttpClient"/>（它无法完全去掉默认头），
    /// 而是手写 HTTP/1.1 报文，通过裸 Socket 发送，与官方 App 的抓包一致。
    /// </para>
    /// </summary>
    public interface ICleanHttpClient
    {
        /// <summary>发起干净的 GET 请求。</summary>
        Task<HttpResult> GetAsync(string url, int timeoutMs = 10000, CancellationToken cancellationToken = default(CancellationToken));
    }
}
