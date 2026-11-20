using System;
using System.IO;
using System.Text;

namespace KtvDownloader.Infrastructure.Logging
{
    /// <summary>
    /// 日志记录器抽象。便于测试替换，也便于将来换成远程日志。
    /// </summary>
    public interface ILogger
    {
        void Debug(string message);
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception exception = null);
    }

    /// <summary>
    /// 写文件的日志记录器。线程安全，按天切分文件，自动清理过期日志。
    /// </summary>
    public sealed class FileLogger : ILogger
    {
        private readonly object _sync = new object();
        private readonly string _directory;
        private readonly int _retainDays;

        public FileLogger(string directory = null, int retainDays = 14)
        {
            _directory = directory ?? Models.AppConfig.LogDirectory;
            _retainDays = retainDays <= 0 ? 14 : retainDays;
            TryCreateDirectory();
        }

        public void Debug(string message) => Write("DEBUG", message, null);

        public void Info(string message) => Write("INFO ", message, null);

        public void Warn(string message) => Write("WARN ", message, null);

        public void Error(string message, Exception exception = null) => Write("ERROR", message, exception);

        private void Write(string level, string message, Exception exception)
        {
            if (message == null && exception == null) return;

            var builder = new StringBuilder();
            builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            builder.Append(' ');
            builder.Append(level);
            builder.Append(' ');
            builder.Append(message ?? string.Empty);

            if (exception != null)
            {
                builder.AppendLine();
                builder.Append(exception);
            }

            var line = builder.ToString();

            // 控制台输出便于命令行调试；GUI 运行时用户看不到，但无害。
            System.Diagnostics.Debug.WriteLine(line);

            lock (_sync)
            {
                try
                {
                    TryCreateDirectory();
                    var file = Path.Combine(_directory, DateTime.Now.ToString("yyyyMMdd") + ".log");
                    File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // 日志失败绝不能影响主流程，静默忽略。
                }
            }
        }

        private void TryCreateDirectory()
        {
            try
            {
                if (!Directory.Exists(_directory)) Directory.CreateDirectory(_directory);
            }
            catch
            {
                // 目录不可创建时退化为只输出到 Debug。
            }
        }

        /// <summary>删除超过保留天数的日志文件。</summary>
        public void CleanupOldLogs()
        {
            try
            {
                if (!Directory.Exists(_directory)) return;
                var threshold = DateTime.Now.AddDays(-_retainDays);
                foreach (var file in Directory.GetFiles(_directory, "*.log"))
                {
                    if (File.GetLastWriteTime(file) < threshold) File.Delete(file);
                }
            }
            catch
            {
                // 清理失败不影响运行。
            }
        }
    }
}
