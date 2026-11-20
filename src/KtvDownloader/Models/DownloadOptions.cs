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

        /// <summary>
        /// 叠加层配置（水印图片 / 文字卡片）。null 或未启用时不烧录。
        /// <para>
        /// 入队时快照一份，不要直接引用全局对象：批量下载可能跑几十分钟，
        /// 期间用户在编辑器里改了配置，正在跑的任务不该跟着变。
        /// </para>
        /// <para>
        /// ⚠️ 开启烧录会<b>强制输出 MP4</b>：烧录必须重编码视频，而 <c>.ts</c> 容器
        /// 拿不到 <c>+faststart</c> 这类 MP4 的好处，音频也得退回 mp2。
        /// <see cref="ConvertToMp4"/> 为 false 时由 <c>DownloadService</c> 覆盖并记日志。
        /// </para>
        /// </summary>
        public OverlayComposition Overlay { get; set; }

        /// <summary>这个任务是否需要烧录叠加层。</summary>
        public bool NeedsBurnIn =>
            Overlay != null && Overlay.Enabled && Overlay.BurnOnDownload && Overlay.HasAnyRenderable();

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
                DeleteIntermediateFiles = DeleteIntermediateFiles,
                // 深拷贝：入队后用户改配置不该影响已经在跑的任务。
                Overlay = Overlay == null ? null : Overlay.Clone()
            };
        }
    }
}
