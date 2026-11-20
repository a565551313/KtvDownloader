using System;
using System.Threading;
using System.Threading.Tasks;

namespace KtvDownloader.Services.Interfaces
{
    /// <summary>
    /// 曲库更新进度。
    /// </summary>
    public sealed class DatabaseUpdateProgress
    {
        public DatabaseUpdateProgress(string stage, long bytesReceived = 0, long? totalBytes = null)
        {
            Stage = stage ?? string.Empty;
            BytesReceived = bytesReceived;
            TotalBytes = totalBytes;
        }

        /// <summary>当前阶段文案，如「正在登录曲库服务」「正在下载曲库」。</summary>
        public string Stage { get; }

        public long BytesReceived { get; }

        public long? TotalBytes { get; }

        /// <summary>下载进度百分比，非下载阶段为 0。</summary>
        public double Percent =>
            TotalBytes.HasValue && TotalBytes.Value > 0
                ? Math.Min(100d, BytesReceived * 100d / TotalBytes.Value)
                : 0d;

        public string ToDisplayText()
        {
            if (TotalBytes.HasValue && TotalBytes.Value > 0 && BytesReceived > 0)
            {
                return string.Format("{0} {1:F1}% ({2}/{3})",
                    Stage, Percent,
                    Models.DownloadProgress.FormatSize(BytesReceived),
                    Models.DownloadProgress.FormatSize(TotalBytes.Value));
            }
            return Stage;
        }
    }

    /// <summary>
    /// 曲库版本信息。
    /// </summary>
    public sealed class DatabaseVersionInfo
    {
        /// <summary>服务端曲库版本日期（毫秒时间戳字符串）。</summary>
        public string Version { get; set; }

        /// <summary>下载地址。</summary>
        public string Url { get; set; }

        /// <summary>压缩包的 MD5，用于校验。</summary>
        public string Md5 { get; set; }

        /// <summary>远端文件大小（字节），未知时为 null。</summary>
        public long? SizeBytes { get; set; }

        /// <summary>本地已安装的版本，未知时为 null。</summary>
        public string LocalVersion { get; set; }

        /// <summary>是否有新版本可更新。</summary>
        public bool HasUpdate
        {
            get
            {
                if (string.IsNullOrEmpty(Version)) return false;
                if (string.IsNullOrEmpty(LocalVersion)) return true;
                return !string.Equals(Version, LocalVersion, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// 曲库更新服务。复刻官方 Muse 曲库同步协议：
    /// RSA 登录 → 取文件信息 → 下载 gz → 校验 MD5 → 解压 → 替换本地曲库。
    /// </summary>
    public interface IDatabaseUpdateService
    {
        /// <summary>
        /// 查询远端曲库版本，与本地版本对比。
        /// </summary>
        Task<DatabaseVersionInfo> CheckUpdateAsync(
            string localDatabasePath,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// 下载并安装最新曲库。成功后本地文件被替换，返回新曲库路径。
        /// </summary>
        Task<string> UpdateAsync(
            string targetDatabasePath,
            IProgress<DatabaseUpdateProgress> progress,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>读取本地曲库的版本标记文件内容。</summary>
        string ReadLocalVersion(string localDatabasePath);
    }
}
