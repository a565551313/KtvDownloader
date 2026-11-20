using System;
using System.IO;

namespace KtvDownloader.Models
{
    /// <summary>
    /// 应用配置。持久化到 %AppData%\KtvDownloader\config.json。
    /// </summary>
    public sealed class AppConfig
    {
        /// <summary>曲库 SQLite 文件路径（muse.db）。</summary>
        public string DatabasePath { get; set; } = string.Empty;

        /// <summary>下载输出目录。</summary>
        public string OutputDirectory { get; set; } = string.Empty;

        /// <summary>ffmpeg.exe 路径。留空时自动在 tools 目录与 PATH 中查找。</summary>
        public string FfmpegPath { get; set; } = string.Empty;

        /// <summary>同时下载的任务数（1-8）。</summary>
        public int MaxConcurrency { get; set; } = 2;

        /// <summary>默认取址分辨率。</summary>
        public string DefaultResolution { get; set; } = "720";

        /// <summary>默认音轨处理方式。</summary>
        public VocalOption DefaultVocal { get; set; } = VocalOption.KeepBoth;

        /// <summary>默认是否转 MP4。</summary>
        public bool ConvertToMp4 { get; set; } = true;

        /// <summary>搜索结果每页条数。</summary>
        public int PageSize { get; set; } = 50;

        /// <summary>
        /// 预览时长（秒）。0 表示完整预览（下载整首再播）。
        /// 默认 60 秒：兼顾速度与"能听出是什么歌"。
        /// </summary>
        public int PreviewDurationSeconds { get; set; } = 60;

        /// <summary>
        /// 最低日志级别：<c>Debug</c> / <c>Info</c> / <c>Warn</c> / <c>Error</c>。
        /// <para>
        /// 默认 <c>Debug</c>（全记，和以前行为一致）。日常使用改成 <c>Warn</c>
        /// 可以把日志体积压掉七成以上 —— 现在 <see cref="Infrastructure.Logging.FileLogger"/>
        /// 是缓冲写，但行数少了刷盘次数也跟着少。
        /// </para>
        /// </summary>
        public string LogLevel { get; set; } = "Debug";

        /// <summary>应用数据目录，固定为 %AppData%\KtvDownloader。</summary>
        public static string AppDataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KtvDownloader");

        /// <summary>配置文件名。</summary>
        public static string ConfigFilePath => Path.Combine(AppDataDirectory, "config.json");

        /// <summary>日志目录。</summary>
        public static string LogDirectory => Path.Combine(AppDataDirectory, "logs");

        /// <summary>创建带默认路径的配置。</summary>
        public static AppConfig CreateDefault()
        {
            return new AppConfig
            {
                DatabasePath = string.Empty,
                OutputDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "KtvDownloader"),
                FfmpegPath = string.Empty,
                MaxConcurrency = 2
            };
        }

        /// <summary>把配置修正到合法范围，避免手工编辑出的非法值导致运行异常。</summary>
        public void Normalize()
        {
            if (MaxConcurrency < 1) MaxConcurrency = 1;
            if (MaxConcurrency > 8) MaxConcurrency = 8;
            if (PageSize < 10) PageSize = 10;
            if (PageSize > 500) PageSize = 500;

            if (string.IsNullOrWhiteSpace(OutputDirectory))
                OutputDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "KtvDownloader");

            if (string.IsNullOrWhiteSpace(DefaultResolution))
                DefaultResolution = "720";

            // 预览时长：0 = 完整下载，否则必须在 10-600 秒之间。
            // ⚠️ 原来的写法只挡了 <0 和 >600，1~9 这种值能直接穿过去 ——
            // 而 <10 秒的预览切片经常连一个完整的 TS 关键帧都凑不齐，VLC 会黑屏或报错。
            if (PreviewDurationSeconds < 0) PreviewDurationSeconds = 60;
            if (PreviewDurationSeconds > 600) PreviewDurationSeconds = 600;
            if (PreviewDurationSeconds > 0 && PreviewDurationSeconds < 10) PreviewDurationSeconds = 10;

            if (string.IsNullOrWhiteSpace(LogLevel)) LogLevel = "Debug";
        }
    }
}
