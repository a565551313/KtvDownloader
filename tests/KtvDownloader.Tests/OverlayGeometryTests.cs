using System;
using KtvDownloader.Models;
using KtvDownloader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KtvDownloader.Tests
{
    /// <summary>
    /// 叠加层的几何计算。
    /// <para>
    /// 这块的价值在于：<b>预览（GDI+）和成品（ffmpeg）都调这里的同一套函数</b>。
    /// 算错了不会报错，只会让水印出现在错误的位置，而用户要对着成品才发现。
    /// </para>
    /// </summary>
    [TestClass]
    public class OverlayGeometryTests
    {
        private static OverlayElement Image(int anchor, int marginX, int marginY)
        {
            var element = new OverlayElement
            {
                Kind = OverlayElementKind.Image,
                ImagePath = "logo.png",
                Anchor = (OverlayAnchor)anchor,
                MarginX = marginX,
                MarginY = marginY,
                RelativeWidth = 0.1,
                Opacity = 1.0
            };
            element.Normalize();
            return element;
        }

        [TestMethod]
        public void 缩放_按视频高度等比换算()
        {
            // 720p 是基准，原值返回。
            Assert.AreEqual(24, OverlayGeometry.ScaleForVideo(24, 720));

            // 1080p = 1.5 倍。
            Assert.AreEqual(36, OverlayGeometry.ScaleForVideo(24, 1080));

            // 480p = 2/3。
            Assert.AreEqual(16, OverlayGeometry.ScaleForVideo(24, 480));
        }

        [TestMethod]
        public void 缩放_一像素不会被舍入成零()
        {
            // 1080p 上 1px 描边按 1.5 算是 1.5 → 四舍五入 2；
            // 但 480p 上是 0.67，四舍五入会变成 0 —— 描边整个消失。
            Assert.AreEqual(1, OverlayGeometry.ScaleForVideo(1, 480));
            Assert.AreEqual(2, OverlayGeometry.ScaleForVideo(1, 1080));
        }

        [TestMethod]
        public void 缩放_视频高度非法时返回原值不抛异常()
        {
            Assert.AreEqual(24, OverlayGeometry.ScaleForVideo(24, 0));
            Assert.AreEqual(24, OverlayGeometry.ScaleForVideo(24, -100));
        }

        [TestMethod]
        public void 反向缩放_与正向缩放互逆()
        {
            foreach (var height in new[] { 480, 720, 1080, 2160 })
            {
                var scaled = OverlayGeometry.ScaleForVideo(24, height);
                var back = OverlayGeometry.UnscaleFromVideo(scaled, height);

                // 允许 1 像素的舍入误差。
                Assert.IsTrue(Math.Abs(back - 24) <= 1,
                    string.Format("高度 {0} 往返换算偏差过大：{1}", height, back));
            }
        }

        [TestMethod]
        public void 图片尺寸_宽按画面比例高按原图纵横比()
        {
            int width, height;
            OverlayGeometry.ResolveImageSize(200, 100, 0.1, 1280, out width, out height);

            Assert.AreEqual(128, width);     // 1280 × 0.1
            Assert.AreEqual(64, height);     // 纵横比 2:1
        }

        [TestMethod]
        public void 图片尺寸_原图尺寸未知时退化为正方形()
        {
            int width, height;
            OverlayGeometry.ResolveImageSize(0, 0, 0.1, 1280, out width, out height);

            Assert.AreEqual(128, width);
            Assert.AreEqual(128, height);
        }

        [TestMethod]
        public void 定位_右上角()
        {
            // 1280x720，元素 100x50，边距 20（720p 基准，正好不缩放）。
            var rect = OverlayGeometry.ResolveRect(
                OverlayAnchor.TopRight, 20, 20, 1280, 720, 100, 50);

            Assert.AreEqual(1280 - 100 - 20, rect.X);
            Assert.AreEqual(20, rect.Y);
        }

        [TestMethod]
        public void 定位_正中忽略边距()
        {
            var rect = OverlayGeometry.ResolveRect(
                OverlayAnchor.Center, 200, 200, 1280, 720, 100, 50);

            Assert.AreEqual((1280 - 100) / 2, rect.X);
            Assert.AreEqual((720 - 50) / 2, rect.Y);
        }

        [TestMethod]
        public void 定位_九个锚点都落在画面内()
        {
            foreach (OverlayAnchor anchor in Enum.GetValues(typeof(OverlayAnchor)))
            {
                var rect = OverlayGeometry.ResolveRect(anchor, 24, 24, 1280, 720, 100, 50);

                Assert.IsTrue(rect.X >= 0 && rect.Y >= 0,
                    anchor + " 的坐标为负：" + rect);
                Assert.IsTrue(rect.X + rect.Width <= 1280 && rect.Y + rect.Height <= 720,
                    anchor + " 超出画面：" + rect);
            }
        }

        [TestMethod]
        public void 定位_元素比画面大时夹到零而不是负数()
        {
            // 右下角 + 超大元素：不夹的话 x/y 会是负的，
            // ffmpeg 会把元素画到画面外，表现为"水印消失了"。
            var rect = OverlayGeometry.ResolveRect(
                OverlayAnchor.BottomRight, 24, 24, 1280, 720, 2000, 1200);

            Assert.AreEqual(0, rect.X);
            Assert.AreEqual(0, rect.Y);
        }

        [TestMethod]
        public void 定位_边距在1080p上会等比放大()
        {
            var at720 = OverlayGeometry.ResolveRect(OverlayAnchor.TopLeft, 20, 20, 1280, 720, 100, 50);
            var at1080 = OverlayGeometry.ResolveRect(OverlayAnchor.TopLeft, 20, 20, 1920, 1080, 150, 75);

            Assert.AreEqual(20, at720.X);
            Assert.AreEqual(30, at1080.X);   // 20 × 1080/720
        }

        [TestMethod]
        public void 文本定位表达式_靠边用具体边距居中用表达式()
        {
            // 左边：就是边距本身（1080p 上 20 → 30）。
            Assert.AreEqual("30",
                OverlayGeometry.BuildTextPositionExpression(OverlayAnchor.TopLeft, 20, 1080, true));

            // 居中：必须交给 ffmpeg 用 main_w 和文本宽 w 算，
            // 因为文字渲染后的实际宽度只有 libfreetype 知道。
            Assert.AreEqual("(main_w-w)/2",
                OverlayGeometry.BuildTextPositionExpression(OverlayAnchor.TopCenter, 20, 1080, true));
            Assert.AreEqual("(main_h-h)/2",
                OverlayGeometry.BuildTextPositionExpression(OverlayAnchor.Center, 20, 1080, false));

            // 靠右：main_w - 文本宽 - 边距。
            Assert.AreEqual("main_w-w-30",
                OverlayGeometry.BuildTextPositionExpression(OverlayAnchor.TopRight, 20, 1080, true));
        }

        [TestMethod]
        public void 文本定位表达式_边距为零时不输出多余的减零()
        {
            Assert.AreEqual("main_w-w",
                OverlayGeometry.BuildTextPositionExpression(OverlayAnchor.MiddleRight, 0, 720, true));
            Assert.AreEqual("main_h-h",
                OverlayGeometry.BuildTextPositionExpression(OverlayAnchor.BottomCenter, 0, 720, false));
        }

        [TestMethod]
        public void 时间窗口_全程显示时没有enable表达式()
        {
            var element = Image(2, 20, 20);
            element.HasTimeWindow = false;

            Assert.IsNull(OverlayGeometry.BuildEnableExpression(element));
            Assert.IsTrue(OverlayGeometry.IsVisibleAt(element, 0));
            Assert.IsTrue(OverlayGeometry.IsVisibleAt(element, 9999));
        }

        [TestMethod]
        public void 时间窗口_闭区间含首尾()
        {
            var element = Image(2, 20, 20);
            element.HasTimeWindow = true;
            element.StartSeconds = 10;
            element.EndSeconds = 15;
            element.Normalize();

            Assert.AreEqual("between(t,10,15)", OverlayGeometry.BuildEnableExpression(element));

            Assert.IsFalse(OverlayGeometry.IsVisibleAt(element, 9.99));
            Assert.IsTrue(OverlayGeometry.IsVisibleAt(element, 10));    // 含起点
            Assert.IsTrue(OverlayGeometry.IsVisibleAt(element, 12.5));
            Assert.IsTrue(OverlayGeometry.IsVisibleAt(element, 15));    // 含终点
            Assert.IsFalse(OverlayGeometry.IsVisibleAt(element, 15.01));
        }

        [TestMethod]
        public void 时间窗口_小数秒用不变文化格式输出()
        {
            var element = Image(2, 20, 20);
            element.HasTimeWindow = true;
            element.StartSeconds = 0.5;
            element.EndSeconds = 4.25;
            element.Normalize();

            // 区域设置是德语/法语时小数点是逗号，ffmpeg 会解析失败。
            Assert.AreEqual("between(t,0.5,4.25)", OverlayGeometry.BuildEnableExpression(element));
        }

        [TestMethod]
        public void 时间窗口_起止为零长度时至少留半秒()
        {
            var element = Image(2, 20, 20);
            element.HasTimeWindow = true;
            element.StartSeconds = 10;
            element.EndSeconds = 10;
            element.Normalize();

            // 起止相同的窗口一帧都不显示，用户会以为功能坏了。
            Assert.AreEqual(10.5, element.EndSeconds, 0.001);
        }

        [TestMethod]
        public void 时间窗口_起止颠倒会被纠正()
        {
            var element = Image(2, 20, 20);
            element.HasTimeWindow = true;
            element.StartSeconds = 15;
            element.EndSeconds = 10;
            element.Normalize();

            Assert.IsTrue(element.EndSeconds > element.StartSeconds);
        }

        [TestMethod]
        public void 隐藏的元素任何时刻都不可见()
        {
            var element = Image(2, 20, 20);
            element.Visible = false;
            element.HasTimeWindow = false;

            Assert.IsFalse(OverlayGeometry.IsVisibleAt(element, 0));
        }

        [TestMethod]
        public void 拖拽_拖到左上区域会变成左上锚点并记下边距()
        {
            var element = Image((int)OverlayAnchor.TopRight, 24, 24);

            // 拖到 (40, 30)，元素 100x50，视频 1280x720。
            OverlayGeometry.ApplyDrag(element, 1280, 720, 100, 50, 40, 30);

            Assert.AreEqual(OverlayAnchor.TopLeft, element.Anchor);
            Assert.AreEqual(40, element.MarginX);
            Assert.AreEqual(30, element.MarginY);

            // 关键：用反解出来的参数正向再算一次，必须落回拖到的位置。
            var rect = OverlayGeometry.ResolveRect(element.Anchor, element.MarginX, element.MarginY,
                1280, 720, 100, 50);
            Assert.AreEqual(40, rect.X);
            Assert.AreEqual(30, rect.Y);
        }

        [TestMethod]
        public void 拖拽_拖到右下区域会变成右下锚点()
        {
            var element = Image((int)OverlayAnchor.TopLeft, 0, 0);

            OverlayGeometry.ApplyDrag(element, 1280, 720, 100, 50, 1100, 600);

            Assert.AreEqual(OverlayAnchor.BottomRight, element.Anchor);

            var rect = OverlayGeometry.ResolveRect(element.Anchor, element.MarginX, element.MarginY,
                1280, 720, 100, 50);
            Assert.AreEqual(1100, rect.X);
            Assert.AreEqual(600, rect.Y);
        }

        [TestMethod]
        public void 拖拽_拖到中间会吸附成居中锚点()
        {
            var element = Image((int)OverlayAnchor.TopLeft, 0, 0);

            // 元素中心落在画面中心附近。
            OverlayGeometry.ApplyDrag(element, 1280, 720, 100, 50, 590, 335);

            Assert.AreEqual(OverlayAnchor.Center, element.Anchor);
        }

        [TestMethod]
        public void 拖拽_九个区域分别吸附到九个锚点()
        {
            var expected = new[,]
            {
                { OverlayAnchor.TopLeft, OverlayAnchor.TopCenter, OverlayAnchor.TopRight },
                { OverlayAnchor.MiddleLeft, OverlayAnchor.Center, OverlayAnchor.MiddleRight },
                { OverlayAnchor.BottomLeft, OverlayAnchor.BottomCenter, OverlayAnchor.BottomRight }
            };

            // 视频 1200x900，元素 100x50。取每个分区里明确落在中间的目标点。
            var xs = new[] { 10, 550, 1090 };
            var ys = new[] { 10, 425, 840 };

            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    var element = Image((int)OverlayAnchor.Center, 0, 0);
                    OverlayGeometry.ApplyDrag(element, 1200, 900, 100, 50, xs[column], ys[row]);

                    Assert.AreEqual(expected[row, column], element.Anchor,
                        string.Format("目标 ({0},{1}) 吸附到了 {2}", xs[column], ys[row], element.Anchor));
                }
            }
        }

        [TestMethod]
        public void 拖拽_拖出画面会被夹住而不是产生负边距()
        {
            var element = Image((int)OverlayAnchor.TopLeft, 0, 0);

            OverlayGeometry.ApplyDrag(element, 1280, 720, 100, 50, -500, -500);

            Assert.IsTrue(element.MarginX >= 0);
            Assert.IsTrue(element.MarginY >= 0);
        }

        [TestMethod]
        public void 拖拽_在1080p上拖动的边距换算回720p基准()
        {
            var element = Image((int)OverlayAnchor.TopLeft, 0, 0);

            // 1080p 上拖到 x=60 → 存进模型的应该是 720p 基准的 40。
            OverlayGeometry.ApplyDrag(element, 1920, 1080, 150, 75, 60, 60);

            Assert.AreEqual(40, element.MarginX);

            // 换到 480p 片源上，同一份配置应该等比缩到 27 左右。
            var rect = OverlayGeometry.ResolveRect(element.Anchor, element.MarginX, element.MarginY,
                854, 480, 66, 33);
            Assert.AreEqual(27, rect.X);
        }
    }
}
