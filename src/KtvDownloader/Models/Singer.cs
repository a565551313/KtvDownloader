namespace KtvDownloader.Models
{
    /// <summary>
    /// 歌手实体。对应 muse.db 的 singers 表。
    /// </summary>
    public sealed class Singer
    {
        public string Id { get; set; }

        /// <summary>歌手名（可能为空，部分歌手仅有 tid）。</summary>
        public string Name { get; set; }

        /// <summary>拼音首字母，用于检索。</summary>
        public string NameCap { get; set; }

        /// <summary>全拼。</summary>
        public string NameFull { get; set; }

        /// <summary>去除修饰的纯名称。</summary>
        public string NameTrim { get; set; }

        /// <summary>头像标识，形如 ip1_singer_image_1030，拼接 CDN 后可取图。</summary>
        public string Image { get; set; }

        /// <summary>性别：男 / 女 / 组合。</summary>
        public string Type { get; set; }

        /// <summary>地区：大陆 / 港台 / 中国 / 外国。</summary>
        public string Area { get; set; }

        public int HotScore { get; set; }

        public string DisplayName =>
            string.IsNullOrEmpty(Name) ? NameTrim ?? string.Empty : Name;

        public override string ToString() => DisplayName;
    }
}
