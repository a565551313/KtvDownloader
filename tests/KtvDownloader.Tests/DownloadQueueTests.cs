using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Models;
using KtvDownloader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KtvDownloader.Tests
{
    /// <summary>
    /// 下载队列。
    /// <para>
    /// 回归的重点是 <see cref="DownloadQueue.ActiveTaskCount"/>：
    /// 早期实现把任务放进字典后**从不摘除**，于是这个数字只涨不降 ——
    /// 退出时永远弹"还有下载任务未完成"，状态栏永远卡在"下载中"，
    /// "全部取消"永远认为队列非空，字典还一路泄漏到进程结束。
    /// </para>
    /// </summary>
    [TestClass]
    public class DownloadQueueTests
    {
        private static DownloadTask NewTask(string musicNo)
        {
            return new DownloadTask(TestSongs.Create(musicNo: musicNo), @"C:\Music");
        }

        private static DownloadOptions NewOptions()
        {
            return new DownloadOptions { Resolution = "720", Vocal = VocalOption.KeepBoth };
        }

        [TestMethod]
        public void 计数_任务全部结束后归零()
        {
            // 用一道闸门把任务全部卡住，这样"入队后计数 == 6"才是确定的
            // （否则跑得快的任务可能已经收尾，断言就成了碰运气）。
            var gate = new TaskCompletionSource<bool>();
            var service = new FakeDownloadService
            {
                Handler = async (task, ct) =>
                {
                    await gate.Task.ConfigureAwait(false);
                    task.State = DownloadState.Completed;
                    return task;
                }
            };

            using (var queue = new DownloadQueue(NullLogger.Instance, service, 4))
            {
                for (var i = 0; i < 6; i++) queue.Enqueue(NewTask("n" + i), NewOptions());

                Assert.AreEqual(6, queue.ActiveTaskCount, "刚入队时应该全部在册");
                Assert.AreEqual(6, queue.GetTasks().Count);

                gate.SetResult(true);

                Wait.Until(() => queue.ActiveTaskCount == 0, 10000, "任务结束后计数没有归零");

                Assert.AreEqual(6, service.StartedCount);
                Assert.AreEqual(0, queue.GetTasks().Count);
            }
        }

        [TestMethod]
        public void 计数_失败的任务也要摘掉()
        {
            var service = new FakeDownloadService
            {
                Handler = (task, ct) =>
                {
                    task.State = DownloadState.Failed;
                    task.ErrorMessage = "模拟失败";
                    return Task.FromResult(task);
                }
            };

            using (var queue = new DownloadQueue(NullLogger.Instance, service, 2))
            {
                queue.Enqueue(NewTask("a"), NewOptions());
                queue.Enqueue(NewTask("b"), NewOptions());

                Wait.Until(() => queue.ActiveTaskCount == 0, 10000, "失败任务没有从计数里摘掉");
            }
        }

        [TestMethod]
        public void 计数_抛异常的任务不能把队列卡死()
        {
            var service = new FakeDownloadService
            {
                Handler = (task, ct) =>
                {
                    throw new InvalidOperationException("模拟未处理异常");
                }
            };

            using (var queue = new DownloadQueue(NullLogger.Instance, service, 2))
            {
                queue.Enqueue(NewTask("a"), NewOptions());

                Wait.Until(() => queue.ActiveTaskCount == 0, 10000,
                    "任务抛异常后计数没有归零 —— 队列会一直显示\"下载中\"");
            }
        }

        [TestMethod]
        public void 并发_不超过上限()
        {
            var service = new FakeDownloadService
            {
                Handler = async (task, ct) =>
                {
                    await Task.Delay(80, ct).ConfigureAwait(false);
                    task.State = DownloadState.Completed;
                    return task;
                }
            };

            using (var queue = new DownloadQueue(NullLogger.Instance, service, 2))
            {
                for (var i = 0; i < 8; i++) queue.Enqueue(NewTask("n" + i), NewOptions());

                Wait.Until(() => queue.ActiveTaskCount == 0, 20000, "任务没有全部跑完");

                Assert.IsTrue(service.ObservedPeakConcurrency <= 2,
                    "并发上限 2，实际同时跑了 " + service.ObservedPeakConcurrency + " 个");
                Assert.AreEqual(8, service.StartedCount);
            }
        }

        [TestMethod]
        public void 并发数_热调整立即生效且不打断在跑的任务()
        {
            var started = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<bool>();
            var completed = 0;

            var service = new FakeDownloadService
            {
                Handler = async (task, ct) =>
                {
                    started.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    task.State = DownloadState.Completed;
                    Interlocked.Increment(ref completed);
                    return task;
                }
            };

            using (var queue = new DownloadQueue(NullLogger.Instance, service, 1))
            {
                Assert.AreEqual(1, queue.MaxConcurrency);

                queue.Enqueue(NewTask("blocking"), NewOptions());

                // 等第一个任务真的跑起来。
                Assert.IsTrue(started.Task.Wait(5000), "任务没有启动");

                queue.SetMaxConcurrency(4);
                Assert.AreEqual(4, queue.MaxConcurrency);
                Assert.AreEqual(4, service.MaxConcurrency, "并发数应该同步给下载服务");

                // 关键：改并发数不能把在跑的任务取消掉。
                Assert.AreEqual(1, queue.ActiveTaskCount);

                release.SetResult(true);

                Wait.Until(() => queue.ActiveTaskCount == 0, 10000, "改并发数后任务没能正常结束");
                Assert.AreEqual(1, Volatile.Read(ref completed));
            }
        }

        [TestMethod]
        public void 并发数_夹在1到8()
        {
            var service = new FakeDownloadService();
            using (var queue = new DownloadQueue(NullLogger.Instance, service, 2))
            {
                queue.SetMaxConcurrency(0);
                Assert.AreEqual(1, queue.MaxConcurrency);

                queue.SetMaxConcurrency(99);
                Assert.AreEqual(8, queue.MaxConcurrency);

                queue.SetMaxConcurrency(-3);
                Assert.AreEqual(1, queue.MaxConcurrency);
            }
        }

        [TestMethod]
        public void 构造_非法并发数被夹住()
        {
            var service = new FakeDownloadService();
            using (var queue = new DownloadQueue(NullLogger.Instance, service, 0))
            {
                Assert.AreEqual(1, queue.MaxConcurrency);
            }
        }

        [TestMethod]
        public void 取消_单个任务从队列里消失()
        {
            var release = new TaskCompletionSource<bool>();
            var service = new FakeDownloadService
            {
                Handler = async (task, ct) =>
                {
                    await release.Task.ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    task.State = DownloadState.Completed;
                    return task;
                }
            };

            using (var queue = new DownloadQueue(NullLogger.Instance, service, 4))
            {
                var first = NewTask("a");
                var second = NewTask("b");

                queue.Enqueue(first, NewOptions());
                queue.Enqueue(second, NewOptions());

                Assert.AreEqual(2, queue.ActiveTaskCount);

                queue.Cancel(second.Id);
                release.SetResult(true);

                Wait.Until(() => queue.ActiveTaskCount == 0, 10000, "取消后计数没有归零");
            }
        }

        [TestMethod]
        public void 取消全部_清空队列()
        {
            var release = new TaskCompletionSource<bool>();
            var service = new FakeDownloadService
            {
                Handler = async (task, ct) =>
                {
                    await release.Task.ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    task.State = DownloadState.Completed;
                    return task;
                }
            };

            using (var queue = new DownloadQueue(NullLogger.Instance, service, 2))
            {
                for (var i = 0; i < 4; i++) queue.Enqueue(NewTask("n" + i), NewOptions());

                queue.CancelAll();
                release.SetResult(true);

                Wait.Until(() => queue.ActiveTaskCount == 0, 10000, "CancelAll 之后队列没有清空");
            }
        }

        [TestMethod]
        public void 释放后再入队_抛ObjectDisposedException()
        {
            var service = new FakeDownloadService();
            var queue = new DownloadQueue(NullLogger.Instance, service, 2);
            queue.Dispose();

            Assert.ThrowsException<ObjectDisposedException>(() => queue.Enqueue(NewTask("a"), NewOptions()));
        }

        [TestMethod]
        public void 释放_可以被调用两次()
        {
            var service = new FakeDownloadService();
            var queue = new DownloadQueue(NullLogger.Instance, service, 2);

            queue.Enqueue(NewTask("a"), NewOptions());
            Wait.Until(() => queue.ActiveTaskCount == 0, 10000, "任务没有跑完");

            queue.Dispose();
            queue.Dispose();
        }

        [TestMethod]
        public void 入队_空参数直接拒绝()
        {
            var service = new FakeDownloadService();
            using (var queue = new DownloadQueue(NullLogger.Instance, service, 2))
            {
                Assert.ThrowsException<ArgumentNullException>(() => queue.Enqueue(null, NewOptions()));
                Assert.ThrowsException<ArgumentNullException>(() => queue.Enqueue(NewTask("a"), null));
            }
        }

        [TestMethod]
        public void 事件_任务状态变化会广播()
        {
            var service = new FakeDownloadService();
            using (var queue = new DownloadQueue(NullLogger.Instance, service, 2))
            {
                var updates = new List<DownloadState>();
                var queueChanges = 0;

                EventHandler<DownloadTaskEventArgs> onUpdate = (s, e) =>
                {
                    lock (updates) updates.Add(e.Task.State);
                };
                EventHandler onQueueChanged = (s, e) => Interlocked.Increment(ref queueChanges);

                queue.TaskUpdated += onUpdate;
                queue.QueueChanged += onQueueChanged;

                queue.Enqueue(NewTask("a"), NewOptions());

                Wait.Until(() => queue.ActiveTaskCount == 0, 10000, "任务没有跑完");

                lock (updates) Assert.IsTrue(updates.Count > 0, "一次状态广播都没有");
                Assert.IsTrue(Volatile.Read(ref queueChanges) > 0, "入队没有触发 QueueChanged");

                queue.TaskUpdated -= onUpdate;
                queue.QueueChanged -= onQueueChanged;
            }
        }

        [TestMethod]
        public void 快照_可以在枚举时安全并发修改()
        {
            var service = new FakeDownloadService
            {
                Handler = async (task, ct) =>
                {
                    await Task.Delay(30, ct).ConfigureAwait(false);
                    task.State = DownloadState.Completed;
                    return task;
                }
            };

            using (var queue = new DownloadQueue(NullLogger.Instance, service, 4))
            {
                for (var i = 0; i < 20; i++) queue.Enqueue(NewTask("n" + i), NewOptions());

                // GetTasks 返回的是快照，遍历过程中任务陆续结束也不该抛"集合已修改"。
                for (var round = 0; round < 50; round++)
                {
                    foreach (var task in queue.GetTasks())
                    {
                        Assert.IsNotNull(task.Song);
                    }
                    Thread.Sleep(5);
                }

                Wait.Until(() => queue.ActiveTaskCount == 0, 20000, "任务没有全部跑完");
            }
        }
    }
}
