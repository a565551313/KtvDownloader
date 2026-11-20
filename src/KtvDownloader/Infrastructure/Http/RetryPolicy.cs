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
        /// </summary>
        public static bool IsRetryable(Exception exception)
        {
            if (exception == null) return false;

            return exception is System.Net.Http.HttpRequestException
                || exception is System.IO.IOException
                || exception is System.Net.Sockets.SocketException
                || exception is TaskCanceledException && !((TaskCanceledException)exception).CancellationToken.IsCancellationRequested;
        }

        /// <summary>
        /// 计算第 N 次重试前的等待时长。指数退避：2s, 4s, 8s。
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
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;

                    if (attempt >= attempts || !IsRetryable(ex)) throw;

                    var delay = GetDelay(attempt);
                    if (onRetry != null) onRetry(attempt, ex.Message);

                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }

            throw lastException ?? new InvalidOperationException("重试失败");
        }
    }
}
