using System;
using System.Collections.Generic;
using KtvDownloader.Models;

namespace KtvDownloader.Services
{
    /// <summary>
    /// 一个命名的文字样式预设（KTV 里常见的那几种配色）。
    /// </summary>
    public sealed class OverlayTextPreset
    {
        /// <summary>界面下拉框里显示的名字。</summary>
        public string Name { get; private set; }

        /// <summary>字体名。<b>要能在 C:\Windows\Fonts 下找到</b>，见 <see cref="FontFileResolver"/>。</summary>
        public string FontFamily { get; private set; }

        public int FontSizePx { get; private set; }
        public string FillColor { get; private set; }
        public string BorderColor { get; private set; }
        public int BorderWidthPx { get; private set; }
        public bool Shadow { get; private set; }

        public OverlayTextPreset(string name, string fontFamily, int fontSizePx,
            string fillColor, string borderColor, int borderWidthPx, bool shadow)
        {
            Name = name;
            FontFamily = fontFamily;
            FontSizePx = fontSizePx;
            FillColor = fillColor;
            BorderColor = borderColor;
            BorderWidthPx = borderWidthPx;
            Shadow = shadow;
        }

        /// <summary>把样式套到一个文本元素上（不动位置、时间窗口和文字内容）。</summary>
        public void ApplyTo(OverlayElement element)
        {
            if (element == null) return;

            element.Kind = OverlayElementKind.Text;
            element.FontFamily = FontFamily;
            element.FontSizePx = FontSizePx;
            element.FillColor = FillColor;
            element.BorderColor = BorderColor;
            element.BorderWidthPx = BorderWidthPx;
            element.Shadow = Shadow;
            element.Normalize();
        }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// 内置的文字样式预设。
    /// <para>
    /// <b>字体一律选单文件字体（.ttf）。</b>「微软雅黑」是 <c>msyh.ttc</c>
    /// （一个文件里装好几个字面），ffmpeg 的 drawtext 经 libfreetype 打开 ttc 时
    /// 取到哪个字面并不稳定，实测会出现粗细和预览不一致的情况。
    /// 黑体（<c>simhei.ttf</c>）本身笔画就粗，是 KTV 字幕最稳妥的选择。
    /// </para>
    /// </summary>
    public static class OverlayTextPresets
    {
        /// <summary>全部预设，界面下拉框直接绑这个列表。</summary>
        public static IReadOnlyList<OverlayTextPreset> All
        {
            get
            {
                return new List<OverlayTextPreset>
                {
                    // KTV 最经典的一种：蓝字白描边。
                    new OverlayTextPreset("KTV 蓝字白描边", "黑体", 56, "#1E90FF", "#FFFFFF", 4, true),

                    new OverlayTextPreset("白字黑描边", "黑体", 52, "#FFFFFF", "#000000", 3, true),

                    // 开场/欢迎常用，暖色在深色背景上跳得出来。
                    new OverlayTextPreset("金字深棕描边", "黑体", 56, "#FFD700", "#5A3A00", 4, true),

                    new OverlayTextPreset("红字白描边", "黑体", 56, "#E02020", "#FFFFFF", 4, true),

                    // 台标/公告类小字，不抢画面。
                    new OverlayTextPreset("小号白字（公告）", "黑体", 28, "#FFFFFF", "#000000", 2, false),

                    new OverlayTextPreset("青字白描边", "黑体", 48, "#00CED1", "#FFFFFF", 3, true)
                };
            }
        }

        /// <summary>默认预设（新建文本元素时用）。</summary>
        public static OverlayTextPreset Default
        {
            get { return All[0]; }
        }

        /// <summary>按名字找预设，找不到返回 null。</summary>
        public static OverlayTextPreset Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            foreach (var preset in All)
            {
                if (string.Equals(preset.Name, name, StringComparison.Ordinal)) return preset;
            }

            return null;
        }

        /// <summary>用默认预设建一个新的文本元素。</summary>
        public static OverlayElement CreateTextElement(string text)
        {
            var element = new OverlayElement
            {
                Kind = OverlayElementKind.Text,
                Text = text ?? string.Empty,
                Anchor = OverlayAnchor.TopCenter,
                MarginX = 24,
                MarginY = 24,
                HasTimeWindow = true,
                StartSeconds = 0,
                EndSeconds = 5
            };

            Default.ApplyTo(element);
            return element;
        }

        /// <summary>建一个新的图片元素。</summary>
        public static OverlayElement CreateImageElement(string imagePath)
        {
            var element = new OverlayElement
            {
                Kind = OverlayElementKind.Image,
                ImagePath = imagePath ?? string.Empty,
                Anchor = OverlayAnchor.TopRight,
                MarginX = 24,
                MarginY = 24,
                RelativeWidth = 0.15,
                Opacity = 1.0
            };

            element.Normalize();
            return element;
        }
    }
}
