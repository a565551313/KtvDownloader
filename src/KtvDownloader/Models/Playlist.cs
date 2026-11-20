namespace KtvDownloader.Models
{
    /// <summary>
    /// 歌单 / 榜单实体。对应 muse.db 的 playlists 表。
    /// </summary>
    public sealed class Playlist
    {
        public string Id { get; set; }

        /// <summary>歌单名称。</summary>
        public string Name { get; set; }

        /// <summary>1=主题歌单（248 个），2=榜单（9 个），3=排行榜（19 个）。</summary>
        public int Type { get; set; }

        /// <summary>曲目数量。</summary>
        public int SongCount { get; set; }

        /// <summary>是否高品质歌单。</summary>
        public bool IsHighQuality { get; set; }

        /// <summary>类型的中文描述。</summary>
        public string TypeName
        {
            get
            {
                switch (Type)
                {
                    case 1: return "主题歌单";
                    case 2: return "榜单";
                    case 3: return "排行榜";
                    default: return "其它";
                }
            }
        }

        public override string ToString() => Name;
    }
}
