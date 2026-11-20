using System;
using System.IO;
using System.Reflection;

namespace KtvDownloader.Infrastructure.Extensions
{
    /// <summary>
    /// 通用扩展方法。
    /// </summary>
    public static class CommonExtensions
    {
        /// <summary>
        /// 把任意字符串转换为可安全用作文件名的形式，替换 Windows 非法字符。
        /// </summary>
        public static string ToSafeFileName(this string value, int maxLength = 120)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var invalid = Path.GetInvalidFileNameChars();
            var buffer = value.ToCharArray();
            for (var i = 0; i < buffer.Length; i++)
            {
                if (Array.IndexOf(invalid, buffer[i]) >= 0) buffer[i] = '_';
            }

            var result = new string(buffer).Trim();
            if (result.Length > maxLength) result = result.Substring(0, maxLength);
            return result.TrimEnd('.', ' ');
        }

        /// <summary>判断字符串是否非空（去除首尾空白后仍有内容）。</summary>
        public static bool HasValue(this string value) => !string.IsNullOrWhiteSpace(value);

    }

    /// <summary>
    /// 程序集相关工具。
    /// </summary>
    public static class AssemblyInfoHelper
    {
        /// <summary>获取当前程序集版本号字符串。</summary>
        public static string GetVersion()
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version == null ? "1.0.0.0" : version.ToString();
        }

        /// <summary>获取当前 exe 所在目录。</summary>
        public static string BaseDirectory =>
            AppDomain.CurrentDomain.BaseDirectory;
    }
}
