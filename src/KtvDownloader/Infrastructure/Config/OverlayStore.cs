using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Models;

namespace KtvDownloader.Infrastructure.Config
{
    /// <summary>
    /// 叠加配置的读写。文件：<c>%AppData%\KtvDownloader\overlay.jsonl</c>。
    /// <para>
    /// <b>为什么是 JSONL（一行一个元素）而不是一个嵌套 JSON：</b>
    /// <see cref="MiniJson"/> 只解析扁平键值对（<c>ParseObject</c> 返回
    /// <c>Dictionary&lt;string,string&gt;</c>），要存数组套对象就得给它加递归解析 ——
    /// 而它在 <see cref="ConfigStore"/>、曲库更新、取址协议里到处在用，改坏了影响面太大。
    /// 一行一个元素，每行都是扁平对象，正好能直接用现成的、已经测过的解析器。
    /// </para>
    /// <para>
    /// 附带好处：这个文件用户能拿记事本直接看、直接改（和 <c>favorites.txt</c> 一个思路）。
    /// </para>
    /// <para>
    /// 全局开关和编码参数存在第一行的 <c>"Kind":"Meta"</c> 里，不单独开文件。
    /// 写入方式和 <see cref="ConfigStore"/> 一致：先 <c>.tmp</c> 再原子替换 ——
    /// 直接截断写的话，断电或被 <c>Stop-Process</c> 干掉（docs 坑13 就是让人这么干的）
    /// 会留下一个 0 字节文件，用户整套水印配置就没了。
    /// </para>
    /// </summary>
    public sealed class OverlayStore
    {
        /// <summary>Meta 行的 Kind 值。它不是一个真的元素，只是搭个便车存全局设置。</summary>
        private const string MetaKind = "Meta";

        private readonly string _filePath;
        private readonly ILogger _logger;
        private readonly object _sync = new object();

        public OverlayStore(string filePath = null, ILogger logger = null)
        {
            _filePath = filePath ?? Path.Combine(AppConfig.AppDataDirectory, "overlay.jsonl");
            _logger = logger;
        }

        /// <summary>配置文件路径。</summary>
        public string FilePath => _filePath;

        /// <summary>最近一次读写失败的原因，成功时为 null。</summary>
        public string LastError { get; private set; }

        /// <summary>
        /// 加载配置。文件不存在返回一个<b>未启用</b>的空配置，解析失败返回默认配置，都不抛异常。
        /// </summary>
        public OverlayComposition Load()
        {
            LastError = null;

            lock (_sync)
            {
                var composition = new OverlayComposition();

                try
                {
                    if (!File.Exists(_filePath)) return composition;

                    var lineNumber = 0;
                    foreach (var raw in File.ReadAllLines(_filePath, Encoding.UTF8))
                    {
                        lineNumber++;

                        var line = (raw ?? string.Empty).Trim();
                        if (line.Length == 0 || line[0] == '#') continue;   // 空行和 # 注释都放过

                        var map = MiniJson.ParseObject(line);
                        if (map == null)
                        {
                            Warn(string.Format("overlay.jsonl 第 {0} 行不是合法 JSON，已跳过", lineNumber));
                            continue;
                        }

                        var kind = GetString(map, "Kind", string.Empty);

                        if (string.Equals(kind, MetaKind, StringComparison.OrdinalIgnoreCase))
                        {
                            ApplyMeta(composition, map);
                            continue;
                        }

                        var element = ParseElement(map);
                        if (element != null) composition.Elements.Add(element);
                    }
                }
                catch (Exception ex)
                {
                    Warn("读取叠加配置失败，已回落到空配置: " + ex.Message);
                    return new OverlayComposition();
                }

                composition.Normalize();
                return composition;
            }
        }

        /// <summary>保存（原子写入）。失败时返回 false 并记 <see cref="LastError"/>，不抛异常。</summary>
        public bool Save(OverlayComposition composition)
        {
            if (composition == null)
            {
                LastError = "叠加配置对象为 null";
                return false;
            }

            LastError = null;

            lock (_sync)
            {
                var tempPath = _filePath + ".tmp";

                try
                {
                    composition.Normalize();

                    var directory = Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    var builder = new StringBuilder();
                    builder.AppendLine("# KtvDownloader 叠加配置：一行一个元素，可以用记事本改。");
                    builder.AppendLine("# 尺寸（边距/字号/描边）都以 720p 为基准，渲染时按实际分辨率等比缩放。");
                    builder.AppendLine(BuildMetaLine(composition));

                    foreach (var element in composition.Elements)
                    {
                        if (element == null) continue;
                        builder.AppendLine(BuildElementLine(element));
                    }

                    using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                        FileShare.None, 4096, FileOptions.WriteThrough))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.Write(builder.ToString());
                        writer.Flush();
                    }

                    if (File.Exists(_filePath)) File.Replace(tempPath, _filePath, null);
                    else File.Move(tempPath, _filePath);

                    return true;
                }
                catch (Exception ex)
                {
                    LastError = "保存叠加配置失败: " + ex.Message;
                    Warn(LastError);

                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    return false;
                }
            }
        }

        // ── Meta 行 ─────────────────────────────────────────────────

        private static string BuildMetaLine(OverlayComposition composition)
        {
            var fields = new List<string>
            {
                Field("Kind", MetaKind),
                Field("Enabled", composition.Enabled),
                Field("ShowInPreview", composition.ShowInPreview),
                Field("BurnOnDownload", composition.BurnOnDownload),
                Field("VideoEncoder", composition.VideoEncoder),
                Field("VideoPreset", composition.VideoPreset),
                Field("VideoCrf", composition.VideoCrf)
            };

            return "{" + string.Join(",", fields.ToArray()) + "}";
        }

        private static void ApplyMeta(OverlayComposition composition, IDictionary<string, string> map)
        {
            composition.Enabled = GetBool(map, "Enabled", composition.Enabled);
            composition.ShowInPreview = GetBool(map, "ShowInPreview", composition.ShowInPreview);
            composition.BurnOnDownload = GetBool(map, "BurnOnDownload", composition.BurnOnDownload);
            composition.VideoEncoder = GetString(map, "VideoEncoder", composition.VideoEncoder);
            composition.VideoPreset = GetString(map, "VideoPreset", composition.VideoPreset);
            composition.VideoCrf = GetInt(map, "VideoCrf", composition.VideoCrf);
        }

        // ── 元素行 ──────────────────────────────────────────────────

        private static string BuildElementLine(OverlayElement element)
        {
            var fields = new List<string>
            {
                Field("Kind", element.Kind == OverlayElementKind.Text ? "Text" : "Image"),
                Field("Name", element.Name),
                Field("Visible", element.Visible),
                Field("Anchor", (int)element.Anchor),
                Field("MarginX", element.MarginX),
                Field("MarginY", element.MarginY),
                Field("HasTimeWindow", element.HasTimeWindow),
                Field("StartSeconds", element.StartSeconds),
                Field("EndSeconds", element.EndSeconds)
            };

            if (element.Kind == OverlayElementKind.Image)
            {
                fields.Add(Field("ImagePath", element.ImagePath));
                fields.Add(Field("RelativeWidth", element.RelativeWidth));
                fields.Add(Field("Opacity", element.Opacity));
            }
            else
            {
                fields.Add(Field("Text", element.Text));
                fields.Add(Field("FontFamily", element.FontFamily));
                fields.Add(Field("FontSizePx", element.FontSizePx));
                fields.Add(Field("FillColor", element.FillColor));
                fields.Add(Field("BorderColor", element.BorderColor));
                fields.Add(Field("BorderWidthPx", element.BorderWidthPx));
                fields.Add(Field("Shadow", element.Shadow));
            }

            return "{" + string.Join(",", fields.ToArray()) + "}";
        }

        private static OverlayElement ParseElement(IDictionary<string, string> map)
        {
            var kindText = GetString(map, "Kind", "Image");
            var isText = string.Equals(kindText, "Text", StringComparison.OrdinalIgnoreCase);

            var element = new OverlayElement
            {
                Kind = isText ? OverlayElementKind.Text : OverlayElementKind.Image,
                Name = GetString(map, "Name", string.Empty),
                Visible = GetBool(map, "Visible", true),
                Anchor = GetAnchor(map, "Anchor", OverlayAnchor.TopRight),
                MarginX = GetInt(map, "MarginX", 24),
                MarginY = GetInt(map, "MarginY", 24),
                HasTimeWindow = GetBool(map, "HasTimeWindow", false),
                StartSeconds = GetDouble(map, "StartSeconds", 0),
                EndSeconds = GetDouble(map, "EndSeconds", 5)
            };

            if (isText)
            {
                element.Text = GetString(map, "Text", string.Empty);
                element.FontFamily = GetString(map, "FontFamily", "黑体");
                element.FontSizePx = GetInt(map, "FontSizePx", 48);
                element.FillColor = GetString(map, "FillColor", "#1E90FF");
                element.BorderColor = GetString(map, "BorderColor", "#FFFFFF");
                element.BorderWidthPx = GetInt(map, "BorderWidthPx", 3);
                element.Shadow = GetBool(map, "Shadow", true);
            }
            else
            {
                element.ImagePath = GetString(map, "ImagePath", string.Empty);
                element.RelativeWidth = GetDouble(map, "RelativeWidth", 0.15);
                element.Opacity = GetDouble(map, "Opacity", 1.0);
            }

            element.Normalize();
            return element;
        }

        /// <summary>
        /// 读锚点。
        /// <para>
        /// ⚠️ 不要直接 <c>(OverlayAnchor)GetInt(...)</c> 强转：这文件用户可以手改，
        /// 写成 <c>99</c> 就会得到一个不存在的枚举值，一路传到几何计算那里才出问题，
        /// 而现象只是"水印位置不对"，根本猜不到是配置写错了。
        /// </para>
        /// </summary>
        private static OverlayAnchor GetAnchor(IDictionary<string, string> map, string key, OverlayAnchor fallback)
        {
            var raw = GetInt(map, key, (int)fallback);
            return Enum.IsDefined(typeof(OverlayAnchor), raw) ? (OverlayAnchor)raw : fallback;
        }

        // ── 取值助手（与 ConfigStore 同套路：MiniJson 存的是字面量）──────

        private static string Field(string key, string value)
        {
            return MiniJson.Quote(key) + ":" + MiniJson.Quote(value ?? string.Empty);
        }

        private static string Field(string key, bool value)
        {
            return MiniJson.Quote(key) + ":" + (value ? "true" : "false");
        }

        private static string Field(string key, int value)
        {
            return MiniJson.Quote(key) + ":" + value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Field(string key, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;

            return MiniJson.Quote(key) + ":" + value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string GetString(IDictionary<string, string> map, string key, string fallback)
        {
            string value;
            return map.TryGetValue(key, out value) && value != null ? value : fallback;
        }

        private static int GetInt(IDictionary<string, string> map, string key, int fallback)
        {
            string value;
            if (!map.TryGetValue(key, out value) || value == null) return fallback;

            int parsed;
            return int.TryParse(value.Trim().Trim('"'), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static double GetDouble(IDictionary<string, string> map, string key, double fallback)
        {
            string value;
            if (!map.TryGetValue(key, out value) || value == null) return fallback;

            double parsed;
            return double.TryParse(value.Trim().Trim('"'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static bool GetBool(IDictionary<string, string> map, string key, bool fallback)
        {
            string value;
            if (!map.TryGetValue(key, out value) || value == null) return fallback;

            var trimmed = value.Trim().Trim('"');

            bool parsed;
            if (bool.TryParse(trimmed, out parsed)) return parsed;
            if (trimmed == "1") return true;
            if (trimmed == "0") return false;
            return fallback;
        }

        private void Warn(string message)
        {
            LastError = message;
            if (_logger != null) _logger.Warn(message);
            else System.Diagnostics.Debug.WriteLine(message);
        }
    }
}
