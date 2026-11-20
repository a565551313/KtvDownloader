namespace KtvDownloader.Models
{
    /// <summary>
    /// 歌曲实体。对应 muse.db 的 songs 表。
    /// </summary>
    public sealed class Song
    {
        /// <summary>数据库主键（UUID）。</summary>
        public string Id { get; set; }

        /// <summary>歌号（纯数字，如 7300654），调用取址接口时使用。</summary>
        public string MusicNo { get; set; }

        /// <summary>完整歌名，可能带 (HD) 等后缀。</summary>
        public string Name { get; set; }

        /// <summary>去掉后缀的纯歌名。</summary>
        public string NameTrim { get; set; }

        /// <summary>歌手名（多个歌手用 / 分隔）。</summary>
        public string Singer { get; set; }

        /// <summary>语种：国语 / 粤语 / 闽南语 / 英语 / 日语 / 韩语 / 其它。</summary>
        public string Language { get; set; }

        /// <summary>热度分，越大越热门。</summary>
        public int HotScore { get; set; }

        /// <summary>文件名，如 7300654.ts。扩展名决定媒体类型。</summary>
        public string FileName { get; set; }

        /// <summary>声道模式：1 或 2，决定左右声道哪边是伴奏。</summary>
        public int Accomp { get; set; }

        /// <summary>MV 类型。</summary>
        public string MvType { get; set; }

        /// <summary>默认音量（0-100）。</summary>
        public int Volume { get; set; }

        /// <summary>标签列表（来自 tags_str 字段）。</summary>
        public string[] Tags { get; set; } = new string[0];

        /// <summary>媒体类型，由扩展名推导。</summary>
        public MediaKind Kind
        {
            get
            {
                var name = (FileName ?? string.Empty).ToLowerInvariant();
                if (name.EndsWith(".ts")) return MediaKind.TransportStream;
                if (name.EndsWith(".mp3")) return MediaKind.Audio;
                if (name.EndsWith(".ls") || name.EndsWith(".kls")) return MediaKind.LegacyStream;
                return MediaKind.Unknown;
            }
        }

        /// <summary>用于列表显示的文本，形如「光良 - 童话」。</summary>
        public string DisplayName =>
            string.IsNullOrEmpty(Singer) ? Name : Singer + " - " + Name;

        public override string ToString() => DisplayName;
    }
}
