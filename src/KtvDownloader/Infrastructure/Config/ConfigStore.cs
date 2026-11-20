using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KtvDownloader.Models;

namespace KtvDownloader.Infrastructure.Config
{
    /// <summary>
    /// 配置读写。
    /// <para>
    /// 为了不引入第三方 JSON 库，这里手写一个极简的 JSON 读写器，
    /// 只处理扁平结构（键值对），足够覆盖 <see cref="AppConfig"/> 的需要。
    /// </para>
    /// </summary>
    public sealed class ConfigStore
    {
        private readonly string _filePath;

        public ConfigStore(string filePath = null)
        {
            _filePath = filePath ?? AppConfig.ConfigFilePath;
        }

        /// <summary>
        /// 加载配置。文件不存在或解析失败时返回默认配置，不抛异常。
        /// </summary>
        public AppConfig Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return AppConfig.CreateDefault();

                var json = File.ReadAllText(_filePath, Encoding.UTF8);
                var map = MiniJson.ParseObject(json);
                if (map == null) return AppConfig.CreateDefault();

                var config = AppConfig.CreateDefault();
                config.DatabasePath = GetString(map, "DatabasePath", config.DatabasePath);
                config.OutputDirectory = GetString(map, "OutputDirectory", config.OutputDirectory);
                config.FfmpegPath = GetString(map, "FfmpegPath", config.FfmpegPath);
                config.MaxConcurrency = GetInt(map, "MaxConcurrency", config.MaxConcurrency);
                config.DefaultResolution = GetString(map, "DefaultResolution", config.DefaultResolution);
                config.DefaultVocal = (VocalOption)GetInt(map, "DefaultVocal", (int)config.DefaultVocal);
                config.ConvertToMp4 = GetBool(map, "ConvertToMp4", config.ConvertToMp4);
                config.PageSize = GetInt(map, "PageSize", config.PageSize);
                config.PreviewDurationSeconds = GetInt(map, "PreviewDurationSeconds", config.PreviewDurationSeconds);
                config.Normalize();
                return config;
            }
            catch
            {
                return AppConfig.CreateDefault();
            }
        }

        /// <summary>
        /// 保存配置。失败时返回 false，不抛异常。
        /// </summary>
        public bool Save(AppConfig config)
        {
            if (config == null) return false;

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
                builder.AppendLine("  \"MaxConcurrency\": " + config.MaxConcurrency.ToString(CultureInfo.InvariantCulture) + ",");
                builder.AppendLine("  \"DefaultResolution\": " + MiniJson.Quote(config.DefaultResolution) + ",");
                builder.AppendLine("  \"DefaultVocal\": " + ((int)config.DefaultVocal).ToString(CultureInfo.InvariantCulture) + ",");
                builder.AppendLine("  \"ConvertToMp4\": " + (config.ConvertToMp4 ? "true" : "false") + ",");
                builder.AppendLine("  \"PageSize\": " + config.PageSize.ToString(CultureInfo.InvariantCulture) + ",");
                builder.AppendLine("  \"PreviewDurationSeconds\": " + config.PreviewDurationSeconds.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("}");

                File.WriteAllText(_filePath, builder.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch
            {
                return false;
            }
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
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
