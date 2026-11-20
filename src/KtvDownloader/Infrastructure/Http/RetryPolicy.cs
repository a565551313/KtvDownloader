using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace KtvDownloader.Infrastructure.Http
{
    /// <summary>
    /// 媒体下载重试策略。
    /// <para>
    /// 歌曲 CDN（origjoy / ktvsky / wb66）在高峰期会返回 502/503/504，
    /// 或直接断开连接。这些基本都是**瞬时故障**，等几秒重试即可成功。
    /// 之前没有重试，一次 502 就判定「转码失败」，用户看到的就是莫名其妙失败。
    /// </para>
    /// </summary>
    public static class RetryPolicy
    {
        /// <summary>默认重试次数。</summary>
        public const int DefaultMaxAttempts = 3;

        /// <summary>
        /// 判断这个状态码是否值得重试。
        /// 5xx 是服务端临时问题，429 是限流，408 是超时，都该重试。
        /// 4xx 里除这几个之外的（如 403/404）重试也没用。
        /// </summary>
        public static bool IsRetryable(HttpStatusCode statusCode)
        {
            var code = (int)statusCode;

            if (code == 408) return true;   // Request Timeout
            if (code == 429) return true;   // Too Many Requests
            if (code >= 500 && code <= 599) return true;

            return false;
        }

        /// <summary>
        /// 判断异常是否值得重试。网络层异常（连接断开、超时）都算。
        /// <para>
        /// <see cref="TaskCanceledException"/> 有两种来源，必须区分：
        /// 1. 调用方主动取消 → 它自带的 token 已 canceled → **不能**重试；
        /// 2. <c>HttpClient</c> 超时 → 它自带的 token 是 <c>None</c> → 应该重试。
        /// </para>
        /// </summary>
        public static bool IsRetryable(Exception exception)
        {
            if (exception == null) return false;

            var taskCanceled = exception as TaskCanceledException;
            if (taskCanceled != null)
                return !taskCanceled.CancellationToken.IsCancellationRequested;

            return exception is System.Net.Http.HttpRequestException
                || exception is System.IO.IOException
                || exception is System.Net.Sockets.SocketException
                || exception is System.Net.WebException;
        }

        /// <summary>
        /// 计算第 N 次尝试失败后的等待时长。指数退避：2s, 4s, 8s（上限 16s）。
        /// </summary>
        public static TimeSpan GetDelay(int attempt)
        {
            var seconds = Math.Pow(2, Math.Max(1, attempt));
            return TimeSpan.FromSeconds(Math.Min(seconds, 16));
        }

        /// <summary>
        /// 执行带重试的异步操作。
        /// </summary>
        /// <param name="operation">要执行的操作，参数为尝试序号（从 1 开始）。</param>
        /// <param name="maxAttempts">最大尝试次数。</param>
        /// <param name="onRetry">每次重试前的回调，参数为（尝试序号, 失败原因）。</param>
        /// <param name="cancellationToken">
        /// 调用方的取消令牌。
        /// <para>
        /// ⚠️ 判断"该不该重试"时，只认这个令牌：
        /// 早期版本写的是 <c>catch (OperationCanceledException) { throw; }</c>，
        /// 而 <see cref="TaskCanceledException"/> 派生自它，
        /// 于是 HttpClient 超时被当成用户取消直接抛出，
        /// 下面 <see cref="IsRetryable(Exception)"/> 里那条超时分支永远走不到 —— CDN 抖动导致的超时从不重试。
        /// </para>
        /// </param>
        public static async Task<T> ExecuteAsync<T>(
            Func<int, Task<T>> operation,
            int maxAttempts = DefaultMaxAttempts,
            Action<int, string> onRetry = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            var attempts = Math.Max(1, maxAttempts);
            Exception lastException = null;

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return await operation(attempt).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 用户主动取消：立刻抛出，不重试。
                    if (cancellationToken.IsCancellationRequested) throw;

                    // 超时（TaskCanceledException 但令牌未取消）等瞬时故障：可以重试。
                    if (!IsRetryable(ex)) throw;

                    lastException = ex;

                    if (attempt >= attempts) throw;

                    if (onRetry != null) onRetry(attempt, ex.Message);

                    await Task.Delay(GetDelay(attempt), cancellationToken).ConfigureAwait(false);
                }
            }

            throw lastException ?? new InvalidOperationException("重试失败");
        }
    }
}
