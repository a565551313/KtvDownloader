using System;

namespace KtvDownloader.Models
{
    /// <summary>
    /// 下载进度快照。通过 <see cref="IProgress{T}"/> 从后台线程推送到 UI 线程。
    /// </summary>
    public sealed class DownloadProgress
    {
        public DownloadProgress(DownloadState state, long bytesReceived, long? totalBytes, string statusText)
        {
            State = state;
            BytesReceived = bytesReceived;
            TotalBytes = totalBytes;
            StatusText = statusText ?? string.Empty;
            Timestamp = DateTime.Now;
        }

        public DownloadState State { get; }

        public long BytesReceived { get; }

        public long? TotalBytes { get; }

        /// <summary>完成百分比（0-100）。</summary>
        public double Percent =>
            TotalBytes.HasValue && TotalBytes.Value > 0
                ? Math.Min(100d, BytesReceived * 100d / TotalBytes.Value)
                : 0d;

        /// <summary>面向用户的阶段文案，例如「下载中 45%」。</summary>
        public string StatusText { get; }

        public DateTime Timestamp { get; }

        public string ToDisplayText()
        {
            if (State == DownloadState.Downloading && TotalBytes.HasValue && TotalBytes.Value > 0)
            {
                return string.Format("下载中 {0:F0}% ({1}/{2})",
                    Percent, FormatSize(BytesReceived), FormatSize(TotalBytes.Value));
            }
            return StatusText;
        }

        /// <summary>把字节数格式化为易读文本。</summary>
        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024d).ToString("F1") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024d / 1024d).ToString("F1") + " MB";
            return (bytes / 1024d / 1024d / 1024d).ToString("F2") + " GB";
        }
    }
}
