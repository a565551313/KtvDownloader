using System;
using System.Globalization;
using KtvDownloader.Models;

namespace KtvDownloader.Services
{
    /// <summary>叠加元素的矩形区域（源视频像素坐标系，原点在左上角）。</summary>
    public struct OverlayRect
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;

        public OverlayRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "({0},{1}) {2}x{3}", X, Y, Width, Height);
        }
    }

    /// <summary>
    /// 叠加元素的定位与缩放几何。<b>纯函数</b>，不碰文件系统、不启动进程。
    /// <para>
    /// 这是预览与烧录之间<b>唯一</b>的坐标来源：GDI+ 画预览时调它，
    /// 拼 ffmpeg 参数时也调它。两边各算一套迟早会出现
    /// "预览里在右上角、烧出来跑到中间"，而这种偏差只有对比成品才能发现。
    /// </para>
    /// <para>
    /// 所有输入的尺寸都以 720p 为基准（见 <see cref="OverlayElement"/>），
    /// 在这里按实际视频高度等比换算，所以同一套配置在 480p/1080p 片源上观感一致。
    /// </para>
    /// </summary>
    public static class OverlayGeometry
    {
        /// <summary>720p 基准高度。</summary>
        public const int BaselineVideoHeight = OverlayElement.BaselineVideoHeight;

        /// <summary>
        /// 把 720p 基准下的像素尺寸换算到实际视频高度。
        /// <para>视频高度非法（&lt;=0）时按基准处理，返回原值，不抛异常。</para>
        /// </summary>
        public static int ScaleForVideo(int valueAt720p, int videoHeight)
        {
            if (videoHeight <= 0) return valueAt720p;
            if (valueAt720p == 0) return 0;

            var scaled = (int)Math.Round(
                valueAt720p * (double)videoHeight / BaselineVideoHeight, MidpointRounding.AwayFromZero);

            // 至少 1 像素：1080p 上把 1px 描边四舍五入成 0 会让描边整个消失。
            return valueAt720p > 0 ? Math.Max(1, scaled) : scaled;
        }

        /// <summary>
        /// 图片元素在视频里的实际像素尺寸。
        /// <para>
        /// 宽 = 视频宽 × <see cref="OverlayElement.RelativeWidth"/>；
        /// 高按原图纵横比推。原图尺寸拿不到时（<paramref name="imageNaturalWidth"/> &lt;= 0）
        /// 退化为正方形，至少不会画出一个 0 高度的区域。
        /// </para>
        /// </summary>
        public static void ResolveImageSize(
            int imageNaturalWidth,
            int imageNaturalHeight,
            double relativeWidth,
            int videoWidth,
            out int width,
            out int height)
        {
            var vw = videoWidth > 0 ? videoWidth : 1280;
            var w = (int)Math.Round(vw * relativeWidth, MidpointRounding.AwayFromZero);
            width = Math.Max(1, w);

            if (imageNaturalWidth > 0 && imageNaturalHeight > 0)
            {
                var h = (int)Math.Round(
                    width * (double)imageNaturalHeight / imageNaturalWidth, MidpointRounding.AwayFromZero);
                height = Math.Max(1, h);
            }
            else
            {
                height = width;
            }
        }

        /// <summary>
        /// 由锚点、边距、视频尺寸、元素尺寸算出元素左上角坐标，并夹在画面内。
        /// <para>
        /// 夹取是必须的：元素比画面还大（比如用户把 RelativeWidth 调到 0.8 又是竖图）时，
        /// 不夹会得到负的 x/y，ffmpeg 的 overlay 会把元素画到画面外，看起来就是"水印消失了"。
        /// </para>
        /// </summary>
        public static OverlayRect ResolveRect(
            OverlayAnchor anchor,
            int marginXAt720p,
            int marginYAt720p,
            int videoWidth,
            int videoHeight,
            int elementWidth,
            int elementHeight)
        {
            var vw = videoWidth > 0 ? videoWidth : 1280;
            var vh = videoHeight > 0 ? videoHeight : 720;

            var mx = ScaleForVideo(Math.Max(0, marginXAt720p), vh);
            var my = ScaleForVideo(Math.Max(0, marginYAt720p), vh);

            int x;
            switch (anchor)
            {
                case OverlayAnchor.TopLeft:
                case OverlayAnchor.MiddleLeft:
                case OverlayAnchor.BottomLeft:
                    x = mx;
                    break;

                case OverlayAnchor.TopCenter:
                case OverlayAnchor.Center:
                case OverlayAnchor.BottomCenter:
                    x = (vw - elementWidth) / 2;
                    break;

                default:
                    x = vw - elementWidth - mx;
                    break;
            }

            int y;
            switch (anchor)
            {
                case OverlayAnchor.TopLeft:
                case OverlayAnchor.TopCenter:
                case OverlayAnchor.TopRight:
                    y = my;
                    break;

                case OverlayAnchor.MiddleLeft:
                case OverlayAnchor.Center:
                case OverlayAnchor.MiddleRight:
                    y = (vh - elementHeight) / 2;
                    break;

                default:
                    y = vh - elementHeight - my;
                    break;
            }

            // 夹到画面内。元素比画面宽/高时贴到 0，让它至少完整露出左上部分。
            x = Clamp(x, 0, Math.Max(0, vw - elementWidth));
            y = Clamp(y, 0, Math.Max(0, vh - elementHeight));

            return new OverlayRect(x, y, Math.Max(1, elementWidth), Math.Max(1, elementHeight));
        }

        /// <summary>
        /// 图片元素在指定视频上的最终矩形（含缩放）。预览与烧录都调这个。
        /// </summary>
        public static OverlayRect ResolveImageRect(
            OverlayElement element,
            int imageNaturalWidth,
            int imageNaturalHeight,
            int videoWidth,
            int videoHeight)
        {
            if (element == null) return new OverlayRect(0, 0, 1, 1);

            int w, h;
            ResolveImageSize(imageNaturalWidth, imageNaturalHeight,
                element.RelativeWidth, videoWidth, out w, out h);

            return ResolveRect(element.Anchor, element.MarginX, element.MarginY,
                videoWidth, videoHeight, w, h);
        }

        /// <summary>
        /// 生成 ffmpeg <c>drawtext</c> 的 x/y 表达式。
        /// <para>
        /// ⚠️ 文本必须用<b>表达式</b>而不是具体数字：文字渲染后的宽高只有
        /// freetype 在运行时才知道（GDI+ 量出来的和它不完全相等），
        /// 所以交给 ffmpeg 用它自己的 <c>main_w</c>/<c>main_h</c>（视频尺寸）
        /// 和 <c>w</c>/<c>h</c>（文本尺寸）来定位，这样锚点语义才和预览一致。
        /// </para>
        /// </summary>
        /// <param name="marginAt720p">边距（720p 基准），内部按视频高度换算。</param>
        /// <param name="videoHeight">实际视频高度，用于换算边距。</param>
        /// <param name="horizontal">true 求 x 表达式，false 求 y 表达式。</param>
        public static string BuildTextPositionExpression(
            OverlayAnchor anchor, int marginAt720p, int videoHeight, bool horizontal)
        {
            var margin = ScaleForVideo(Math.Max(0, marginAt720p), videoHeight);
            var m = margin.ToString(CultureInfo.InvariantCulture);

            if (horizontal)
            {
                switch (anchor)
                {
                    case OverlayAnchor.TopLeft:
                    case OverlayAnchor.MiddleLeft:
                    case OverlayAnchor.BottomLeft:
                        return m;

                    case OverlayAnchor.TopCenter:
                    case OverlayAnchor.Center:
                    case OverlayAnchor.BottomCenter:
                        return "(main_w-w)/2";

                    default:
                        return margin == 0 ? "main_w-w" : "main_w-w-" + m;
                }
            }

            switch (anchor)
            {
                case OverlayAnchor.TopLeft:
                case OverlayAnchor.TopCenter:
                case OverlayAnchor.TopRight:
                    return m;

                case OverlayAnchor.MiddleLeft:
                case OverlayAnchor.Center:
                case OverlayAnchor.MiddleRight:
                    return "(main_h-h)/2";

                default:
                    return margin == 0 ? "main_h-h" : "main_h-h-" + m;
            }
        }

        /// <summary>
        /// 某时刻这个元素该不该显示（预览用）。
        /// <para>没设时间窗口 = 全程显示。窗口是闭区间，含首尾两端。</para>
        /// </summary>
        public static bool IsVisibleAt(OverlayElement element, double timeSeconds)
        {
            if (element == null || !element.Visible) return false;
            if (!element.HasTimeWindow) return true;

            return timeSeconds >= element.StartSeconds && timeSeconds <= element.EndSeconds;
        }

        /// <summary>
        /// 生成 ffmpeg 滤镜的 <c>enable</c> 表达式内容；全程显示时返回 null（调用方就别输出 enable=）。
        /// <para>
        /// ⚠️ 返回值<b>不含</b>外层单引号。表达式里的逗号会被 filtergraph 解析器
        /// 当成滤镜分隔符，调用方必须用 <c>'…'</c> 包住（见 <see cref="FfmpegFilterText.QuoteFilterValue"/>）。
        /// </para>
        /// </summary>
        public static string BuildEnableExpression(OverlayElement element)
        {
            if (element == null || !element.HasTimeWindow) return null;

            var start = element.StartSeconds;
            var end = element.EndSeconds;
            if (end < start) end = start;

            return string.Format(CultureInfo.InvariantCulture,
                "between(t,{0:0.###},{1:0.###})", start, end);
        }

        /// <summary>
        /// <see cref="ScaleForVideo"/> 的逆运算：把实际视频上的像素尺寸换算回 720p 基准值。
        /// 拖拽时用它，保证"拖到哪、存的就是哪"，而不是存一个烧出来会跑偏的数。
        /// </summary>
        public static int UnscaleFromVideo(int scaledValue, int videoHeight)
        {
            if (videoHeight <= 0) return scaledValue;

            return (int)Math.Round(
                scaledValue * (double)BaselineVideoHeight / videoHeight, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// 把元素拖到指定位置：反推出锚点与边距并写回元素。
        /// <para>
        /// 为什么不是简单地"改坐标"：模型里存的是<b>锚点 + 边距</b>（这样换分辨率、
        /// 换片源都不用重调），所以拖拽必须把目标位置反解回这套参数。
        /// </para>
        /// <para>
        /// 锚点按元素中心落在画面的九分区自动吸附 —— 拖到左边就变成靠左锚点，
        /// 拖到中间就变成居中锚点。这让拖拽和「预设位置」按钮是同一套语义：
        /// 拖完之后按预设按钮，位置不会跳。
        /// </para>
        /// </summary>
        /// <param name="element">要移动的元素（会被就地修改）。</param>
        /// <param name="targetX">目标左上角 X（视频像素）。</param>
        /// <param name="targetY">目标左上角 Y（视频像素）。</param>
        /// <param name="elementWidth">元素宽（视频像素），居中/靠右锚点要用它算边距。</param>
        /// <param name="elementHeight">元素高（视频像素）。</param>
        public static void ApplyDrag(
            OverlayElement element,
            int videoWidth,
            int videoHeight,
            int elementWidth,
            int elementHeight,
            int targetX,
            int targetY)
        {
            if (element == null) return;

            var vw = videoWidth > 0 ? videoWidth : 1280;
            var vh = videoHeight > 0 ? videoHeight : 720;

            // 夹到画面内，免得拖出去之后算出负边距。
            var x = Clamp(targetX, 0, Math.Max(0, vw - elementWidth));
            var y = Clamp(targetY, 0, Math.Max(0, vh - elementHeight));

            var centerX = x + elementWidth / 2.0;
            var centerY = y + elementHeight / 2.0;

            // 42% / 58% 两个阈值：中间留 16% 的"居中带"，
            // 太窄的话想拖到正中很难对准，太宽则左右两边没地方放。
            var horizontal = centerX < vw * 0.42 ? -1 : (centerX > vw * 0.58 ? 1 : 0);
            var vertical = centerY < vh * 0.42 ? -1 : (centerY > vh * 0.58 ? 1 : 0);

            element.Anchor = ComposeAnchor(vertical, horizontal);

            // 边距换算回 720p 基准（ScaleForVideo 用的是 videoHeight，两个轴都一样）。
            if (horizontal < 0) element.MarginX = Math.Max(0, UnscaleFromVideo(x, vh));
            else if (horizontal > 0) element.MarginX = Math.Max(0, UnscaleFromVideo(vw - elementWidth - x, vh));

            if (vertical < 0) element.MarginY = Math.Max(0, UnscaleFromVideo(y, vh));
            else if (vertical > 0) element.MarginY = Math.Max(0, UnscaleFromVideo(vh - elementHeight - y, vh));

            element.Normalize();
        }

        /// <summary>把"垂直分区(-1/0/1) + 水平分区(-1/0/1)"合成锚点枚举。</summary>
        private static OverlayAnchor ComposeAnchor(int vertical, int horizontal)
        {
            if (vertical < 0)
            {
                if (horizontal < 0) return OverlayAnchor.TopLeft;
                return horizontal > 0 ? OverlayAnchor.TopRight : OverlayAnchor.TopCenter;
            }

            if (vertical > 0)
            {
                if (horizontal < 0) return OverlayAnchor.BottomLeft;
                return horizontal > 0 ? OverlayAnchor.BottomRight : OverlayAnchor.BottomCenter;
            }

            if (horizontal < 0) return OverlayAnchor.MiddleLeft;
            return horizontal > 0 ? OverlayAnchor.MiddleRight : OverlayAnchor.Center;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (max < min) max = min;
            return value < min ? min : (value > max ? max : value);
        }
    }
}
