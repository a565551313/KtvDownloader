namespace KtvDownloader.Models
{
    /// <summary>
    /// 叠加元素在画面上的锚点（九宫格）。
    /// <para>
    /// 命名按「垂直位置 + 水平位置」读：<c>MiddleLeft</c> = 垂直居中、靠左。
    /// 界面预设只放出上两排 + 中排（<c>TopLeft</c>…<c>MiddleRight</c>），
    /// 因为 KTV 画面底部通常留给歌名条和字幕，压上去会打架；
    /// 但枚举本身保留下排，需要时（比如片尾致谢）照样能用。
    /// </para>
    /// </summary>
    public enum OverlayAnchor
    {
        /// <summary>左上。</summary>
        TopLeft = 0,

        /// <summary>中上。</summary>
        TopCenter = 1,

        /// <summary>右上。</summary>
        TopRight = 2,

        /// <summary>中左（垂直居中、靠左）。</summary>
        MiddleLeft = 3,

        /// <summary>正中。</summary>
        Center = 4,

        /// <summary>中右（垂直居中、靠右）。</summary>
        MiddleRight = 5,

        /// <summary>左下。</summary>
        BottomLeft = 6,

        /// <summary>中下。</summary>
        BottomCenter = 7,

        /// <summary>右下。</summary>
        BottomRight = 8
    }

    /// <summary>叠加元素的类型。</summary>
    public enum OverlayElementKind
    {
        /// <summary>图片（透明 PNG 最佳）。</summary>
        Image = 0,

        /// <summary>文本卡片。</summary>
        Text = 1
    }
}
