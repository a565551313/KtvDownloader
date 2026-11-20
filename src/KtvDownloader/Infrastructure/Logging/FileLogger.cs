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
    /// <para>
    /// <b>关于性能</b>：早期实现每条日志都调一次 <c>File.AppendAllText</c>，
    /// 那是"打开 → 定位 → 写 → 刷盘 → 关闭"五步，一次下载几十条日志就是几十次磁盘往返，
    /// 批量下载时能在任务管理器里看见明显的卡顿。现在改成常驻 <see cref="StreamWriter"/>，
    /// 只在下面几种时机刷盘：
    /// </para>
    /// <list type="bullet">
    ///   <item>WARN / ERROR —— 这类日志往往紧跟崩溃，必须落盘，否则正好丢掉最关键的现场；</item>
    ///   <item>缓冲区攒够 <see cref="FlushThresholdLines"/> 行；</item>
    ///   <item>调用 <see cref="Flush"/> 或 <see cref="Dispose"/>；</item>
    ///   <item>跨天需要切换文件。</item>
    /// </list>
    /// </summary>
    public sealed class FileLogger : ILogger, IDisposable
    {
        /// <summary>缓冲多少行后强制刷一次盘。</summary>
        private const int FlushThresholdLines = 64;

        private readonly object _sync = new object();
        private readonly string _directory;
        private readonly int _retainDays;

        /// <summary>当前打开的日志文件写入器（跨天时重建）。</summary>
        private StreamWriter _writer;

        /// <summary>当前 writer 对应的日期文件后缀，用于判断是否需要跨天切换。</summary>
        private string _currentDate;

        /// <summary>自上次刷盘以来攒下的行数。</summary>
        private int _pendingLines;

        /// <summary>true 表示文件写入已经彻底失败（目录不可写等），之后不再重试，免得每条日志都抛一次异常。</summary>
        private bool _disabled;

        private bool _disposed;

        public FileLogger(string directory = null, int retainDays = 14)
            : this(directory, retainDays, LogLevel.Debug)
        {
        }

        /// <param name="minimumLevel">低于该级别的日志直接丢弃，不写文件也不占缓冲区。</param>
        public FileLogger(string directory, int retainDays, LogLevel minimumLevel)
        {
            _directory = directory ?? Models.AppConfig.LogDirectory;
            _retainDays = retainDays <= 0 ? 14 : retainDays;
            MinimumLevel = minimumLevel;
            TryCreateDirectory();
        }

        /// <summary>
        /// 最低记录级别。低于它的日志会被直接丢弃。
        /// <para>
        /// 默认 <see cref="LogLevel.Debug"/>（等于全记）。排查现场问题时可以调成
        /// <see cref="LogLevel.Debug"/> 拿最详细的输出；日常使用调到
        /// <see cref="LogLevel.Info"/> 能显著减少日志体积。
        /// </para>
        /// </summary>
        public LogLevel MinimumLevel { get; set; }

        public void Debug(string message) => Write(LogLevel.Debug, message, null);

        public void Info(string message) => Write(LogLevel.Info, message, null);

        public void Warn(string message) => Write(LogLevel.Warn, message, null);

        public void Error(string message, Exception exception = null) => Write(LogLevel.Error, message, exception);

        /// <summary>把缓冲区里的日志立刻落盘。</summary>
        public void Flush()
        {
            lock (_sync)
            {
                FlushLocked();
            }
        }

        private void Write(LogLevel level, string message, Exception exception)
        {
            if (message == null && exception == null) return;
            if (level < MinimumLevel) return;

            var builder = new StringBuilder();
            builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            builder.Append(' ');
            builder.Append(Format(level));
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
                if (_disposed || _disabled) return;

                try
                {
                    EnsureWriterLocked();
                    if (_writer == null) return;

                    _writer.WriteLine(line);
                    _pendingLines++;

                    // WARN 及以上必须立刻落盘：这类日志常常就是崩溃前的最后一条。
                    if (level >= LogLevel.Warn || _pendingLines >= FlushThresholdLines)
                        FlushLocked();
                }
                catch
                {
                    // 日志失败绝不能影响主流程：丢掉缓冲区，之后不再尝试写文件。
                    _disabled = true;
                    _writer = null;
                    _pendingLines = 0;
                }
            }
        }

        private static string Format(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Info: return "INFO ";
                case LogLevel.Warn: return "WARN ";
                default: return "ERROR";
            }
        }

        /// <summary>确保 writer 指向今天的文件。调用方必须持有 <see cref="_sync"/>。</summary>
        private void EnsureWriterLocked()
        {
            var today = DateTime.Now.ToString("yyyyMMdd");
            if (_writer != null && today == _currentDate) return;

            // 跨天：先把昨天的刷干净再关掉。
            if (_writer != null)
            {
                try { FlushLocked(); } catch { }
                try { _writer.Dispose(); } catch { }
                _writer = null;
            }

            TryCreateDirectory();

            var file = Path.Combine(_directory, today + ".log");
            var stream = new FileStream(file, FileMode.Append, FileAccess.Write,
                // 允许多个实例（以及用户用记事本打开）同时读写，否则第二个 FileLogger 直接抛 IOException。
                FileShare.ReadWrite, 4096);

            _writer = new StreamWriter(stream, new UTF8Encoding(false));
            _writer.AutoFlush = false;
            _currentDate = today;
            _pendingLines = 0;
        }

        private void FlushLocked()
        {
            if (_writer == null) return;
            try
            {
                _writer.Flush();
                _pendingLines = 0;
            }
            catch
            {
                _disabled = true;
                try { _writer.Dispose(); } catch { }
                _writer = null;
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
                    try
                    {
                        // 今天正在写的这个文件不要动。
                        if (string.Equals(Path.GetFileName(file), _currentDate + ".log",
                            StringComparison.OrdinalIgnoreCase)) continue;

                        if (File.GetLastWriteTime(file) < threshold) File.Delete(file);
                    }
                    catch
                    {
                        // 单个文件删不掉（被占用等）不影响其它文件。
                    }
                }
            }
            catch
            {
                // 清理失败不影响运行。
            }
        }

        /// <summary>刷盘并关闭文件句柄。程序退出时必须调用，否则丢掉最后几十行日志。</summary>
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;

                try
                {
                    if (_writer != null)
                    {
                        _writer.Flush();
                        _writer.Dispose();
                    }
                }
                catch
                {
                    // 退出阶段的日志失败没有意义，忽略。
                }
                finally
                {
                    _writer = null;
                    _pendingLines = 0;
                }
            }
        }
    }
}
