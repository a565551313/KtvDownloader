namespace KtvDownloader.Models
{
    /// <summary>
    /// 下载产物的音轨处理方式。
    /// </summary>
    public enum VocalOption
    {
        /// <summary>保留原始双音轨（原唱+伴奏都在，播放器可切换）。</summary>
        KeepBoth = 0,

        /// <summary>只保留第一条音轨（原唱）。</summary>
        OriginalOnly = 1,

        /// <summary>只保留第二条音轨（伴奏）。</summary>
        AccompanimentOnly = 2,

        /// <summary>同时产出原唱版和伴奏版两个文件。</summary>
        SplitBoth = 3
    }

    /// <summary>
    /// 单个任务的下载选项。
    /// </summary>
    public sealed class DownloadOptions
    {
        /// <summary>取址分辨率：480 / 720 / 1080。源站多数只有 480p，传更高也返回 480p。</summary>
        public string Resolution { get; set; } = "720";

        /// <summary>是否请求 H.265 版本。</summary>
        public bool PreferH265 { get; set; }

        /// <summary>音轨处理方式。</summary>
        public VocalOption Vocal { get; set; } = VocalOption.KeepBoth;

        /// <summary>是否转码为 MP4。为 false 时保留原始 .ts 文件。</summary>
        public bool ConvertToMp4 { get; set; } = true;

        /// <summary>MP4 音频码率（kbps）。会把 mp2 重编码为 AAC，避免播放器静音。</summary>
        public int AudioBitrateKbps { get; set; } = 192;

        /// <summary>是否在完成后删除中间 ts 文件。</summary>
        public bool DeleteIntermediateFiles { get; set; } = true;

        /// <summary>创建一份默认选项的副本。</summary>
        public DownloadOptions Clone()
        {
            return new DownloadOptions
            {
                Resolution = Resolution,
                PreferH265 = PreferH265,
                Vocal = Vocal,
                ConvertToMp4 = ConvertToMp4,
                AudioBitrateKbps = AudioBitrateKbps,
                DeleteIntermediateFiles = DeleteIntermediateFiles
            };
        }
    }
}
