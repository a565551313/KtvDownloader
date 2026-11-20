using System;
using System.Collections.Generic;
using System.IO;

namespace KtvDownloader.Infrastructure.Config
{
    /// <summary>
    /// 曲库文件路径探测。
    /// <para>
    /// 用户通常已经把 muse.db 放在某个位置（比如项目根目录），
    /// 不应该逼他每次手动配置。这里按优先级依次探测：
    /// 1. 配置文件中已保存的路径
    /// 2. %AppData%\KtvDownloader\muse.db（标准位置，曲库更新也写这里）
    /// 3. exe 所在目录及向上若干层目录中的 muse.db（覆盖开发时 bin/Debug/net48 的情况）
    /// 4. 程序当前工作目录下的 muse.db
    /// </para>
    /// </summary>
    public static class DatabasePathResolver
    {
        /// <summary>曲库默认文件名。</summary>
        public const string DefaultFileName = "muse.db";

        /// <summary>向上探测的最大层数。</summary>
        private const int MaxParentDepth = 8;

        /// <summary>
        /// 按优先级探测曲库路径。找到返回绝对路径，否则返回 null。
        /// </summary>
        /// <param name="preferredPath">配置中已保存的路径，可为空。</param>
        public static string Resolve(string preferredPath)
        {
            // 1. 配置里已保存且有效
            if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
                return Path.GetFullPath(preferredPath);

            foreach (var candidate in EnumerateCandidates())
            {
                if (File.Exists(candidate))
                {
                    try
                    {
                        return Path.GetFullPath(candidate);
                    }
                    catch
                    {
                        // 路径非法时继续找下一个。
                    }
                }
            }

            return null;
        }

        /// <summary>列出所有候选路径，用于诊断与提示。</summary>
        public static IEnumerable<string> EnumerateCandidates()
        {
            // 2. 标准数据目录
            yield return Path.Combine(Models.AppConfig.AppDataDirectory, DefaultFileName);

            // 3. 从 exe 目录逐级向上
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDirectory))
            {
                DirectoryInfo directory = null;
                try
                {
                    directory = new DirectoryInfo(baseDirectory);
                }
                catch
                {
                    // 忽略，后面还有别的候选。
                }

                for (var depth = 0; depth < MaxParentDepth && directory != null; depth++)
                {
                    yield return Path.Combine(directory.FullName, DefaultFileName);

                    // 同时看一层常见的 data / tools 子目录
                    yield return Path.Combine(directory.FullName, "data", DefaultFileName);

                    try
                    {
                        directory = directory.Parent;
                    }
                    catch
                    {
                        directory = null;
                    }
                }
            }

            // 4. 当前工作目录
            string workingDirectory = null;
            try
            {
                workingDirectory = Directory.GetCurrentDirectory();
            }
            catch
            {
                // 忽略。
            }

            if (!string.IsNullOrEmpty(workingDirectory))
                yield return Path.Combine(workingDirectory, DefaultFileName);
        }

        /// <summary>
        /// 曲库更新时应该写入的位置：优先沿用当前已生效的曲库路径，
        /// 否则用标准数据目录。
        /// </summary>
        public static string ResolveUpdateTarget(string currentPath)
        {
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                try
                {
                    var full = Path.GetFullPath(currentPath);
                    var directory = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) return full;
                }
                catch
                {
                    // 落到默认位置。
                }
            }

            return Path.Combine(Models.AppConfig.AppDataDirectory, DefaultFileName);
        }
    }
}
