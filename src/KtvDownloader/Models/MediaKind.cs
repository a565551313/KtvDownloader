namespace KtvDownloader.Models
{
    /// <summary>
    /// 媒体类型。由曲库中 filename 的扩展名推导。
    /// </summary>
    public enum MediaKind
    {
        /// <summary>未知。</summary>
        Unknown = 0,

        /// <summary>MPEG-TS 卡拉OK视频（主流，697493 首）。</summary>
        TransportStream = 1,

        /// <summary>纯音频 MP3（1157 首）。</summary>
        Audio = 2,

        /// <summary>旧式流封装 .ls / .kls（8488 首）。</summary>
        LegacyStream = 3
    }
}
