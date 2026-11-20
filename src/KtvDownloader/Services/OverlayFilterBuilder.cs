using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KtvDownloader.Models;

namespace KtvDownloader.Services
{
    /// <summary>
    /// 一个待写入的文本文件（<c>drawtext</c> 用 <c>textfile=</c> 读它，避免多层转义）。
    /// </summary>
    public sealed class OverlayTextFile
    {
        /// <summary>目标路径。</summary>
        public string Path { get; set; }

        /// <summary>文件内容。<b>必须以 UTF-8 无 BOM 写入，换行必须是 LF</b>，见 <see cref="OverlayFilterBuilder"/>。</summary>
        public string Content { get; set; }
    }

    /// <summary>
    /// 构建滤镜图所需的外部资源。抽成接口是为了让 <see cref="OverlayFilterBuilder"/>
    /// 保持纯函数 —— 不读图、不扫字体目录、不写临时文件，全部由调用方提供，于是可以单元测试。
    /// </summary>
    public interface IOverlayAssetResolver
    {
        /// <summary>取图片的自然像素尺寸。失败返回 false（调用方会跳过该元素）。</summary>
        bool TryGetImageSize(string imagePath, out int width, out int height);

        /// <summary>字体名 → 字体文件路径。失败返回 false。</summary>
        bool TryResolveFont(string family, out string filePath, out string warning);

        /// <summary>为一段文本分配一个临时文件路径（只分配，不写入）。</summary>
        string AllocateTextFilePath();
    }

    /// <summary>
    /// 一次烧录的完整渲染计划。<see cref="OverlayFilterBuilder"/> 的产物，
    /// <c>MediaService</c> 照着它拼命令行、写临时文件、收尾清理。
    /// </summary>
    public sealed class OverlayRenderPlan
    {
        /// <summary>
        /// <c>-filter_complex</c> 的完整值（未加外层双引号）。
        /// <see cref="IsEmpty"/> 为 true 时是 null。
        /// </summary>
        public string FilterComplex { get; set; }

        /// <summary>
        /// 需要按顺序追加到命令行的图片输入（主视频之后）。
        /// 第 i 个对应滤镜图里的输入流 <c>[i+1:v]</c>。
        /// </summary>
        public List<string> ImageInputs { get; private set; }

        /// <summary>需要写入的文本文件。ffmpeg 跑完后由调用方删除。</summary>
        public List<OverlayTextFile> TextFiles { get; private set; }

        /// <summary>非致命提示（字体兜底、.ttc 风险等），应记日志并在界面上告知用户。</summary>
        public List<string> Warnings { get; private set; }

        /// <summary>
        /// 滤镜图最终输出的流标签（形如 <c>v3</c>，不含方括号），调用方用它做 <c>-map</c>。
        /// <para>
        /// 由 <see cref="OverlayFilterBuilder.Build"/> 直接填好。别靠反查 FilterComplex 字符串去猜 ——
        /// 两处各数一遍迟早对不上。
        /// </para>
        /// </summary>
        public string OutputLabel { get; internal set; }

        /// <summary>没有任何可渲染元素 —— 调用方应走普通转码路径，别加 filter_complex。</summary>
        public bool IsEmpty { get; private set; }

        public OverlayRenderPlan()
        {
            ImageInputs = new List<string>();
            TextFiles = new List<OverlayTextFile>();
            Warnings = new List<string>();
            IsEmpty = true;
        }

        internal void MarkNonEmpty()
        {
            IsEmpty = false;
        }
    }

    /// <summary>
    /// 把 <see cref="OverlayComposition"/> 编译成 ffmpeg 的 <c>-filter_complex</c> 图。<b>纯函数</b>。
    /// <para>
    /// <b>为什么所有叠加必须合并进一次 ffmpeg 调用：</b>
    /// 分几趟做等于把视频重编码几遍，时间按倍数涨，画质还要多损失几代。
    /// 所以这里产出的是一张完整的图：图片元素的 <c>scale</c> 支链 + 主干上的
    /// <c>overlay</c>/<c>drawtext</c> 串成一条链，一次跑完。
    /// </para>
    /// <para>
    /// 渲染顺序 = 元素列表顺序，后面的盖在前面的上面。
    /// </para>
    /// <para>
    /// <b>已知的一致性边界</b>：图片元素的坐标是完全确定的（两边都用
    /// <see cref="OverlayGeometry"/> 算出同样的像素值）；文本元素的定位交给 ffmpeg 的
    /// <c>main_w/main_h/w/h</c> 表达式，因为文字渲染后的实际宽高只有 libfreetype 在运行时才知道，
    /// GDI+ 量出来的和它不会完全相等。锚点语义一致，但像素级会有几个字的偏差。
    /// 界面上要用「试烧样片」让用户自己确认，别承诺像素级所见即所得。
    /// </para>
    /// </summary>
    public static class OverlayFilterBuilder
    {
        /// <summary>多行文本的行距（720p 基准像素）。ffmpeg 默认 0，多行会挤在一起。</summary>
        private const int LineSpacingAt720p = 6;

        /// <summary>投影偏移（720p 基准像素）。</summary>
        private const int ShadowOffsetAt720p = 2;

        /// <summary>投影颜色的不透明度。</summary>
        private const double ShadowOpacity = 0.5;

        /// <summary>
        /// 构建渲染计划。
        /// </summary>
        /// <param name="composition">叠加配置。null 或未启用时返回空计划。</param>
        /// <param name="videoWidth">正片视频宽（像素）。</param>
        /// <param name="videoHeight">正片视频高（像素），用于把 720p 基准尺寸换算过来。</param>
        /// <param name="assets">外部资源提供者。</param>
        public static OverlayRenderPlan Build(
            OverlayComposition composition,
            int videoWidth,
            int videoHeight,
            IOverlayAssetResolver assets)
        {
            var plan = new OverlayRenderPlan();

            if (composition == null || !composition.Enabled || assets == null) return plan;

            var elements = composition.GetRenderableElements();
            if (elements.Count == 0) return plan;

            // ── 第一趟：给图片元素分配输入序号，算好尺寸与坐标，生成 scale 支链 ──
            //
            // 输入 0 永远是正片，图片从输入 1 开始按列表顺序排。
            // 支链要先全部声明，后面的主干才能引用它们的标签。
            //
            // ⚠️ 读不到尺寸的元素记进 skipped，**不许改 element.Visible** ——
            // 那是调用方传进来的配置对象，改了就等于这个"纯函数"偷偷污染了全局状态：
            // 用户下次打开编辑器会发现元素莫名其妙被关掉了。
            var branches = new StringBuilder();
            var skipped = new HashSet<OverlayElement>();
            var imageLabels = new Dictionary<OverlayElement, string>();
            var imageRects = new Dictionary<OverlayElement, OverlayRect>();

            foreach (var element in elements)
            {
                if (element.Kind != OverlayElementKind.Image) continue;

                int naturalWidth, naturalHeight;
                if (!assets.TryGetImageSize(element.ImagePath, out naturalWidth, out naturalHeight))
                {
                    plan.Warnings.Add("读不到图片尺寸，已跳过: " + element.ImagePath);
                    skipped.Add(element);
                    continue;
                }

                var ordinal = plan.ImageInputs.Count + 1;
                plan.ImageInputs.Add(element.ImagePath);

                int width, height;
                OverlayGeometry.ResolveImageSize(naturalWidth, naturalHeight,
                    element.RelativeWidth, videoWidth, out width, out height);

                var label = "ov" + ordinal.ToString(CultureInfo.InvariantCulture);

                branches
                    .Append('[').Append(ordinal.ToString(CultureInfo.InvariantCulture)).Append(":v]")
                    .Append("scale=").Append(FfmpegFilterText.Option("w", width))
                    .Append(':').Append(FfmpegFilterText.Option("h", height))
                    .Append(":flags=lanczos");

                // 不透明度 < 1 时才加 colorchannelmixer：aa 是 alpha 通道的乘数。
                // 完全不透明时少一个滤镜节点，省一点编码时间。
                if (element.Opacity < 0.999)
                {
                    branches
                        .Append(",format=rgba,colorchannelmixer=")
                        .Append(FfmpegFilterText.Option("aa", element.Opacity));
                }

                branches.Append('[').Append(label).Append("];");
                imageLabels[element] = label;

                // 坐标是确定的（图片尺寸、视频尺寸都已知），在这里一次算完。
                // 预览端调的是同一个 OverlayGeometry.ResolveImageRect，两边不会漂。
                imageRects[element] = OverlayGeometry.ResolveImageRect(
                    element, naturalWidth, naturalHeight, videoWidth, videoHeight);
            }

            // ── 第二趟：按列表顺序把 overlay / drawtext 串成主干 ──
            var chain = new StringBuilder();
            var current = "0:v";     // 当前视频流标签（第一个节点直接引用主输入）
            var step = 0;

            foreach (var element in elements)
            {
                if (skipped.Contains(element)) continue;

                var next = "v" + step.ToString(CultureInfo.InvariantCulture);
                string node;

                if (element.Kind == OverlayElementKind.Image)
                {
                    string label;
                    OverlayRect rect;
                    if (!imageLabels.TryGetValue(element, out label)) continue;
                    if (!imageRects.TryGetValue(element, out rect)) continue;

                    node = BuildOverlayNode(current, label, next, rect, element);
                }
                else
                {
                    node = BuildDrawtextNode(current, next, element, videoHeight, assets, plan);
                    if (node == null) continue;   // 字体解析失败，警告已记进 plan
                }

                if (chain.Length > 0) chain.Append(';');

                chain.Append(node);
                current = next;
                step++;
            }

            if (step == 0)
            {
                // 所有元素都被跳过了：清空图片输入，让调用方走普通转码路径。
                // 否则会留下一堆 -i 却没有任何滤镜引用它们，ffmpeg 直接报错退出。
                plan.ImageInputs.Clear();
                plan.TextFiles.Clear();
                return plan;
            }

            // 支链在前、主干在后。第一趟里每条支链已自带结尾的 ';'。
            var graph = new StringBuilder();
            graph.Append(branches).Append(chain);

            plan.FilterComplex = graph.ToString();
            plan.OutputLabel = current;
            plan.MarkNonEmpty();
            return plan;
        }

        private static string BuildOverlayNode(
            string inputLabel, string overlayLabel, string outputLabel, OverlayRect rect, OverlayElement element)
        {
            var builder = new StringBuilder();

            builder.Append('[').Append(inputLabel).Append(']');
            builder.Append('[').Append(overlayLabel).Append(']');
            builder.Append("overlay=")
                   .Append(FfmpegFilterText.Option("x", rect.X))
                   .Append(':').Append(FfmpegFilterText.Option("y", rect.Y));

            // eof_action=pass：图片输入只有一帧，overlay 默认 repeatlast=1 会把它一直保持到最后，
            // 这正是我们要的（水印全程在）。显式写 eof_action=pass 是为了防止某些构建的默认值不同。
            builder.Append(":eof_action=pass");

            var enable = OverlayGeometry.BuildEnableExpression(element);
            if (enable != null)
            {
                // ⚠️ 单引号不能省：between(t,0,5) 里的逗号会被 filtergraph 当成滤镜分隔符。
                builder.Append(':').Append(FfmpegFilterText.QuotedOption("enable", enable));
            }

            builder.Append('[').Append(outputLabel).Append(']');
            return builder.ToString();
        }

        private static string BuildDrawtextNode(
            string inputLabel,
            string outputLabel,
            OverlayElement element,
            int videoHeight,
            IOverlayAssetResolver assets,
            OverlayRenderPlan plan)
        {
            string fontFile;
            string warning;
            if (!assets.TryResolveFont(element.FontFamily, out fontFile, out warning))
            {
                plan.Warnings.Add(string.Format("找不到字体「{0}」，已跳过这段文字", element.FontFamily));
                return null;
            }

            if (!string.IsNullOrEmpty(warning) && !plan.Warnings.Contains(warning))
                plan.Warnings.Add(warning);

            // 文本落到临时文件里，drawtext 用 textfile= 读。
            // ⚠️ expansion=none 必须加：否则文本里的 %{...} 会被当成变量展开，
            //    用户写一句 "进度 %{n}" 就会渲染出乱七八糟的数字。
            var textFile = assets.AllocateTextFilePath();
            plan.TextFiles.Add(new OverlayTextFile
            {
                Path = textFile,
                Content = NormalizeNewLines(element.Text)
            });

            var fontSize = OverlayGeometry.ScaleForVideo(element.FontSizePx, videoHeight);

            var builder = new StringBuilder();
            builder.Append('[').Append(inputLabel).Append(']');
            builder.Append("drawtext=");

            var options = new List<string>
            {
                FfmpegFilterText.QuotedOption("fontfile", FfmpegFilterText.ToFilterPath(fontFile)),
                FfmpegFilterText.QuotedOption("textfile", FfmpegFilterText.ToFilterPath(textFile)),
                "expansion=none",
                FfmpegFilterText.Option("fontsize", fontSize),
                FfmpegFilterText.Option("fontcolor", FfmpegFilterText.Color(element.FillColor))
            };

            if (element.BorderWidthPx > 0)
            {
                // ffmpeg 的 borderw 是**只向外**扩。GDI+ 预览那边用
                // pen.Width = 2 * borderw 画路径来对齐这个语义
                // （见 Controls/OverlayRenderer.cs，docs §7.6 的一致性边界表）。
                options.Add(FfmpegFilterText.Option("borderw",
                    OverlayGeometry.ScaleForVideo(element.BorderWidthPx, videoHeight)));
                options.Add(FfmpegFilterText.Option("bordercolor",
                    FfmpegFilterText.Color(element.BorderColor)));
            }

            if (element.Shadow)
            {
                var offset = OverlayGeometry.ScaleForVideo(ShadowOffsetAt720p, videoHeight);
                // 颜色后接 @不透明度，是 ffmpeg 颜色规范的一部分。
                options.Add("shadowcolor=0x000000@" +
                            ShadowOpacity.ToString("0.##", CultureInfo.InvariantCulture));
                options.Add(FfmpegFilterText.Option("shadowx", offset));
                options.Add(FfmpegFilterText.Option("shadowy", offset));
            }

            options.Add(FfmpegFilterText.Option("line_spacing",
                OverlayGeometry.ScaleForVideo(LineSpacingAt720p, videoHeight)));

            // 文本的渲染后宽高只有 freetype 知道，所以定位必须用表达式而不是具体数字。
            options.Add(FfmpegFilterText.Option("x", OverlayGeometry.BuildTextPositionExpression(
                element.Anchor, element.MarginX, videoHeight, true)));
            options.Add(FfmpegFilterText.Option("y", OverlayGeometry.BuildTextPositionExpression(
                element.Anchor, element.MarginY, videoHeight, false)));

            var enable = OverlayGeometry.BuildEnableExpression(element);
            if (enable != null)
                options.Add(FfmpegFilterText.QuotedOption("enable", enable));

            builder.Append(string.Join(":", options.ToArray()));
            builder.Append('[').Append(outputLabel).Append(']');
            return builder.ToString();
        }

        /// <summary>
        /// 换行统一成 LF。
        /// <para>
        /// ⚠️ 不是洁癖：Windows 的多行 TextBox 给的是 <c>\r\n</c>，
        /// 而 drawtext 会把 <c>\r</c> 当成一个普通字符去渲染 ——
        /// 每行末尾多出一个方框（豆腐块）。这个 bug 只有看成品才发现。
        /// </para>
        /// </summary>
        public static string NormalizeNewLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
