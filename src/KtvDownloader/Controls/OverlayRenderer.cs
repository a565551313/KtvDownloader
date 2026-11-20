using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using KtvDownloader.Models;
using KtvDownloader.Services;

namespace KtvDownloader.Controls
{
    /// <summary>
    /// 用 GDI+ 把 <see cref="OverlayComposition"/> 画到一块画布上。<b>预览专用的渲染器。</b>
    /// <para>
    /// <b>放在 Controls 而不是 Services</b>：docs §3.3 的铁律是 Services 层不碰 UI。
    /// 这个类只引用 <c>System.Drawing</c>、没引用 <c>System.Windows.Forms</c>，
    /// 字面上不算违规，但它干的就是"往画布上画东西"，是纯表现层的事，
    /// 而且只有 <see cref="OverlayDesignSurface"/> 与 <see cref="OverlayLayeredWindow"/>
    /// 两个 UI 宿主在用它 —— 放 Services 只会让人误以为能在业务逻辑里调。
    /// 依赖方向依然是 UI → Services（这里反过来调 <see cref="OverlayGeometry"/> 与
    /// <see cref="OverlayFilterBuilder"/>），没有循环。
    /// </para>
    /// <para>
    /// 两个宿主共用它：编辑器里的设计画布（普通控件）和预览时盖在视频上的分层窗口。
    /// 共用一份渲染代码，两个界面才不会长得不一样。
    /// </para>
    /// <para>
    /// <b>坐标策略</b>：所有绘制都在「视频像素坐标系」里进行，
    /// 由调用方通过 <see cref="Graphics.ScaleTransform"/> 缩放到实际显示尺寸。
    /// 这样字号、边距、描边全都是按视频分辨率算的，和 ffmpeg 那边
    /// （<see cref="OverlayFilterBuilder"/> 同样按视频高度换算）用的是同一套数字。
    /// </para>
    /// <para>
    /// <b>与成品必然存在的差异</b>（别对外承诺像素级一致）：
    /// <list type="bullet">
    /// <item>文字用 GDI+ 排版，成品用 libfreetype，字距和文本块的宽高会有几像素差；</item>
    /// <item>描边对齐做了近似：ffmpeg 的 <c>borderw</c> 只向外扩，
    ///       GDI+ 的 <c>Pen</c> 以路径为中心线，所以这里用 <c>pen.Width = 2 × borderw</c> 去凑外扩量；</item>
    /// <item>字重由字体文件决定。<b>这里刻意不用 <c>FontStyle.Bold</c></b> ——
    ///       ffmpeg 的 drawtext 没有加粗选项，粗体只能靠指向一个粗体字体文件。
    ///       预览这边要是偷偷加粗，就会出现"预览是粗的、成品是细的"。</item>
    /// </list>
    /// 所以界面上要给一个「试烧样片」按钮，让用户拿真成品确认。
    /// </para>
    /// </summary>
    public static class OverlayRenderer
    {
        /// <summary>多行文本的行距（720p 基准像素），与 <c>OverlayFilterBuilder</c> 保持一致。</summary>
        private const int LineSpacingAt720p = 6;

        /// <summary>投影偏移（720p 基准像素），与 <c>OverlayFilterBuilder</c> 保持一致。</summary>
        private const int ShadowOffsetAt720p = 2;

        /// <summary>投影不透明度，与 <c>OverlayFilterBuilder</c> 保持一致。</summary>
        private const float ShadowOpacity = 0.5f;

        /// <summary>图片缓存：路径 → 图像（读不到时缓存 null，避免每帧都去撞一次磁盘）。</summary>
        private static readonly Dictionary<string, Image> ImageCache =
            new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        private static readonly object CacheSync = new object();

        /// <summary>
        /// 把叠加层画到 <paramref name="graphics"/> 上。
        /// <para>
        /// 调用前请先设置好变换，使「视频像素坐标」映射到画布坐标，例如：
        /// <c>g.ScaleTransform(canvasWidth / (float)videoWidth, canvasHeight / (float)videoHeight)</c>。
        /// </para>
        /// </summary>
        /// <param name="graphics">目标画布，变换已设好。</param>
        /// <param name="composition">叠加配置。</param>
        /// <param name="videoWidth">视频宽（像素），决定坐标系的宽度。</param>
        /// <param name="videoHeight">视频高（像素），决定 720p 基准尺寸的缩放比。</param>
        /// <param name="timeSeconds">当前播放时刻，用于时间窗口过滤；设计画布传 -1 表示全部显示。</param>
        /// <param name="selected">当前选中的元素，会画上选中框；没有传 null。</param>
        /// <param name="showAll">true 时忽略时间窗口，把所有元素都画出来（编辑时用）。</param>
        public static void Render(
            Graphics graphics,
            OverlayComposition composition,
            int videoWidth,
            int videoHeight,
            double timeSeconds,
            OverlayElement selected,
            bool showAll)
        {
            if (graphics == null || composition == null || !composition.Enabled) return;
            if (videoWidth <= 0 || videoHeight <= 0) return;

            foreach (var element in composition.Elements)
            {
                if (element == null || !element.Visible) continue;

                // 时间窗口：设计画布（showAll）和"未开始播放"时不过滤，
                // 否则用户刚打开编辑器什么都看不见，以为功能坏了。
                if (!showAll && !OverlayGeometry.IsVisibleAt(element, timeSeconds)) continue;

                try
                {
                    if (element.Kind == OverlayElementKind.Image)
                        RenderImage(graphics, element, videoWidth, videoHeight);
                    else
                        RenderText(graphics, element, videoWidth, videoHeight);

                    if (ReferenceEquals(element, selected))
                        RenderSelection(graphics, element, videoWidth, videoHeight);
                }
                catch (Exception)
                {
                    // 单个元素画失败（图片被删、字体缺失）不该让整个预览黑掉。
                }
            }
        }

        private static void RenderImage(Graphics graphics, OverlayElement element, int videoWidth, int videoHeight)
        {
            var image = GetImage(element.ImagePath);
            if (image == null) return;

            var rect = OverlayGeometry.ResolveImageRect(element, image.Width, image.Height, videoWidth, videoHeight);

            var target = new Rectangle(rect.X, rect.Y, rect.Width, rect.Height);

            if (element.Opacity >= 0.999)
            {
                graphics.DrawImage(image, target);
                return;
            }

            // 半透明：用 ColorMatrix 调 alpha 通道，比逐像素改快得多。
            using (var attributes = new ImageAttributes())
            {
                var matrix = new ColorMatrix { Matrix33 = (float)element.Opacity };
                attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                graphics.DrawImage(image, target, 0, 0, image.Width, image.Height,
                    GraphicsUnit.Pixel, attributes);
            }
        }

        /// <summary>一次文本测量的结果。<see cref="MeasureTextBlock"/> 产出，渲染与命中测试共用。</summary>
        private sealed class TextMetrics
        {
            public string[] Lines;
            public float[] LineWidths;
            public float BlockWidth;
            public float LineHeight;
            public int FontSize;
            public Rectangle Bounds;
        }

        /// <summary>
        /// 量出一段文本在视频坐标系里占的矩形。
        /// <para>
        /// <b>渲染和命中测试必须共用这一个方法。</b>各量一遍的话，
        /// 迟早出现"看着点在字上却选不中"—— 而那种偏差很难查，因为两块代码看起来都对。
        /// </para>
        /// </summary>
        private static TextMetrics MeasureTextBlock(
            Graphics graphics, OverlayElement element, int videoWidth, int videoHeight, Font font)
        {
            var text = OverlayFilterBuilder.NormalizeNewLines(element.Text);
            var lines = text.Split('\n');

            var fontSize = OverlayGeometry.ScaleForVideo(element.FontSizePx, videoHeight);
            var lineSpacing = OverlayGeometry.ScaleForVideo(LineSpacingAt720p, videoHeight);

            var widths = new float[lines.Length];
            var blockWidth = 0f;

            for (var i = 0; i < lines.Length; i++)
            {
                widths[i] = lines[i].Length == 0
                    ? 0f
                    : graphics.MeasureString(lines[i], font, int.MaxValue, StringFormat.GenericTypographic).Width;
                if (widths[i] > blockWidth) blockWidth = widths[i];
            }

            var lineHeight = fontSize + lineSpacing;
            var blockHeight = lineHeight * lines.Length;

            // 用和 ffmpeg 完全相同的锚点公式定位（同一个 OverlayGeometry 方法，
            // 只是那边输出的是 main_w/main_h 表达式，这边代入具体数字）。
            var rect = OverlayGeometry.ResolveRect(
                element.Anchor, element.MarginX, element.MarginY,
                videoWidth, videoHeight,
                (int)Math.Ceiling(blockWidth), (int)Math.Ceiling(blockHeight));

            return new TextMetrics
            {
                Lines = lines,
                LineWidths = widths,
                BlockWidth = blockWidth,
                LineHeight = lineHeight,
                FontSize = fontSize,
                Bounds = new Rectangle(rect.X, rect.Y, rect.Width, rect.Height)
            };
        }

        private static void RenderText(Graphics graphics, OverlayElement element, int videoWidth, int videoHeight)
        {
            var text = OverlayFilterBuilder.NormalizeNewLines(element.Text);
            if (text.Length == 0) return;

            var fontSize = OverlayGeometry.ScaleForVideo(element.FontSizePx, videoHeight);
            var borderWidth = OverlayGeometry.ScaleForVideo(element.BorderWidthPx, videoHeight);
            var shadowOffset = OverlayGeometry.ScaleForVideo(ShadowOffsetAt720p, videoHeight);

            using (var font = CreateFont(element.FontFamily, fontSize))
            {
                if (font == null) return;

                var metrics = MeasureTextBlock(graphics, element, videoWidth, videoHeight, font);
                var lines = metrics.Lines;
                var widths = metrics.LineWidths;
                var blockWidth = metrics.BlockWidth;
                var lineHeight = metrics.LineHeight;
                var rect = metrics.Bounds;

                var fill = ParseColor(element.FillColor, Color.DodgerBlue);
                var border = ParseColor(element.BorderColor, Color.White);

                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                var originX = rect.X;
                var originY = rect.Y;

                using (var path = new GraphicsPath())
                {
                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Length == 0) continue;

                        // 居中锚点时每行各自居中，和 ffmpeg 的行为一致（它按整块排版但每行独立）。
                        var x = originX;
                        if (element.Anchor == OverlayAnchor.TopCenter ||
                            element.Anchor == OverlayAnchor.Center ||
                            element.Anchor == OverlayAnchor.BottomCenter)
                        {
                            x = originX + (int)Math.Round((blockWidth - widths[i]) / 2f);
                        }

                        path.AddString(lines[i], font.FontFamily, (int)FontStyle.Regular,
                            fontSize, new PointF(x, originY + lineHeight * i), StringFormat.GenericTypographic);
                    }

                    if (path.PointCount == 0) return;

                    if (element.Shadow)
                    {
                        using (var shadowBrush = new SolidBrush(Color.FromArgb((int)(ShadowOpacity * 255), 0, 0, 0)))
                        using (var shadowMatrix = new Matrix())
                        {
                            shadowMatrix.Translate(shadowOffset, shadowOffset);
                            using (var shadowPath = (GraphicsPath)path.Clone())
                            {
                                shadowPath.Transform(shadowMatrix);
                                graphics.FillPath(shadowBrush, shadowPath);
                            }
                        }
                    }

                    if (borderWidth > 0)
                    {
                        // ⚠️ pen.Width = 2 × borderw：GDI+ 的 pen 以路径为中心线（内外各一半），
                        // 而 ffmpeg 的 borderw 只向外扩。用两倍宽度才能让外扩量对上。
                        using (var pen = new Pen(border, borderWidth * 2f)
                        {
                            LineJoin = LineJoin.Round,
                            StartCap = LineCap.Round,
                            EndCap = LineCap.Round
                        })
                        {
                            graphics.DrawPath(pen, path);
                        }
                    }

                    using (var brush = new SolidBrush(fill))
                    {
                        graphics.FillPath(brush, path);
                    }
                }
            }
        }

        /// <summary>
        /// 元素在视频坐标系里的包围盒。选中框、拖拽命中测试都靠它。
        /// <para>
        /// <b>必须和实际渲染用的是同一套测量</b>（图片走 <see cref="OverlayGeometry.ResolveImageRect"/>，
        /// 文本走 <see cref="MeasureTextBlock"/>），否则会出现"框和字对不上"或者"点在字上选不中"。
        /// </para>
        /// </summary>
        public static Rectangle GetElementBounds(
            Graphics graphics, OverlayElement element, int videoWidth, int videoHeight)
        {
            if (graphics == null || element == null || videoWidth <= 0 || videoHeight <= 0)
                return Rectangle.Empty;

            if (element.Kind == OverlayElementKind.Image)
            {
                var image = GetImage(element.ImagePath);
                var rect = OverlayGeometry.ResolveImageRect(element,
                    image == null ? 0 : image.Width,
                    image == null ? 0 : image.Height,
                    videoWidth, videoHeight);

                return new Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
            }

            var fontSize = OverlayGeometry.ScaleForVideo(element.FontSizePx, videoHeight);
            using (var font = CreateFont(element.FontFamily, fontSize))
            {
                if (font == null) return Rectangle.Empty;
                return MeasureTextBlock(graphics, element, videoWidth, videoHeight, font).Bounds;
            }
        }

        /// <summary>选中框：虚线矩形。画在视频坐标系里，所以线宽要按分辨率缩放。</summary>
        private static void RenderSelection(Graphics graphics, OverlayElement element, int videoWidth, int videoHeight)
        {
            var bounds = GetElementBounds(graphics, element, videoWidth, videoHeight);
            if (bounds.IsEmpty) return;

            bounds.Inflate(4, 4);

            using (var pen = new Pen(Color.FromArgb(220, 0, 200, 255), Math.Max(1f, videoHeight / 480f))
            {
                DashStyle = DashStyle.Dash
            })
            {
                graphics.DrawRectangle(pen, bounds);
            }
        }

        /// <summary>
        /// 按字体名建一个 GDI+ 字体。
        /// <para>
        /// <b>刻意用 <c>FontStyle.Regular</c></b>：ffmpeg 的 drawtext 没有加粗选项，
        /// 预览这边加粗了就会出现"预览是粗的、成品是细的"。要粗体就换一个粗体字体文件。
        /// </para>
        /// </summary>
        private static Font CreateFont(string family, int sizePx)
        {
            try
            {
                return new Font(string.IsNullOrWhiteSpace(family) ? "黑体" : family,
                    Math.Max(1, sizePx), FontStyle.Regular, GraphicsUnit.Pixel);
            }
            catch (Exception)
            {
                try
                {
                    return new Font(FontFamily.GenericSansSerif, Math.Max(1, sizePx),
                        FontStyle.Regular, GraphicsUnit.Pixel);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>取图片（带缓存）。读不到返回 null，并且把"读不到"也缓存下来。</summary>
        public static Image GetImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            lock (CacheSync)
            {
                Image cached;
                if (ImageCache.TryGetValue(path, out cached)) return cached;
            }

            Image image = null;

            try
            {
                if (File.Exists(path))
                {
                    // 读进内存再建 Image：Image.FromFile 会把文件锁住直到 Dispose，
                    // 而水印图正好是用户可能在别处编辑的文件。
                    var bytes = File.ReadAllBytes(path);
                    using (var stream = new MemoryStream(bytes))
                    {
                        image = Image.FromStream(stream);
                    }
                }
            }
            catch (Exception)
            {
                image = null;
            }

            lock (CacheSync)
            {
                Image existing;
                if (ImageCache.TryGetValue(path, out existing)) return existing;
                ImageCache[path] = image;
            }

            return image;
        }

        /// <summary>
        /// 让某张图片的缓存失效。
        /// <para>用户在编辑器里换了图、或者用同一个路径重新导出了图之后必须调，
        /// 否则预览一直显示旧的那张。</para>
        /// </summary>
        public static void InvalidateImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            lock (CacheSync)
            {
                Image cached;
                if (ImageCache.TryGetValue(path, out cached))
                {
                    ImageCache.Remove(path);
                    if (cached != null) cached.Dispose();
                }
            }
        }

        /// <summary>清空图片缓存。窗口关闭时调，别让 GDI+ 句柄一直挂着。</summary>
        public static void ClearImageCache()
        {
            lock (CacheSync)
            {
                foreach (var pair in ImageCache)
                {
                    if (pair.Value != null) pair.Value.Dispose();
                }

                ImageCache.Clear();
            }
        }

        /// <summary><c>#RRGGBB</c> → <see cref="Color"/>。非法时用兜底色，不抛异常。</summary>
        public static Color ParseColor(string htmlColor, Color fallback)
        {
            var text = (htmlColor ?? string.Empty).Trim();
            if (text.Length == 0) return fallback;

            try
            {
                return ColorTranslator.FromHtml(text.StartsWith("#") ? text : "#" + text);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        /// <summary>
        /// 按视频纵横比算出「等比缩放并居中」后在画布里的矩形（letterbox）。
        /// <para>
        /// libVLC 默认就是这么摆放画面的，所以要盖在视频上必须先算出同一块区域，
        /// 否则叠加层会画到黑边上、或者整体偏移。
        /// </para>
        /// </summary>
        public static Rectangle FitRect(int videoWidth, int videoHeight, int canvasWidth, int canvasHeight)
        {
            if (videoWidth <= 0 || videoHeight <= 0 || canvasWidth <= 0 || canvasHeight <= 0)
                return new Rectangle(0, 0, Math.Max(0, canvasWidth), Math.Max(0, canvasHeight));

            var scale = Math.Min((double)canvasWidth / videoWidth, (double)canvasHeight / videoHeight);
            var width = (int)Math.Round(videoWidth * scale);
            var height = (int)Math.Round(videoHeight * scale);

            return new Rectangle((canvasWidth - width) / 2, (canvasHeight - height) / 2, width, height);
        }
    }
}
