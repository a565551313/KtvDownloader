using System;
using System.Collections.Generic;
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
    /// </summary>
    public sealed class DownloadQueue : IDownloadQueue, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IDownloadService _downloadService;
        private readonly SemaphoreSlim _semaphore;
        private readonly object _sync = new object();

        private readonly Dictionary<Guid, CancellationTokenSource> _cancellations =
            new Dictionary<Guid, CancellationTokenSource>();

        private readonly Dictionary<Guid, DownloadTask> _tasks = new Dictionary<Guid, DownloadTask>();
        private bool _disposed;

        public DownloadQueue(ILogger logger, IDownloadService downloadService, int maxConcurrency)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _downloadService = downloadService ?? throw new ArgumentNullException(nameof(downloadService));

            var concurrency = Math.Max(1, Math.Min(8, maxConcurrency));
            _downloadService.MaxConcurrency = concurrency;
            _semaphore = new SemaphoreSlim(concurrency, concurrency);
        }

        public event EventHandler<DownloadTaskEventArgs> TaskUpdated;

        public event EventHandler QueueChanged;

        /// <summary>当前排队与运行中的任务数。</summary>
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

        /// <summary>调整并发数。只影响后续启动的任务。</summary>
        public void SetMaxConcurrency(int value)
        {
            // SemaphoreSlim 不能在运行中改变上限，这里记录到服务层，
            // 实际并发由 _semaphore 的初始值决定；如需动态调整应重建队列。
            _downloadService.MaxConcurrency = Math.Max(1, Math.Min(8, value));
        }

        public void Enqueue(DownloadTask task, DownloadOptions options)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (_disposed) throw new ObjectDisposedException(nameof(DownloadQueue));

            var cts = new CancellationTokenSource();

            lock (_sync)
            {
                _tasks[task.Id] = task;
                _cancellations[task.Id] = cts;
            }

            RaiseQueueChanged();

            // 每个任务独立跑，进度通过各自的 IProgress 回调。
            // 不 await，让它们并发执行；异常在内部消化。
            Task.Run(() => RunTaskAsync(task, options, cts.Token));
        }

        private async Task RunTaskAsync(DownloadTask task, DownloadOptions options, CancellationToken cancellationToken)
        {
            var acquired = false;
            try
            {
                await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired = true;

                // 每个任务一个独立进度通道，只回调自己的进度。
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
                    try { _semaphore.Release(); } catch { }
                }

                lock (_sync)
                {
                    CancellationTokenSource cts;
                    if (_cancellations.TryGetValue(task.Id, out cts))
                    {
                        _cancellations.Remove(task.Id);
                        try { cts.Dispose(); } catch { }
                    }
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
            _logger.Info("已请求取消全部任务，共 " + all.Count + " 个");
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
            if (_disposed) return;
            _disposed = true;

            CancelAll();

            lock (_sync)
            {
                foreach (var cts in _cancellations.Values)
                {
                    try { cts.Dispose(); } catch { }
                }
                _cancellations.Clear();
                _tasks.Clear();
            }

            _semaphore.Dispose();
        }
    }
}
