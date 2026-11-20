using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Models;
using KtvDownloader.Services.Interfaces;

namespace KtvDownloader.Services
{
    /// <summary>
    /// 下载队列。负责任务排队、并发限制与状态广播。
    /// <para>
    /// 每个任务都有独立的 <see cref="CancellationTokenSource"/> 与 <see cref="IProgress{T}"/>，
    /// 因此批量下载时：
    /// 1. 每首歌的进度各自独立更新，互不干扰；
    /// 2. 取消某一首不影响其它；
    /// 3. 某一首失败，其余继续。
    /// </para>
    /// <para>
    /// ⚠️ 任务结束后**必须**把它从 <see cref="_tasks"/> 里摘掉。
    /// 早期只摘 <c>_cancellations</c> 不摘 <c>_tasks</c>，导致 <see cref="ActiveTaskCount"/>
    /// 只增不减，于是：主界面每次退出都弹"还有下载任务未完成"、
    /// "全部取消"永远认为队列非空、状态栏永久显示"下载中，队列 N 项"。
    /// </para>
    /// </summary>
    public sealed class DownloadQueue : IDownloadQueue, IDisposable
    {
        /// <summary>Dispose 时等待在跑任务收尾的最长时间。</summary>
        private static readonly TimeSpan DisposeWaitTimeout = TimeSpan.FromSeconds(3);

        private readonly ILogger _logger;
        private readonly IDownloadService _downloadService;
        private readonly object _sync = new object();

        private readonly Dictionary<Guid, CancellationTokenSource> _cancellations =
            new Dictionary<Guid, CancellationTokenSource>();

        private readonly Dictionary<Guid, DownloadTask> _tasks = new Dictionary<Guid, DownloadTask>();

        /// <summary>正在执行（含排队等信号量）的任务，供 Dispose 时收尾。</summary>
        private readonly List<Task> _running = new List<Task>();

        /// <summary>因调整并发数而退役的信号量，Dispose 时一并清理。</summary>
        private readonly List<SemaphoreSlim> _retiredGates = new List<SemaphoreSlim>();

        /// <summary>当前的并发闸门。调整并发数时会整体换掉（<see cref="SetMaxConcurrency"/>）。</summary>
        private SemaphoreSlim _gate;

        private int _maxConcurrency;
        private bool _disposed;

        public DownloadQueue(ILogger logger, IDownloadService downloadService, int maxConcurrency)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _downloadService = downloadService ?? throw new ArgumentNullException(nameof(downloadService));

            _maxConcurrency = Clamp(maxConcurrency);
            _downloadService.MaxConcurrency = _maxConcurrency;
            _gate = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        }

        public event EventHandler<DownloadTaskEventArgs> TaskUpdated;

        public event EventHandler QueueChanged;

        /// <summary>当前排队与运行中的任务数。任务结束（成功/失败/取消）后立即归零。</summary>
        public int ActiveTaskCount
        {
            get
            {
                lock (_sync)
                {
                    return _tasks.Count;
                }
            }
        }

        /// <summary>当前并发上限。</summary>
        public int MaxConcurrency
        {
            get
            {
                lock (_sync)
                {
                    return _maxConcurrency;
                }
            }
        }

        /// <summary>当前排队/运行中的任务快照，供界面显示。</summary>
        public IReadOnlyList<DownloadTask> GetTasks()
        {
            lock (_sync)
            {
                return _tasks.Values.ToList();
            }
        }

        /// <summary>
        /// 调整并发数，**立即生效**。
        /// <para>
        /// <see cref="SemaphoreSlim"/> 的上限建完就不能改，所以这里是换一个新闸门：
        /// 之后启动的任务用新闸门，已经在跑的任务继续用它们捕获到的旧闸门并在结束时释放它。
        /// 旧闸门进入 <see cref="_retiredGates"/>，Dispose 时统一清理。
        /// </para>
        /// </summary>
        public void SetMaxConcurrency(int value)
        {
            var concurrency = Clamp(value);

            lock (_sync)
            {
                _downloadService.MaxConcurrency = concurrency;

                if (concurrency == _maxConcurrency) return;

                _logger.Info(string.Format("下载并发数 {0} → {1}", _maxConcurrency, concurrency));
                _maxConcurrency = concurrency;

                if (_gate != null) _retiredGates.Add(_gate);
                _gate = _disposed ? null : new SemaphoreSlim(concurrency, concurrency);
            }
        }

        public void Enqueue(DownloadTask task, DownloadOptions options)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (options == null) throw new ArgumentNullException(nameof(options));

            CancellationTokenSource cts;
            Task runner;

            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(DownloadQueue));
                if (_gate == null) throw new ObjectDisposedException(nameof(DownloadQueue));

                cts = new CancellationTokenSource();
                _tasks[task.Id] = task;
                _cancellations[task.Id] = cts;

                // 每个任务独立跑，进度通过各自的 IProgress 回调。
                // 不 await，让它们并发执行；异常在内部消化。
                runner = Task.Run(() => RunTaskAsync(task, options, cts.Token));
                _running.Add(runner);

                // 收尾时把自己从 _running 摘掉。必须在同一个锁内注册，
                // 否则极快完成的任务可能"先移除后添加"，留下一条永远清不掉的记录。
                runner.ContinueWith(
                    t =>
                    {
                        lock (_sync)
                        {
                            _running.Remove(t);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            RaiseQueueChanged();
        }

        private async Task RunTaskAsync(DownloadTask task, DownloadOptions options, CancellationToken cancellationToken)
        {
            SemaphoreSlim gate;
            lock (_sync)
            {
                gate = _gate;
            }

            if (gate == null)
            {
                // 队列已释放。
                task.State = DownloadState.Canceled;
                task.StatusText = "已取消（队列已关闭）";
                RemoveTask(task);
                NotifyTaskUpdated(task, new DownloadProgress(
                    DownloadState.Canceled, 0, null, task.StatusText));
                return;
            }

            var acquired = false;
            try
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired = true;

                // 每个任务一个独立进度通道，只回调自己的进度。
                // 这里在线程池线程上创建，Progress 会在线程池上回调，
                // 所以订阅方（MainForm）必须自己 BeginInvoke 封送到 UI 线程。
                var progress = new Progress<DownloadProgress>(p => NotifyTaskUpdated(task, p));

                await _downloadService.ExecuteAsync(task, options, progress, cancellationToken)
                    .ConfigureAwait(false);

                // 确保最终状态也被广播（有些路径可能不经过 progress）。
                NotifyTaskUpdated(task, new DownloadProgress(
                    task.State, task.BytesReceived, task.TotalBytes, task.StatusText));
            }
            catch (OperationCanceledException)
            {
                task.State = DownloadState.Canceled;
                task.StatusText = "已取消";
                NotifyTaskUpdated(task, new DownloadProgress(
                    DownloadState.Canceled, 0, null, "已取消"));
            }
            catch (ObjectDisposedException)
            {
                // 队列/服务正在关闭，按取消处理，不要记成 ERROR 吓唬用户。
                task.State = DownloadState.Canceled;
                task.StatusText = "已取消（程序正在关闭）";
                NotifyTaskUpdated(task, new DownloadProgress(
                    DownloadState.Canceled, 0, null, task.StatusText));
            }
            catch (Exception ex)
            {
                _logger.Error("队列任务异常: " + task.Song.DisplayName, ex);
                task.State = DownloadState.Failed;
                task.ErrorMessage = ex.Message;
                task.StatusText = "失败：" + ex.Message;
                NotifyTaskUpdated(task, new DownloadProgress(
                    DownloadState.Failed, task.BytesReceived, task.TotalBytes, task.StatusText));
            }
            finally
            {
                if (acquired)
                {
                    try { gate.Release(); } catch { }
                }

                RemoveTask(task);
                RaiseQueueChanged();
            }
        }

        /// <summary>任务收尾：从任务表与取消表里摘掉，并释放 CTS。</summary>
        private void RemoveTask(DownloadTask task)
        {
            lock (_sync)
            {
                _tasks.Remove(task.Id);

                CancellationTokenSource cts;
                if (_cancellations.TryGetValue(task.Id, out cts))
                {
                    _cancellations.Remove(task.Id);
                    try { cts.Dispose(); } catch { }
                }
            }
        }

        public void Cancel(Guid taskId)
        {
            CancellationTokenSource cts;
            lock (_sync)
            {
                if (!_cancellations.TryGetValue(taskId, out cts)) return;
            }

            try
            {
                cts.Cancel();
                _logger.Info("已请求取消任务 " + taskId);
            }
            catch (ObjectDisposedException)
            {
                // 任务已结束。
            }
        }

        public void CancelAll()
        {
            List<CancellationTokenSource> all;
            lock (_sync)
            {
                all = new List<CancellationTokenSource>(_cancellations.Values);
            }

            foreach (var cts in all)
            {
                try { cts.Cancel(); } catch { }
            }

            if (all.Count > 0) _logger.Info("已请求取消全部任务，共 " + all.Count + " 个");
        }

        private void NotifyTaskUpdated(DownloadTask task, DownloadProgress progress)
        {
            var handler = TaskUpdated;
            if (handler == null) return;

            try
            {
                handler(this, new DownloadTaskEventArgs(task, progress));
            }
            catch (Exception ex)
            {
                _logger.Warn("任务状态回调异常: " + ex.Message);
            }
        }

        private void RaiseQueueChanged()
        {
            var handler = QueueChanged;
            if (handler == null) return;

            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.Warn("队列变化回调异常: " + ex.Message);
            }
        }

        public void Dispose()
        {
            List<Task> running;
            List<SemaphoreSlim> gates;

            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;

                running = new List<Task>(_running);
                gates = new List<SemaphoreSlim>(_retiredGates);
                if (_gate != null) gates.Add(_gate);
                _gate = null;
                _retiredGates.Clear();
            }

            CancelAll();

            // 给在跑的任务一点收尾时间（关文件、Kill ffmpeg）。
            // 超时就不再等：UI 线程不能被 Dispose 卡住。
            if (running.Count > 0)
            {
                try
                {
                    Task.WaitAll(running.ToArray(), DisposeWaitTimeout);
                }
                catch
                {
                    // 超时或任务内部异常，都已经各自记过日志了。
                }
            }

            lock (_sync)
            {
                foreach (var cts in _cancellations.Values)
                {
                    try { cts.Dispose(); } catch { }
                }
                _cancellations.Clear();
                _tasks.Clear();
                _running.Clear();
            }

            // 仍有任务在跑时**不**释放信号量：释放会让它们的 Release/WaitAsync
            // 抛 ObjectDisposedException，被记成一堆假 ERROR。
            // SemaphoreSlim 不持有非托管资源（我们没用 AvailableWaitHandle），交给 GC 即可。
            if (running.Count == 0 || running.All(t => t.IsCompleted))
            {
                foreach (var gate in gates)
                {
                    try { gate.Dispose(); } catch { }
                }
            }
        }

        private static int Clamp(int value)
        {
            return Math.Max(1, Math.Min(8, value));
        }
    }
}
