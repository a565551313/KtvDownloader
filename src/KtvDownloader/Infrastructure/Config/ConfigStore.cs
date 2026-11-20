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
    /// 配置读写。持久化到 <c>%AppData%\KtvDownloader\config.json</c>。
    /// <para>
    /// 为了不引入第三方 JSON 库，这里手写一个极简的 JSON 读写器（见 <see cref="MiniJson"/>），
    /// 只处理扁平结构（键值对），足够覆盖 <see cref="AppConfig"/> 的需要。
    /// </para>
    /// <para>
    /// <b>写入是原子的</b>：先写 <c>.tmp</c>，再 <c>File.Replace</c> / <c>File.Move</c> 换过去。
    /// 原来的 <c>File.WriteAllText</c> 是"先截断再写"，如果这时候断电、蓝屏或者进程被
    /// <c>Stop-Process</c> 干掉（docs §坑13 就是让人这么干的），config.json 会停在 0 字节，
    /// 下次启动 <see cref="Load"/> 解析失败 → 静默回落到默认配置 → 用户设的输出目录、
    /// 曲库路径全没了，而且界面上看不出来为什么。
    /// </para>
    /// </summary>
    public sealed class ConfigStore
    {
        private readonly string _filePath;
        private readonly ILogger _logger;

        public ConfigStore(string filePath = null, ILogger logger = null)
        {
            _filePath = filePath ?? AppConfig.ConfigFilePath;
            _logger = logger;
        }

        /// <summary>最近一次 <see cref="Load"/> / <see cref="Save"/> 失败的原因，成功时为 null。</summary>
        public string LastError { get; private set; }

        /// <summary>配置文件路径。</summary>
        public string FilePath => _filePath;

        /// <summary>
        /// 加载配置。文件不存在或解析失败时返回默认配置，不抛异常。
        /// </summary>
        public AppConfig Load()
        {
            LastError = null;

            try
            {
                if (!File.Exists(_filePath)) return AppConfig.CreateDefault();

                var json = File.ReadAllText(_filePath, Encoding.UTF8);
                var map = MiniJson.ParseObject(json);
                if (map == null)
                {
                    Fail("配置文件不是合法 JSON，已回落到默认配置: " + _filePath);
                    return AppConfig.CreateDefault();
                }

                var config = AppConfig.CreateDefault();
                config.DatabasePath = GetString(map, "DatabasePath", config.DatabasePath);
                config.OutputDirectory = GetString(map, "OutputDirectory", config.OutputDirectory);
                config.FfmpegPath = GetString(map, "FfmpegPath", config.FfmpegPath);
                config.MaxConcurrency = GetInt(map, "MaxConcurrency", config.MaxConcurrency);
                config.DefaultResolution = GetString(map, "DefaultResolution", config.DefaultResolution);
                config.DefaultVocal = GetVocal(map, "DefaultVocal", config.DefaultVocal);
                config.ConvertToMp4 = GetBool(map, "ConvertToMp4", config.ConvertToMp4);
                config.PageSize = GetInt(map, "PageSize", config.PageSize);
                config.PreviewDurationSeconds = GetInt(map, "PreviewDurationSeconds", config.PreviewDurationSeconds);
                config.LogLevel = GetString(map, "LogLevel", config.LogLevel);
                config.Normalize();
                return config;
            }
            catch (Exception ex)
            {
                Fail("读取配置失败，已回落到默认配置: " + ex.Message, ex);
                return AppConfig.CreateDefault();
            }
        }

        /// <summary>
        /// 保存配置（原子写入）。失败时返回 false 并把原因记到 <see cref="LastError"/>，不抛异常。
        /// </summary>
        public bool Save(AppConfig config)
        {
            LastError = null;

            if (config == null)
            {
                LastError = "配置对象为 null";
                return false;
            }

            var tempPath = _filePath + ".tmp";

            try
            {
                config.Normalize();

                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var builder = new StringBuilder();
                builder.AppendLine("{");
                builder.AppendLine("  \"DatabasePath\": " + MiniJson.Quote(config.DatabasePath) + ",");
                builder.AppendLine("  \"OutputDirectory\": " + MiniJson.Quote(config.OutputDirectory) + ",");
                builder.AppendLine("  \"FfmpegPath\": " + MiniJson.Quote(config.FfmpegPath) + ",");
                builder.AppendLine("  \"MaxConcurrency\": " + Num(config.MaxConcurrency) + ",");
                builder.AppendLine("  \"DefaultResolution\": " + MiniJson.Quote(config.DefaultResolution) + ",");
                builder.AppendLine("  \"DefaultVocal\": " + Num((int)config.DefaultVocal) + ",");
                builder.AppendLine("  \"ConvertToMp4\": " + (config.ConvertToMp4 ? "true" : "false") + ",");
                builder.AppendLine("  \"PageSize\": " + Num(config.PageSize) + ",");
                builder.AppendLine("  \"PreviewDurationSeconds\": " + Num(config.PreviewDurationSeconds) + ",");
                builder.AppendLine("  \"LogLevel\": " + MiniJson.Quote(config.LogLevel));
                builder.AppendLine("}");

                var json = builder.ToString();

                // 1) 先把内容完整落到临时文件（带 Flush，确保进磁盘而不是停在系统缓存里）。
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                }

                // 2) 再原子替换。File.Replace 会保留目标文件的 ACL，
                //    目标不存在时 Replace 会抛，所以要退回 Move。
                if (File.Exists(_filePath))
                {
                    File.Replace(tempPath, _filePath, null);
                }
                else
                {
                    File.Move(tempPath, _filePath);
                }

                return true;
            }
            catch (Exception ex)
            {
                Fail("保存配置失败: " + ex.Message, ex);

                // 别让失败的临时文件留在磁盘上占地方。
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return false;
            }
        }

        private void Fail(string message, Exception exception = null)
        {
            LastError = message;
            if (_logger != null) _logger.Warn(message);
            else System.Diagnostics.Debug.WriteLine(message);
        }

        private static string Num(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
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
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        private static bool GetBool(IDictionary<string, string> map, string key, bool fallback)
        {
            string value;
            if (!map.TryGetValue(key, out value) || value == null) return fallback;

            // MiniJson 存的是原始字面量，可能带引号（"true"）也可能是裸 true。
            var trimmed = value.Trim().Trim('"');
            bool parsed;
            if (bool.TryParse(trimmed, out parsed)) return parsed;
            if (trimmed == "1") return true;
            if (trimmed == "0") return false;
            return fallback;
        }

        /// <summary>
        /// 读取音轨处理选项。
        /// <para>
        /// ⚠️ 原来直接 <c>(VocalOption)GetInt(...)</c> 强转：配置文件是用户可以手改的，
        /// 写成 <c>99</c> 就会得到一个不存在的枚举值，一路传到 ffmpeg 参数拼装那里才炸，
        /// 而且报的错和真正的原因隔了三层。这里做范围校验，非法值回落默认。
        /// </para>
        /// </summary>
        private static VocalOption GetVocal(IDictionary<string, string> map, string key, VocalOption fallback)
        {
            var raw = GetInt(map, key, (int)fallback);
            return Enum.IsDefined(typeof(VocalOption), raw) ? (VocalOption)raw : fallback;
        }
    }
}
