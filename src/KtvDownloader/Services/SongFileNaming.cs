using System;
using System.Collections.Generic;
using System.IO;
using KtvDownloader.Infrastructure.Extensions;
using KtvDownloader.Models;

namespace KtvDownloader.Services
{
    /// <summary>
    /// 下载产物的文件命名规则。
    /// <para>
    /// 为什么单独一个类：写入（<c>DownloadService</c>）与反查
    /// （<c>MainForm.FindLocalFile</c>，用于"本地已有就跳过下载/直接播本地"）
    /// 必须用**同一套**规则，否则改一处忘另一处就会出现"明明下载过却找不到"。
    /// </para>
    /// <para>
    /// 规则：<c>{歌名}[-{歌手}] [{歌号}].{扩展名}</c>。
    /// 带上歌号是为了避免同名不同版本（(HD)/(Live)/Cover、不同源）互相覆盖 ——
    /// 早期版本只用「歌名-歌手」，结果后下载的会静默覆盖前一个。
    /// </para>
    /// </summary>
    public static class SongFileNaming
    {
        /// <summary>
        /// 歌名部分的最大长度。留足余量给「 [歌号]」后缀与扩展名，
        /// 避免深目录 + 长歌名撞 Windows 的 260 字符 MAX_PATH。
        /// </summary>
        private const int NameMaxLength = 80;

        /// <summary>音轨分离产物的后缀。</summary>
        public const string OriginalSuffix = "-原唱";

        /// <summary>音轨分离产物的后缀。</summary>
        public const string AccompanimentSuffix = "-伴奏";

        /// <summary>
        /// 生成不含扩展名的基础文件名。song 为空时返回空串。
        /// </summary>
        public static string BuildBaseFileName(Song song)
        {
            if (song == null) return string.Empty;

            var name = (song.Name ?? string.Empty).Trim();
            if (name.Length == 0) name = (song.NameTrim ?? string.Empty).Trim();

            var display = song.Singer.HasValue() ? name + "-" + song.Singer : name;
            var safe = display.ToSafeFileName(NameMaxLength);

            if (safe.Length == 0) safe = "song";

            var musicNo = (song.MusicNo ?? string.Empty).Trim();
            if (musicNo.Length > 0)
                safe = safe + " [" + musicNo.ToSafeFileName(32) + "]";

            // 兜底：全非法字符时至少还能用歌号命名。
            return safe.Length > 0 ? safe : (musicNo.Length > 0 ? musicNo : "song");
        }

        /// <summary>
        /// 列出这首歌在指定目录下所有可能的产物路径（含历史命名与音轨分离后缀），
        /// 按"最可能是用户想要的"顺序返回。
        /// </summary>
        public static IEnumerable<string> EnumerateCandidatePaths(string directory, Song song)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(directory) || song == null) return result;

            var bases = new List<string>();

            var current = BuildBaseFileName(song);
            if (current.Length > 0) bases.Add(current);

            // 兼容早期版本（不带歌号）已经下载好的文件。
            var legacy = BuildLegacyBaseFileName(song);
            if (legacy.Length > 0 && !bases.Contains(legacy)) bases.Add(legacy);

            foreach (var baseName in bases)
            {
                foreach (var extension in new[] { ".mp4", ".ts", ".mp3" })
                {
                    result.Add(Path.Combine(directory, baseName + extension));
                }

                // 音轨分离会产出 -原唱 / -伴奏 后缀的文件。
                foreach (var suffix in new[] { OriginalSuffix, AccompanimentSuffix })
                {
                    result.Add(Path.Combine(directory, baseName + suffix + ".mp4"));
                    result.Add(Path.Combine(directory, baseName + suffix + ".ts"));
                }
            }

            return result;
        }

        /// <summary>
        /// 在下载目录里找这首歌已下载好的文件，找不到返回 null。
        /// </summary>
        public static string FindExistingFile(string directory, Song song)
        {
            try
            {
                foreach (var candidate in EnumerateCandidatePaths(directory, song))
                {
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch
            {
                // 路径非法/无权限等，按"本地没有"处理。
            }

            return null;
        }

        /// <summary>早期命名规则：只有「歌名-歌手」，不含歌号。</summary>
        private static string BuildLegacyBaseFileName(Song song)
        {
            var name = (song.Name ?? string.Empty).Trim();
            if (name.Length == 0) return string.Empty;

            var display = song.Singer.HasValue() ? name + "-" + song.Singer : name;
            var safe = display.ToSafeFileName(100);
            return safe;
        }
    }
}
