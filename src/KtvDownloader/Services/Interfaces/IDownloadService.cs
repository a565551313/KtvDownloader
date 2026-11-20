using System;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Models;

namespace KtvDownloader.Services.Interfaces
{
    /// <summary>
    /// 下载服务。负责把**一首歌**从取址到落盘的完整流程。
    /// <para>
    /// 排队、并发限制、取消与状态广播是 <see cref="IDownloadQueue"/> 的职责。
    /// 早期这个接口上还挂着 <c>ActiveTaskCount</c> 与 <c>CancelAll()</c>，
    /// 但实现分别是"从未被赋值的属性"和"空方法体"，纯属误导，已移除。
    /// </para>
    /// </summary>
    public interface IDownloadService
    {
        /// <summary>同时进行的最大任务数（由队列写入，仅用于记录与诊断）。</summary>
        int MaxConcurrency { get; set; }

        /// <summary>
        /// 执行单个下载任务。进度通过 <paramref name="progress"/> 推送。
        /// </summary>
        Task<DownloadTask> ExecuteAsync(
            DownloadTask task,
            DownloadOptions options,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// 下载队列。负责任务排队、并发限制与状态广播。
    /// </summary>
    public interface IDownloadQueue
    {
        /// <summary>加入一个下载任务。</summary>
        void Enqueue(DownloadTask task, DownloadOptions options);

        /// <summary>取消指定任务。</summary>
        void Cancel(Guid taskId);

        /// <summary>取消全部。</summary>
        void CancelAll();

        /// <summary>
        /// 调整并发上限，立即对之后启动的任务生效
        /// （已在跑的任务不会被中断）。
        /// </summary>
        void SetMaxConcurrency(int maxConcurrency);

        /// <summary>当前排队与运行中的任务数。任务结束后立即减少。</summary>
        int ActiveTaskCount { get; }

        /// <summary>任务状态变化时触发（在 UI 线程之外，调用方需自行封送）。</summary>
        event EventHandler<DownloadTaskEventArgs> TaskUpdated;

        /// <summary>所有任务数变化时触发。</summary>
        event EventHandler QueueChanged;
    }

    /// <summary>
    /// 下载任务事件参数。
    /// </summary>
    public sealed class DownloadTaskEventArgs : EventArgs
    {
        public DownloadTaskEventArgs(DownloadTask task, DownloadProgress progress)
        {
            Task = task;
            Progress = progress;
        }

        public DownloadTask Task { get; }

        public DownloadProgress Progress { get; }
    }
}
