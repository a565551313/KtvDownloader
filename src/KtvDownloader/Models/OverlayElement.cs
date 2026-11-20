using System;
using System.Globalization;
using System.IO;

namespace KtvDownloader.Models
{
    /// <summary>
    /// 一个叠加元素：画面上的水印图片，或者一张文字卡片（片头/片尾/公告）。
    /// <para>
    /// <b>尺寸一律以 720p 为基准。</b> 边距、字号、描边宽存的都是「视频高 720 时该多大」，
    /// 渲染时按实际视频高度等比缩放（见 <c>OverlayGeometry.ScaleForVideo</c>）。
    /// 这样一套配置在 480p / 720p / 1080p 的片源上看起来是一致的 ——
    /// 否则同一张水印在 1080p 上会小一圈，用户得为每种分辨率各调一遍。
    /// </para>
    /// <para>
    /// 这个对象是<b>预览与烧录之间唯一的契约</b>：预览端拿它用 GDI+ 画在透明叠加层上，
    /// 下载端拿它拼 ffmpeg 的 filter_complex。两边都不许自己另算一套坐标。
    /// </para>
    /// </summary>
    public sealed class OverlayElement
    {
        /// <summary>720p 基准高度。所有像素尺寸都以此为参照缩放。</summary>
        public const int BaselineVideoHeight = 720;

        /// <summary>元素类型：图片或文本。</summary>
        public OverlayElementKind Kind { get; set; } = OverlayElementKind.Image;

        /// <summary>界面列表里显示的名字。留空时按类型自动生成。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>单个元素的开关。关掉后预览和烧录都跳过，但配置保留。</summary>
        public bool Visible { get; set; } = true;

        /// <summary>锚点（九宫格）。</summary>
        public OverlayAnchor Anchor { get; set; } = OverlayAnchor.TopRight;

        /// <summary>水平边距（720p 基准像素）。靠左/靠右时生效，居中时忽略。</summary>
        public int MarginX { get; set; } = 24;

        /// <summary>垂直边距（720p 基准像素）。靠上/靠下时生效，垂直居中时忽略。</summary>
        public int MarginY { get; set; } = 24;

        // ── 时间窗口 ────────────────────────────────────────────────

        /// <summary>
        /// 是否限定显示时间段。<c>false</c> = 全程显示（普通台标水印）；
        /// <c>true</c> = 只在 <see cref="StartSeconds"/>…<see cref="EndSeconds"/> 之间显示（片头/片尾卡片）。
        /// </summary>
        public bool HasTimeWindow { get; set; }

        /// <summary>开始显示的时刻（秒，相对正片起点，含）。</summary>
        public double StartSeconds { get; set; }

        /// <summary>停止显示的时刻（秒，相对正片起点，含）。</summary>
        public double EndSeconds { get; set; } = 5;

        // ── 图片元素 ────────────────────────────────────────────────

        /// <summary>图片路径。建议用带透明通道的 PNG。</summary>
        public string ImagePath { get; set; } = string.Empty;

        /// <summary>
        /// 图片宽度占视频宽度的比例（0.02–0.8）。高度按原图纵横比自动算。
        /// <para>
        /// 用比例而不是固定像素，是因为曲库里的片源分辨率不统一，
        /// 固定像素会让同一个台标在 480p 上糊成一片、在 1080p 上小得看不清。
        /// </para>
        /// </summary>
        public double RelativeWidth { get; set; } = 0.15;

        /// <summary>不透明度（0.05–1.0）。1.0 = 完全不透明。</summary>
        public double Opacity { get; set; } = 1.0;

        // ── 文本元素 ────────────────────────────────────────────────

        /// <summary>显示的文本。支持多行（\n）。</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>字体名。<b>必须能在 C:\Windows\Fonts 下找到对应的单文件字体</b>，见 FontFileResolver。</summary>
        public string FontFamily { get; set; } = "黑体";

        /// <summary>字号（720p 基准像素高度）。</summary>
        public int FontSizePx { get; set; } = 48;

        /// <summary>填充色，形如 <c>#1E90FF</c>。</summary>
        public string FillColor { get; set; } = "#1E90FF";

        /// <summary>描边色，形如 <c>#FFFFFF</c>。</summary>
        public string BorderColor { get; set; } = "#FFFFFF";

        /// <summary>描边宽度（720p 基准像素）。0 = 不描边。</summary>
        public int BorderWidthPx { get; set; } = 3;

        /// <summary>是否画投影（黑色半透明，偏移 2px）。提升在亮背景上的可读性。</summary>
        public bool Shadow { get; set; } = true;

        // ── 工具 ────────────────────────────────────────────────────

        /// <summary>
        /// 修正非法值。配置文件是用户可以手改的，越界值一路传到 ffmpeg 才炸，
        /// 报的错和真正的原因隔好几层，所以在加载时统一夹回合法范围。
        /// </summary>
        public void Normalize()
        {
            if (!Enum.IsDefined(typeof(OverlayAnchor), (int)Anchor)) Anchor = OverlayAnchor.TopRight;
            if (!Enum.IsDefined(typeof(OverlayElementKind), (int)Kind)) Kind = OverlayElementKind.Image;

            MarginX = Clamp(MarginX, 0, 2000);
            MarginY = Clamp(MarginY, 0, 2000);

            // 时间窗口：负数、顺序颠倒、长度为零都修正掉。
            if (StartSeconds < 0) StartSeconds = 0;
            if (EndSeconds < 0) EndSeconds = 0;
            if (HasTimeWindow)
            {
                if (EndSeconds < StartSeconds) EndSeconds = StartSeconds;
                // 起止相同的窗口一帧都不会显示，至少给 0.5 秒，否则用户会以为没生效。
                if (EndSeconds - StartSeconds < 0.5) EndSeconds = StartSeconds + 0.5;
            }

            if (double.IsNaN(RelativeWidth) || double.IsInfinity(RelativeWidth)) RelativeWidth = 0.15;
            RelativeWidth = ClampDouble(RelativeWidth, 0.02, 0.8);

            if (double.IsNaN(Opacity) || double.IsInfinity(Opacity)) Opacity = 1.0;
            Opacity = ClampDouble(Opacity, 0.05, 1.0);

            FontSizePx = Clamp(FontSizePx, 8, 400);
            BorderWidthPx = Clamp(BorderWidthPx, 0, 40);

            FillColor = NormalizeColor(FillColor, "#1E90FF");
            BorderColor = NormalizeColor(BorderColor, "#FFFFFF");

            ImagePath = (ImagePath ?? string.Empty).Trim();
            Text = Text ?? string.Empty;
            FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "黑体" : FontFamily.Trim();
            Name = Name ?? string.Empty;
        }

        /// <summary>
        /// 这个元素<b>配置上</b>完不完整、该不该参与渲染。
        /// <para>
        /// 图片路径为空、文本为空 —— 这些都不该让整次下载失败，
        /// 直接跳过该元素并在日志里说明就行。
        /// </para>
        /// <para>
        /// ⚠️ <b>这里刻意不做 <c>File.Exists</c></b>，两个理由：
        /// </para>
        /// <list type="number">
        /// <item>docs §3.3 的分层铁律里 Models 是"纯数据，无依赖"，
        ///       一个 POCO 的判断方法去读磁盘，单元测试就没法用假路径了
        ///       （<c>OverlayFilterBuilderTests</c> 全程用 <c>C:\img\logo.png</c> 这种不存在的桩路径）。</item>
        /// <item>文件到底读不读得到，<b>只有真正去读的那一刻才知道</b>，而且三个消费者
        ///       已经各自处理好了：预览端 <c>OverlayRenderer.GetImage</c> 返回 null 就跳过；
        ///       烧录端 <c>IOverlayAssetResolver.TryGetImageSize</c> 失败就记 warning 跳过；
        ///       编辑器在刷新属性面板时显式查一次。在这里再查一遍只是提前猜，
        ///       猜错的方向还很难看 —— 水印图放在移动硬盘上、下载时硬盘没插，
        ///       <c>HasAnyRenderable()</c> 会变 false，于是<b>整首静默不烧录</b>，
        ///       连一条 warning 都没有；而现在这条路径会走到烧录、跳过该元素、把原因写进
        ///       <c>BurnInResult.Warnings</c> 告诉用户。</item>
        /// </list>
        /// </summary>
        public bool IsRenderable()
        {
            if (!Visible) return false;

            if (Kind == OverlayElementKind.Image)
                return !string.IsNullOrWhiteSpace(ImagePath);

            return !string.IsNullOrWhiteSpace(Text);
        }

        /// <summary>
        /// 跳过这个元素的原因（用于日志与界面提示），可渲染时返回 null。
        /// <para>和 <see cref="IsRenderable"/> 一样只看配置、不碰磁盘，两者不会互相矛盾。</para>
        /// </summary>
        public string GetSkipReason()
        {
            if (!Visible) return "已关闭";

            if (Kind == OverlayElementKind.Image)
                return string.IsNullOrWhiteSpace(ImagePath) ? "未选择图片" : null;

            return string.IsNullOrWhiteSpace(Text) ? "文本为空" : null;
        }

        /// <summary>界面列表里显示的描述文字。</summary>
        public string Describe()
        {
            var title = Kind == OverlayElementKind.Image
                ? (string.IsNullOrWhiteSpace(Name) ? "图片" : Name)
                : (string.IsNullOrWhiteSpace(Text)
                    ? (string.IsNullOrWhiteSpace(Name) ? "文字" : Name)
                    : Text.Replace("\r", " ").Replace("\n", " "));

            if (title.Length > 18) title = title.Substring(0, 18) + "…";

            var time = HasTimeWindow
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.#}–{1:0.#}s", StartSeconds, EndSeconds)
                : "全程";

            return string.Format("{0}　[{1}　{2}]", title, AnchorText(Anchor), time);
        }

        /// <summary>锚点的中文名（界面用）。</summary>
        public static string AnchorText(OverlayAnchor anchor)
        {
            switch (anchor)
            {
                case OverlayAnchor.TopLeft: return "左上";
                case OverlayAnchor.TopCenter: return "中上";
                case OverlayAnchor.TopRight: return "右上";
                case OverlayAnchor.MiddleLeft: return "中左";
                case OverlayAnchor.Center: return "正中";
                case OverlayAnchor.MiddleRight: return "中右";
                case OverlayAnchor.BottomLeft: return "左下";
                case OverlayAnchor.BottomCenter: return "中下";
                case OverlayAnchor.BottomRight: return "右下";
                default: return anchor.ToString();
            }
        }

        /// <summary>深拷贝。编辑器里"应用/取消"要靠它，别让用户改到一半的配置直接污染全局。</summary>
        public OverlayElement Clone()
        {
            return new OverlayElement
            {
                Kind = Kind,
                Name = Name,
                Visible = Visible,
                Anchor = Anchor,
                MarginX = MarginX,
                MarginY = MarginY,
                HasTimeWindow = HasTimeWindow,
                StartSeconds = StartSeconds,
                EndSeconds = EndSeconds,
                ImagePath = ImagePath,
                RelativeWidth = RelativeWidth,
                Opacity = Opacity,
                Text = Text,
                FontFamily = FontFamily,
                FontSizePx = FontSizePx,
                FillColor = FillColor,
                BorderColor = BorderColor,
                BorderWidthPx = BorderWidthPx,
                Shadow = Shadow
            };
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private static double ClampDouble(double value, double min, double max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        /// <summary>
        /// 颜色统一成 <c>#RRGGBB</c>。
        /// <para>
        /// ffmpeg 的 <c>fontcolor</c> / <c>bordercolor</c> 直接吃这个格式，
        /// GDI+ 那边用 <c>ColorTranslator.FromHtml</c> 也能吃 —— 存一种格式两边都省事。
        /// </para>
        /// </summary>
        private static string NormalizeColor(string value, string fallback)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.StartsWith("#", StringComparison.Ordinal)) text = text.Substring(1);

            // 只接受 6 位十六进制。带 alpha 的 8 位不收 —— 透明度由 Opacity 单独管，
            // 两个地方都能调透明度只会让人搞不清到底哪个生效。
            return text.Length == 6 && IsHex(text) ? ("#" + text).ToUpperInvariant() : fallback;
        }

        private static bool IsHex(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }

            return true;
        }
    }
}
