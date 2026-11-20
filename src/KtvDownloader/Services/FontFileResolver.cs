using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KtvDownloader.Infrastructure.Logging;

namespace KtvDownloader.Services
{
    /// <summary>
    /// 字体名 → 字体文件路径。
    /// <para>
    /// 为什么必须有这个东西：Windows 上常见的 ffmpeg 构建<b>不带 fontconfig</b>，
    /// <c>drawtext</c> 的 <c>font=字体名</c> 写法会失效，只认 <c>fontfile=具体路径</c>。
    /// 而用户在界面里选的是"微软雅黑"这种名字，两边对不上。
    /// </para>
    /// <para>
    /// ⚠️ <b>.ttc（TrueType Collection）要当心。</b> 一个 ttc 里装了好几个字面，
    /// libfreetype 经 drawtext 打开时取哪个字面并不稳定，实测有拿到错误粗细的情况。
    /// 所以候选顺序里 <c>.ttf</c>/<c>.otf</c> 一律优先于 <c>.ttc</c>，
    /// 最后只能用 ttc 时通过 <c>warning</c> 告诉调用方，界面上要提示用户。
    /// KTV 场景建议直接把预设钉在 <c>simhei.ttf</c>（黑体）上。
    /// </para>
    /// </summary>
    public sealed class FontFileResolver
    {
        /// <summary>
        /// 常见字体名 → 候选文件名（按优先级排，单文件字体在前）。
        /// <para>键用小写比较，中文名和英文名都收，用户填哪个都能认。</para>
        /// </summary>
        private static readonly Dictionary<string, string[]> KnownFonts =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "黑体", new[] { "simhei.ttf" } },
                { "simhei", new[] { "simhei.ttf" } },
                { "宋体", new[] { "simsun.ttc", "simfang.ttf" } },
                { "simsun", new[] { "simsun.ttc", "simfang.ttf" } },
                { "新宋体", new[] { "simsun.ttc" } },
                { "nsimsun", new[] { "simsun.ttc" } },
                { "微软雅黑", new[] { "msyh.ttf", "msyh.ttc", "msyhbd.ttf", "simhei.ttf" } },
                { "microsoft yahei", new[] { "msyh.ttf", "msyh.ttc", "msyhbd.ttf", "simhei.ttf" } },
                { "楷体", new[] { "simkai.ttf" } },
                { "kaiti", new[] { "simkai.ttf" } },
                { "仿宋", new[] { "simfang.ttf" } },
                { "fangsong", new[] { "simfang.ttf" } },
                { "隶书", new[] { "simli.ttf" } },
                { "lisu", new[] { "simli.ttf" } },
                { "幼圆", new[] { "simyou.ttf" } },
                { "youyuan", new[] { "simyou.ttf" } },
                { "等线", new[] { "Deng.ttf", "Dengb.ttf", "Dengl.ttf", "simhei.ttf" } },
                { "dengxian", new[] { "Deng.ttf", "Dengb.ttf", "Dengl.ttf", "simhei.ttf" } },
                { "arial", new[] { "arial.ttf", "arialbd.ttf", "helvetica.ttf" } },
                { "impact", new[] { "impact.ttf" } },
                { "times new roman", new[] { "times.ttf", "timesbd.ttf" } },
                { "comic sans ms", new[] { "comic.ttf", "comicbd.ttf" } }
            };

        /// <summary>解析不到时的兜底候选，按顺序取第一个存在的。</summary>
        private static readonly string[] FallbackFonts =
        {
            "simhei.ttf", "msyh.ttf", "msyh.ttc", "arial.ttf", "simsun.ttc"
        };

        private readonly string _fontsDirectory;
        private readonly ILogger _logger;
        private readonly Dictionary<string, string> _cache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new object();

        /// <param name="fontsDirectory">字体目录，默认 <c>C:\Windows\Fonts</c>（测试时可指向临时目录）。</param>
        public FontFileResolver(string fontsDirectory = null, ILogger logger = null)
        {
            _fontsDirectory = string.IsNullOrWhiteSpace(fontsDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts")
                : fontsDirectory;

            _logger = logger;
        }

        /// <summary>字体目录（诊断用）。</summary>
        public string FontsDirectory => _fontsDirectory;

        /// <summary>
        /// 解析字体名。解析不到返回 null（调用方应跳过该文本元素，而不是让整次下载失败）。
        /// </summary>
        public string Resolve(string family)
        {
            string path;
            string warning;
            return TryResolve(family, out path, out warning) ? path : null;
        }

        /// <summary>
        /// 解析字体名，并带回诊断信息。
        /// </summary>
        /// <param name="family">字体名，中英文名都可以。</param>
        /// <param name="filePath">解析结果；失败时为 null。</param>
        /// <param name="warning">非致命提示（例如"只能用 .ttc，字形可能不对"）；没有时为 null。</param>
        public bool TryResolve(string family, out string filePath, out string warning)
        {
            filePath = null;
            warning = null;

            var key = (family ?? string.Empty).Trim();

            lock (_sync)
            {
                string cached;
                if (key.Length > 0 && _cache.TryGetValue(key, out cached))
                {
                    if (cached.Length == 0) return false;   // 缓存了"解析失败"
                    filePath = cached;
                    if (IsCollection(filePath)) warning = CollectionWarning(filePath);
                    return true;
                }
            }

            if (key.Length == 0) return false;

            try
            {
                if (!Directory.Exists(_fontsDirectory))
                {
                    if (_logger != null) _logger.Warn("字体目录不存在: " + _fontsDirectory);
                    return false;
                }

                // 1) 内置映射表的候选，按顺序找第一个存在的。
                string[] candidates;
                if (KnownFonts.TryGetValue(key, out candidates))
                {
                    var hit = FirstExisting(candidates);
                    if (hit != null) return CacheAndReturn(key, hit, out filePath, out warning);
                }

                // 2) 用户可能直接填了文件名（"simhei.ttf"）或全路径。
                if (key.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                {
                    var direct = Path.Combine(_fontsDirectory, key);
                    if (File.Exists(direct)) return CacheAndReturn(key, direct, out filePath, out warning);

                    // 补个扩展名再试一次。
                    foreach (var extension in new[] { ".ttf", ".otf", ".ttc" })
                    {
                        var withExtension = Path.Combine(_fontsDirectory, key + extension);
                        if (File.Exists(withExtension))
                            return CacheAndReturn(key, withExtension, out filePath, out warning);
                    }
                }

                if (Path.IsPathRooted(key) && File.Exists(key))
                    return CacheAndReturn(key, key, out filePath, out warning);

                // 3) 扫目录，按文件名包含关系匹配。单文件字体优先于 ttc。
                var scanned = ScanForFamily(key);
                if (scanned != null) return CacheAndReturn(key, scanned, out filePath, out warning);

                // 4) 兜底：随便找一个肯定存在的常见字体，至少能出字。
                var fallback = FirstExisting(FallbackFonts);
                if (fallback != null)
                {
                    if (_logger != null)
                        _logger.Warn(string.Format("找不到字体「{0}」，改用 {1}", key, Path.GetFileName(fallback)));

                    return CacheAndReturn(key, fallback, out filePath, out warning,
                        string.Format("找不到字体「{0}」，已改用 {1}", key, Path.GetFileName(fallback)));
                }

                if (_logger != null) _logger.Warn("解析字体失败，字体目录里没有任何可用字体: " + key);
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Warn("解析字体异常: " + ex.Message);
            }

            lock (_sync)
            {
                _cache[key] = string.Empty;   // 记住失败，别每次都扫一遍目录
            }

            return false;
        }

        /// <summary>清空缓存。设置界面换了字体目录、或用户往 Fonts 里装了新字体后调。</summary>
        public void ClearCache()
        {
            lock (_sync)
            {
                _cache.Clear();
            }
        }

        /// <summary>这个路径是不是 TrueType Collection。</summary>
        public static bool IsCollection(string filePath)
        {
            return string.Equals(Path.GetExtension(filePath ?? string.Empty), ".ttc",
                StringComparison.OrdinalIgnoreCase);
        }

        private static string CollectionWarning(string filePath)
        {
            return string.Format(
                "「{0}」是 TrueType Collection（一个文件里装多个字面），ffmpeg 取到的字形可能不是你要的；建议换成黑体（simhei.ttf）",
                Path.GetFileName(filePath));
        }

        private bool CacheAndReturn(
            string key, string resolved, out string filePath, out string warning, string extraWarning = null)
        {
            lock (_sync)
            {
                _cache[key] = resolved;
            }

            filePath = resolved;
            warning = extraWarning ?? (IsCollection(resolved) ? CollectionWarning(resolved) : null);
            return true;
        }

        private string FirstExisting(string[] fileNames)
        {
            foreach (var name in fileNames)
            {
                var path = Path.Combine(_fontsDirectory, name);
                if (File.Exists(path)) return path;
            }

            return null;
        }

        /// <summary>
        /// 扫字体目录找名字里含 <paramref name="family"/> 的文件。
        /// <para>
        /// 只在前面几条路都没命中时才走这里（有缓存，一个字体名最多扫一次）。
        /// 排序规则：单文件字体（ttf/otf）优先于 ttc，同级别按文件名长度升序 ——
        /// 短的通常就是本体，长的多半是 Bold/Italic 变体。
        /// </para>
        /// </summary>
        private string ScanForFamily(string family)
        {
            try
            {
                var extensions = new[] { ".ttf", ".otf", ".ttc" };

                var matches = Directory.GetFiles(_fontsDirectory)
                    .Where(path => extensions.Contains(
                        Path.GetExtension(path).ToLowerInvariant()))
                    .Where(path => Path.GetFileNameWithoutExtension(path)
                        .IndexOf(family, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(path => IsCollection(path) ? 1 : 0)
                    .ThenBy(path => Path.GetFileName(path).Length)
                    .ToList();

                return matches.Count > 0 ? matches[0] : null;
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Warn("扫描字体目录失败: " + ex.Message);
                return null;
            }
        }
    }
}
