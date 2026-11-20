using System;
using System.Collections.Generic;
using KtvDownloader.Models;
using KtvDownloader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KtvDownloader.Tests
{
    /// <summary>
    /// 滤镜图构建。
    /// <para>
    /// 这些断言的价值在于：滤镜图写错 ffmpeg <b>不一定报错</b>，
    /// 它可能安静地产出一个水印位置不对、或者文字变成乱码的文件，
    /// 只有看成品才发现。而且这类错误在命令行里肉眼极难审出来。
    /// </para>
    /// </summary>
    [TestClass]
    public class OverlayFilterBuilderTests
    {
        /// <summary>
        /// 假的资源提供者：不读磁盘、不扫字体目录，让构建器保持纯函数可测。
        /// </summary>
        private sealed class StubAssets : IOverlayAssetResolver
        {
            private readonly Dictionary<string, int[]> _imageSizes =
                new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

            public bool FontResolves = true;
            public string FontPath = @"C:\Windows\Fonts\simhei.ttf";
            public string FontWarning;
            public List<string> AllocatedTextFiles = new List<string>();

            public void AddImage(string path, int width, int height)
            {
                _imageSizes[path] = new[] { width, height };
            }

            public bool TryGetImageSize(string imagePath, out int width, out int height)
            {
                int[] size;
                if (imagePath != null && _imageSizes.TryGetValue(imagePath, out size))
                {
                    width = size[0];
                    height = size[1];
                    return true;
                }

                width = 0;
                height = 0;
                return false;
            }

            public bool TryResolveFont(string family, out string filePath, out string warning)
            {
                warning = FontWarning;
                filePath = FontResolves ? FontPath : null;
                return FontResolves;
            }

            public string AllocateTextFilePath()
            {
                var path = @"C:\temp\overlay-text-" + AllocatedTextFiles.Count + ".txt";
                AllocatedTextFiles.Add(path);
                return path;
            }
        }

        private static OverlayComposition Composition(params OverlayElement[] elements)
        {
            var composition = new OverlayComposition { Enabled = true };
            foreach (var element in elements) composition.Elements.Add(element);
            return composition;
        }

        private static OverlayElement Logo()
        {
            var element = new OverlayElement
            {
                Kind = OverlayElementKind.Image,
                ImagePath = @"C:\img\logo.png",
                Anchor = OverlayAnchor.TopRight,
                MarginX = 20,
                MarginY = 20,
                RelativeWidth = 0.1,
                Opacity = 1.0
            };
            element.Normalize();
            return element;
        }

        private static OverlayElement Card()
        {
            var element = new OverlayElement
            {
                Kind = OverlayElementKind.Text,
                Text = "欢迎光临",
                FontFamily = "黑体",
                FontSizePx = 48,
                FillColor = "#1E90FF",
                BorderColor = "#FFFFFF",
                BorderWidthPx = 3,
                Anchor = OverlayAnchor.TopCenter,
                MarginY = 20,
                HasTimeWindow = true,
                StartSeconds = 0,
                EndSeconds = 5
            };
            element.Normalize();
            return element;
        }

        [TestMethod]
        public void 未启用时返回空计划()
        {
            var composition = Composition(Logo());
            composition.Enabled = false;

            var assets = new StubAssets();
            assets.AddImage(@"C:\img\logo.png", 200, 100);

            var plan = OverlayFilterBuilder.Build(composition, 1280, 720, assets);

            Assert.IsTrue(plan.IsEmpty);
            Assert.IsNull(plan.FilterComplex);
        }

        [TestMethod]
        public void 没有元素时返回空计划()
        {
            var plan = OverlayFilterBuilder.Build(new OverlayComposition { Enabled = true }, 1280, 720, new StubAssets());

            Assert.IsTrue(plan.IsEmpty);
        }

        [TestMethod]
        public void 图片元素_先缩放再叠加并带上输入序号()
        {
            var assets = new StubAssets();
            assets.AddImage(@"C:\img\logo.png", 200, 100);

            var plan = OverlayFilterBuilder.Build(Composition(Logo()), 1280, 720, assets);

            Assert.IsFalse(plan.IsEmpty);

            // 图片作为第 1 号输入（0 号是正片），按 200x100 的纵横比缩到 128x64。
            Assert.AreEqual(1, plan.ImageInputs.Count);
            Assert.AreEqual(@"C:\img\logo.png", plan.ImageInputs[0]);

            Assert.IsTrue(plan.FilterComplex.Contains("[1:v]scale=w=128:h=64:flags=lanczos"),
                "缩放支链不对：" + plan.FilterComplex);

            // 右上角 + 边距 20：x = 1280 - 128 - 20 = 1132, y = 20。
            Assert.IsTrue(plan.FilterComplex.Contains("overlay=x=1132:y=20"),
                "叠加坐标不对：" + plan.FilterComplex);

            Assert.AreEqual("v0", plan.OutputLabel);
        }

        [TestMethod]
        public void 图片元素_完全不透明时不加colorchannelmixer()
        {
            var assets = new StubAssets();
            assets.AddImage(@"C:\img\logo.png", 200, 100);

            var plan = OverlayFilterBuilder.Build(Composition(Logo()), 1280, 720, assets);

            // 少一个滤镜节点，省一点编码时间。
            Assert.IsFalse(plan.FilterComplex.Contains("colorchannelmixer"));
        }

        [TestMethod]
        public void 图片元素_半透明时用colorchannelmixer调alpha()
        {
            var element = Logo();
            element.Opacity = 0.5;

            var assets = new StubAssets();
            assets.AddImage(@"C:\img\logo.png", 200, 100);

            var plan = OverlayFilterBuilder.Build(Composition(element), 1280, 720, assets);

            Assert.IsTrue(plan.FilterComplex.Contains("format=rgba,colorchannelmixer=aa=0.5"),
                "不透明度不对：" + plan.FilterComplex);
        }

        [TestMethod]
        public void 时间窗口_enable表达式必须用单引号包住()
        {
            var element = Logo();
            element.HasTimeWindow = true;
            element.StartSeconds = 10;
            element.EndSeconds = 15;
            element.Normalize();

            var assets = new StubAssets();
            assets.AddImage(@"C:\img\logo.png", 200, 100);

            var plan = OverlayFilterBuilder.Build(Composition(element), 1280, 720, assets);

            // ⚠️ 单引号不能省：between(t,10,15) 里的逗号会被 filtergraph 当成滤镜分隔符，
            // 整条命令会解析失败。
            Assert.IsTrue(plan.FilterComplex.Contains("enable='between(t,10,15)'"),
                "enable 表达式不对：" + plan.FilterComplex);
        }

        [TestMethod]
        public void 全程显示时不输出enable()
        {
            var assets = new StubAssets();
            assets.AddImage(@"C:\img\logo.png", 200, 100);

            var plan = OverlayFilterBuilder.Build(Composition(Logo()), 1280, 720, assets);

            Assert.IsFalse(plan.FilterComplex.Contains("enable="),
                "全程显示不该有 enable：" + plan.FilterComplex);
        }

        [TestMethod]
        public void 文本元素_走textfile且关闭变量展开()
        {
            var plan = OverlayFilterBuilder.Build(Composition(Card()), 1280, 720, new StubAssets());

            Assert.IsFalse(plan.IsEmpty);

            // 文本不进命令行，落到临时文件里由 ffmpeg 读 —— 少一层转义就少一类 bug。
            Assert.AreEqual(1, plan.TextFiles.Count);
            Assert.AreEqual("欢迎光临", plan.TextFiles[0].Content);
            Assert.IsTrue(plan.FilterComplex.Contains("textfile='C:/temp/overlay-text-0.txt'"),
                "textfile 不对：" + plan.FilterComplex);

            // 没有 expansion=none 的话，用户写一句"进度 %{n}"会被渲染成乱七八糟的数字。
            Assert.IsTrue(plan.FilterComplex.Contains("expansion=none"),
                "缺少 expansion=none：" + plan.FilterComplex);
        }

        [TestMethod]
        public void 文本元素_字体路径转正斜杠且颜色转0x格式()
        {
            var plan = OverlayFilterBuilder.Build(Composition(Card()), 1280, 720, new StubAssets());

            // 反斜杠在滤镜语法里是转义符，必须换成正斜杠（ffmpeg 在 Windows 上完全接受）。
            Assert.IsTrue(plan.FilterComplex.Contains("fontfile='C:/Windows/Fonts/simhei.ttf'"),
                "fontfile 不对：" + plan.FilterComplex);

            Assert.IsTrue(plan.FilterComplex.Contains("fontcolor=0x1E90FF"),
                "填充色不对：" + plan.FilterComplex);
            Assert.IsTrue(plan.FilterComplex.Contains("bordercolor=0xFFFFFF"),
                "描边色不对：" + plan.FilterComplex);
        }

        [TestMethod]
        public void 文本元素_字号按视频高度缩放()
        {
            // 48 是 720p 基准，1080p 上要放大到 72。
            var plan = OverlayFilterBuilder.Build(Composition(Card()), 1920, 1080, new StubAssets());

            Assert.IsTrue(plan.FilterComplex.Contains("fontsize=72"),
                "字号没按分辨率缩放：" + plan.FilterComplex);

            // 描边宽同样缩放：3 → 4.5 → 5（四舍五入，远离零）。
            Assert.IsTrue(plan.FilterComplex.Contains("borderw=5"),
                "描边宽没缩放：" + plan.FilterComplex);
        }

        [TestMethod]
        public void 文本元素_居中锚点用表达式定位()
        {
            var plan = OverlayFilterBuilder.Build(Composition(Card()), 1280, 720, new StubAssets());

            // 文字渲染后的宽高只有 libfreetype 知道，必须交给 ffmpeg 用 main_w/w 算。
            Assert.IsTrue(plan.FilterComplex.Contains("x=(main_w-w)/2"),
                "水平居中表达式不对：" + plan.FilterComplex);
        }

        [TestMethod]
        public void 文本元素_描边为零时不输出borderw()
        {
            var element = Card();
            element.BorderWidthPx = 0;

            var plan = OverlayFilterBuilder.Build(Composition(element), 1280, 720, new StubAssets());

            Assert.IsFalse(plan.FilterComplex.Contains("borderw="),
                "描边宽为 0 时不该输出 borderw：" + plan.FilterComplex);
        }

        [TestMethod]
        public void 字体解析失败_跳过该元素并记下警告()
        {
            var assets = new StubAssets { FontResolves = false };

            var plan = OverlayFilterBuilder.Build(Composition(Card()), 1280, 720, assets);

            // 只有这一个元素，跳过后就没有任何东西可烧了。
            Assert.IsTrue(plan.IsEmpty);
            Assert.IsTrue(plan.Warnings.Count > 0);
            Assert.IsTrue(plan.Warnings[0].Contains("找不到字体"),
                "警告内容不对：" + plan.Warnings[0]);

            // ⚠️ 不能留下没人引用的输入或临时文件，否则 ffmpeg 直接报错。
            Assert.AreEqual(0, plan.TextFiles.Count);
        }

        [TestMethod]
        public void 字体的非致命警告会带出来()
        {
            var assets = new StubAssets { FontWarning = "msyh.ttc 是 TrueType Collection，字形可能不对" };

            var plan = OverlayFilterBuilder.Build(Composition(Card()), 1280, 720, assets);

            Assert.IsFalse(plan.IsEmpty);
            Assert.IsTrue(plan.Warnings.Contains(assets.FontWarning));
        }

        [TestMethod]
        public void 图片尺寸读不到_跳过该元素()
        {
            var assets = new StubAssets();   // 没登记 logo.png

            var plan = OverlayFilterBuilder.Build(Composition(Logo()), 1280, 720, assets);

            Assert.IsTrue(plan.IsEmpty);
            Assert.AreEqual(0, plan.ImageInputs.Count, "跳过的元素不能留下没人引用的 -i 输入");
            Assert.IsTrue(plan.Warnings[0].Contains("读不到图片尺寸"));
        }

        [TestMethod]
        public void 构建器不会修改传入的配置()
        {
            // 读不到尺寸的元素只能记在内部的 skipped 集合里。
            // 改 element.Visible 就等于偷偷污染了调用方的配置对象：
            // 用户下次打开编辑器会发现元素莫名其妙被关掉了。
            var element = Logo();
            var assets = new StubAssets();   // 故意不登记图片

            OverlayFilterBuilder.Build(Composition(element), 1280, 720, assets);

            Assert.IsTrue(element.Visible, "构建器改动了传入元素的 Visible");
        }

        [TestMethod]
        public void 多元素_支链在前主干在后且标签连续()
        {
            var assets = new StubAssets();
            assets.AddImage(@"C:\img\logo.png", 200, 100);

            // 顺序：文字卡片 → 图片水印。列表顺序就是渲染顺序，后面的盖在前面上面。
            var plan = OverlayFilterBuilder.Build(Composition(Card(), Logo()), 1280, 720, assets);

            var graph = plan.FilterComplex;

            var scaleIndex = graph.IndexOf("[1:v]scale", StringComparison.Ordinal);
            var drawtextIndex = graph.IndexOf("drawtext", StringComparison.Ordinal);
            var overlayIndex = graph.IndexOf("overlay=", StringComparison.Ordinal);

            Assert.IsTrue(scaleIndex >= 0 && drawtextIndex > 0 && overlayIndex > drawtextIndex,
                "滤镜图顺序不对（支链应在前，主干按列表顺序串联）：" + graph);

            // 主干：[0:v]drawtext→[v0]，然后 [v0][ov1]overlay→[v1]。
            Assert.IsTrue(graph.Contains("[v0][ov1]overlay"),
                "第二个节点没有接上第一个的输出：" + graph);

            Assert.AreEqual("v1", plan.OutputLabel);
        }

        [TestMethod]
        public void 隐藏的元素和空文本都不参与构建()
        {
            var hidden = Logo();
            hidden.Visible = false;

            var emptyText = Card();
            emptyText.Text = "   ";
            emptyText.Normalize();

            var assets = new StubAssets();
            assets.AddImage(@"C:\img\logo.png", 200, 100);

            var plan = OverlayFilterBuilder.Build(Composition(hidden, emptyText), 1280, 720, assets);

            Assert.IsTrue(plan.IsEmpty);
            Assert.AreEqual(0, plan.ImageInputs.Count);
        }

        [TestMethod]
        public void 换行统一成LF()
        {
            // ⚠️ Windows 的多行 TextBox 给的是 \r\n，而 drawtext 会把 \r 当成一个
            // 普通字符渲染出来 —— 每行末尾多一个方框（豆腐块）。
            Assert.AreEqual("a\nb", OverlayFilterBuilder.NormalizeNewLines("a\r\nb"));
            Assert.AreEqual("a\nb", OverlayFilterBuilder.NormalizeNewLines("a\rb"));
            Assert.AreEqual("a\nb\nc", OverlayFilterBuilder.NormalizeNewLines("a\r\nb\rc"));
            Assert.AreEqual(string.Empty, OverlayFilterBuilder.NormalizeNewLines(null));
        }

        [TestMethod]
        public void 文本文件内容里的换行已经是LF()
        {
            var element = Card();
            element.Text = "第一行\r\n第二行";

            var plan = OverlayFilterBuilder.Build(Composition(element), 1280, 720, new StubAssets());

            Assert.AreEqual("第一行\n第二行", plan.TextFiles[0].Content);
        }
    }
}
