using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Infrastructure.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KtvDownloader.Tests
{
    /// <summary>
    /// 重试策略。
    /// <para>
    /// 这里钉住的是一个真实回归：早期 <c>ExecuteAsync</c> 写的是
    /// <c>catch (OperationCanceledException) { throw; }</c>，
    /// 而 <see cref="TaskCanceledException"/> 派生自它，
    /// 于是 <b>HttpClient 超时被当成用户取消直接抛出</b>，
    /// 超时重试那条分支永远走不到 —— CDN 抖一下就报"下载失败"。
    /// </para>
    /// </summary>
    [TestClass]
    public class RetryPolicyTests
    {
        [TestMethod]
        public void 状态码_5xx与429与408可重试()
        {
            Assert.IsTrue(RetryPolicy.IsRetryable(HttpStatusCode.BadGateway));
            Assert.IsTrue(RetryPolicy.IsRetryable(HttpStatusCode.ServiceUnavailable));
            Assert.IsTrue(RetryPolicy.IsRetryable(HttpStatusCode.GatewayTimeout));
            Assert.IsTrue(RetryPolicy.IsRetryable(HttpStatusCode.InternalServerError));
            Assert.IsTrue(RetryPolicy.IsRetryable((HttpStatusCode)599));
            Assert.IsTrue(RetryPolicy.IsRetryable(HttpStatusCode.RequestTimeout));
            Assert.IsTrue(RetryPolicy.IsRetryable((HttpStatusCode)429));
        }

        [TestMethod]
        public void 状态码_其余4xx重试也没用()
        {
            Assert.IsFalse(RetryPolicy.IsRetryable(HttpStatusCode.NotFound));
            Assert.IsFalse(RetryPolicy.IsRetryable(HttpStatusCode.Forbidden));
            Assert.IsFalse(RetryPolicy.IsRetryable(HttpStatusCode.BadRequest));
            Assert.IsFalse(RetryPolicy.IsRetryable(HttpStatusCode.OK));
            Assert.IsFalse(RetryPolicy.IsRetryable(HttpStatusCode.Moved));
        }

        [TestMethod]
        public void 异常_网络层故障可重试()
        {
            Assert.IsTrue(RetryPolicy.IsRetryable(new HttpRequestException("boom")));
            Assert.IsTrue(RetryPolicy.IsRetryable(new IOException("reset")));
            Assert.IsTrue(RetryPolicy.IsRetryable(new WebException("timeout")));
            Assert.IsTrue(RetryPolicy.IsRetryable(new System.Net.Sockets.SocketException()));
        }

        [TestMethod]
        public void 异常_HttpClient超时形态可重试()
        {
            // HttpClient 超时抛的就是一个不带令牌的 TaskCanceledException。
            Assert.IsTrue(RetryPolicy.IsRetryable(new TaskCanceledException()));
        }

        [TestMethod]
        public void 异常_用户取消携带已取消的令牌时不重试()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.IsFalse(RetryPolicy.IsRetryable(new OperationCanceledException(cts.Token)));
            }
        }

        [TestMethod]
        public void 异常_业务异常不重试()
        {
            Assert.IsFalse(RetryPolicy.IsRetryable(new InvalidOperationException("业务错误")));
            Assert.IsFalse(RetryPolicy.IsRetryable(null));
        }

        [TestMethod]
        public void 退避_指数增长且封顶16秒()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(2), RetryPolicy.GetDelay(1));
            Assert.AreEqual(TimeSpan.FromSeconds(4), RetryPolicy.GetDelay(2));
            Assert.AreEqual(TimeSpan.FromSeconds(8), RetryPolicy.GetDelay(3));
            Assert.AreEqual(TimeSpan.FromSeconds(16), RetryPolicy.GetDelay(4));
            Assert.AreEqual(TimeSpan.FromSeconds(16), RetryPolicy.GetDelay(99));
            Assert.AreEqual(TimeSpan.FromSeconds(2), RetryPolicy.GetDelay(0));
            Assert.AreEqual(TimeSpan.FromSeconds(2), RetryPolicy.GetDelay(-5));
        }

        [TestMethod]
        public async Task 执行_成功时只调一次()
        {
            var calls = 0;

            var result = await RetryPolicy.ExecuteAsync<int>(attempt =>
            {
                calls++;
                return Task.FromResult(attempt * 10);
            });

            Assert.AreEqual(10, result);
            Assert.AreEqual(1, calls);
        }

        [TestMethod]
        public async Task 执行_超时后重试并最终成功()
        {
            var calls = 0;
            var retried = 0;

            var result = await RetryPolicy.ExecuteAsync<string>(attempt =>
            {
                calls++;
                if (attempt < 2) throw new TaskCanceledException();
                return Task.FromResult("ok");
            }, 3, (attempt, reason) => retried++);

            Assert.AreEqual("ok", result);
            Assert.AreEqual(2, calls, "第一次超时、第二次成功");
            Assert.AreEqual(1, retried, "应该恰好回调一次 onRetry");
        }

        [TestMethod]
        public async Task 执行_不可重试的异常立即抛出()
        {
            var calls = 0;

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
                await RetryPolicy.ExecuteAsync<int>(attempt =>
                {
                    calls++;
                    throw new InvalidOperationException("业务错误");
                }, 5));

            Assert.AreEqual(1, calls, "不可重试就不该再试第二次");
        }

        [TestMethod]
        public async Task 执行_重试次数用尽后抛出最后一个异常()
        {
            var calls = 0;

            await Assert.ThrowsExceptionAsync<HttpRequestException>(async () =>
                await RetryPolicy.ExecuteAsync<int>(attempt =>
                {
                    calls++;
                    throw new HttpRequestException("第 " + attempt + " 次失败");
                }, 2));

            Assert.AreEqual(2, calls);
        }

        [TestMethod]
        public async Task 执行_调用方取消时立刻抛出且不重试()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var calls = 0;

                await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
                    await RetryPolicy.ExecuteAsync<int>(attempt =>
                    {
                        calls++;
                        return Task.FromResult(1);
                    }, 5, null, cts.Token));

                Assert.AreEqual(0, calls, "令牌已取消就不该进操作");
            }
        }

        [TestMethod]
        public async Task 执行_操作中途取消调用方令牌时不再重试()
        {
            using (var cts = new CancellationTokenSource())
            {
                var calls = 0;

                // 注意断言的是 TaskCanceledException 本身：
                // MSTest 的 ThrowsException 要求**精确类型**，写父类会判失败。
                await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () =>
                    await RetryPolicy.ExecuteAsync<int>(attempt =>
                    {
                        calls++;
                        cts.Cancel();
                        // 形态和 HttpClient 超时一模一样，但这次是调用方自己取消的。
                        throw new TaskCanceledException();
                    }, 5, null, cts.Token));

                Assert.AreEqual(1, calls, "调用方取消后必须停手");
            }
        }

        [TestMethod]
        public async Task 执行_次数小于1时按1次处理()
        {
            var calls = 0;

            var result = await RetryPolicy.ExecuteAsync<int>(attempt =>
            {
                calls++;
                return Task.FromResult(7);
            }, 0);

            Assert.AreEqual(7, result);
            Assert.AreEqual(1, calls);
        }

        [TestMethod]
        public async Task 执行_操作为null时抛参数异常()
        {
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () =>
                await RetryPolicy.ExecuteAsync<int>(null));
        }
    }
}
