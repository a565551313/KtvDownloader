using System;

namespace KtvDownloader.Infrastructure.Logging
{
    /// <summary>日志级别，数值越大越严重。</summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3
    }

    /// <summary>
    /// 把配置里的字符串安全地解析成 <see cref="LogLevel"/>。
    /// <para>
    /// 认不出的值一律返回 <paramref name="fallback"/>，不抛异常 ——
    /// 配置文件是用户可以手改的，改错了不能让整个程序起不来。
    /// </para>
    /// </summary>
    public static class LogLevelParser
    {
        public static LogLevel Parse(string value, LogLevel fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;

            var trimmed = value.Trim();

            if (string.Equals(trimmed, "DEBUG", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "TRACE", StringComparison.OrdinalIgnoreCase))
                return LogLevel.Debug;

            if (string.Equals(trimmed, "INFO", StringComparison.OrdinalIgnoreCase))
                return LogLevel.Info;

            if (string.Equals(trimmed, "WARN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "WARNING", StringComparison.OrdinalIgnoreCase))
                return LogLevel.Warn;

            if (string.Equals(trimmed, "ERROR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "ERR", StringComparison.OrdinalIgnoreCase))
                return LogLevel.Error;

            return fallback;
        }

        /// <summary>反向转换，用于写回配置文件。</summary>
        public static string ToString(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug: return "Debug";
                case LogLevel.Info: return "Info";
                case LogLevel.Warn: return "Warn";
                case LogLevel.Error: return "Error";
                default: return "Info";
            }
        }
    }
}
