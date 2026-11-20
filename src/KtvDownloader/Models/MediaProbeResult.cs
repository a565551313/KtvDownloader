using System.Globalization;

namespace KtvDownloader.Models
{
    /// <summary>
    /// 一次 <c>ffmpeg -i</c> 探测的结果。
    /// <para>
    /// 为什么合成一个类型：音轨数、视频分辨率、时长<b>都来自同一条命令的同一段 stderr</b>。
    /// 原来只有 <c>CountAudioTracks</c>，烧录叠加层还需要分辨率（算坐标）和时长（算进度），
    /// 分成三次探测就是把同一条命令跑三遍 —— 每次几十到几百毫秒，还各自缓存、各自可能不一致。
    /// </para>
    /// </summary>
    public sealed class MediaProbeResult
    {
        /// <summary>探测是否成功。<c>false</c> 时下面几个值都不可信，且<b>不应被缓存</b>。</summary>
        public bool Usable { get; set; }

        /// <summary>音频轨数量。0 可能是真的没音轨，也可能是探测失败 —— 看 <see cref="Usable"/>。</summary>
        public int AudioTrackCount { get; set; }

        /// <summary>
        /// 第一条音轨的编码名（<c>aac</c> / <c>mp2</c> / <c>mp3</c> …），未知为 null。
        /// <para>
        /// 烧录叠加层时靠它决定音频是重编码还是直接拷贝：中间产物已经是 AAC 就 <c>-c:a copy</c>，
        /// 白转一遍 AAC 只是凭空多一代损失。而源是 TS（音轨 mp2）时<b>必须</b>重编码 ——
        /// mp2 直接 copy 进 MP4 会被标成 mp3，多数播放器解不出来，表现为"画面正常但完全没声音"（坑1）。
        /// </para>
        /// </summary>
        public string AudioCodec { get; set; }

        /// <summary>音频是否已经是 AAC（可以直接流拷贝进 MP4）。</summary>
        public bool AudioIsAac =>
            AudioCodec != null &&
            string.Equals(AudioCodec, "aac", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>视频宽（像素）。未知为 0。</summary>
        public int VideoWidth { get; set; }

        /// <summary>视频高（像素）。未知为 0。</summary>
        public int VideoHeight { get; set; }

        /// <summary>总时长（秒）。未知为 0。</summary>
        public double DurationSeconds { get; set; }

        /// <summary>视频尺寸是否已知。烧录叠加层必须要它，否则坐标算不出来。</summary>
        public bool HasVideoSize => VideoWidth > 0 && VideoHeight > 0;

        /// <summary>一个"什么都没探到"的结果。</summary>
        public static MediaProbeResult Empty()
        {
            return new MediaProbeResult { Usable = false };
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0}x{1}, {2:0.##}s, {3} 条音轨, usable={4}",
                VideoWidth, VideoHeight, DurationSeconds, AudioTrackCount, Usable);
        }
    }
}
