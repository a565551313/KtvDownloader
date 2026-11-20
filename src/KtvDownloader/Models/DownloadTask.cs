using System;

namespace KtvDownloader.Models
{
    /// <summary>
    /// 下载任务状态。
    /// </summary>
    public enum DownloadState
    {
        /// <summary>排队等待。</summary>
        Pending = 0,

        /// <summary>正在取址。</summary>
        Resolving = 1,

        /// <summary>正在下载。</summary>
        Downloading = 2,

        /// <summary>正在解密 TS。</summary>
        Decrypting = 3,

        /// <summary>正在转码或分离音轨。</summary>
        Converting = 4,

        /// <summary>已完成。</summary>
        Completed = 5,

        /// <summary>已失败。</summary>
        Failed = 6,

        /// <summary>已取消。</summary>
        Canceled = 7
    }

    /// <summary>
    /// 单个下载任务。由 UI 创建，交给 <see cref="Services.Interfaces.IDownloadService"/> 执行。
    /// </summary>
    public sealed class DownloadTask
    {
        public DownloadTask(Song song, string outputDirectory)
        {
            Song = song ?? throw new ArgumentNullException(nameof(song));
            OutputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
            Id = Guid.NewGuid();
        }

        /// <summary>任务唯一标识。</summary>
        public Guid Id { get; }

        /// <summary>要下载的歌曲。</summary>
        public Song Song { get; }

        /// <summary>输出目录。</summary>
        public string OutputDirectory { get; }

        /// <summary>当前状态。</summary>
        public DownloadState State { get; set; }

        /// <summary>已接收字节数。</summary>
        public long BytesReceived { get; set; }

        /// <summary>总字节数，未知时为 null。</summary>
        public long? TotalBytes { get; set; }

        /// <summary>完成百分比（0-100）。</summary>
        public double Percent =>
            TotalBytes.HasValue && TotalBytes.Value > 0
                ? Math.Min(100d, BytesReceived * 100d / TotalBytes.Value)
                : 0d;

        /// <summary>状态说明文案，显示在列表中。</summary>
        public string StatusText { get; set; } = "等待中";

        /// <summary>失败原因，仅 State=Failed 时有值。</summary>
        public string ErrorMessage { get; set; }

        /// <summary>最终输出文件路径，完成后有值。</summary>
        public string OutputPath { get; set; }

        public override string ToString() => Song.DisplayName;
    }
}
