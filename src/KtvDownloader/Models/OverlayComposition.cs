using System;
using System.Collections.Generic;

namespace KtvDownloader.Models
{
    /// <summary>
    /// 一套叠加配置：全局开关 + 元素列表 + 烧录用的编码参数。
    /// <para>
    /// 持久化在 <c>%AppData%\KtvDownloader\overlay.jsonl</c>（一行一个元素，
    /// 见 <c>OverlayStore</c>），<b>不放在 config.json 里</b> ——
    /// <see cref="Infrastructure.Config.MiniJson"/> 只解析扁平键值对，
    /// 硬塞嵌套数组要么改它（到处在用、风险大），要么把 JSON 转义成一个字符串存进去（没法看也没法手改）。
    /// 单独一个文件还能让用户直接拿记事本改。
    /// </para>
    /// </summary>
    public sealed class OverlayComposition
    {
        /// <summary>总开关。关掉后预览和烧录都完全不动，等于这个功能不存在。</summary>
        public bool Enabled { get; set; }

        /// <summary>预览时是否显示叠加层（可拖动调整）。</summary>
        public bool ShowInPreview { get; set; } = true;

        /// <summary>下载时是否真的烧进视频文件。</summary>
        public bool BurnOnDownload { get; set; } = true;

        /// <summary>
        /// 视频编码器。默认 <c>libx264</c>（纯 CPU，所有机器都能跑）。
        /// <para>
        /// 有独显可以填 <c>h264_nvenc</c>（NVIDIA）/ <c>h264_qsv</c>（Intel 核显）/
        /// <c>h264_amf</c>（AMD），快 5–10 倍。填了不存在的编码器 ffmpeg 会直接失败，
        /// 所以 <c>MediaService</c> 会先探测一次再决定用不用。
        /// </para>
        /// </summary>
        public string VideoEncoder { get; set; } = "libx264";

        /// <summary>libx264 的速度档。越慢画质越好、耗时越长。硬编时忽略此项。</summary>
        public string VideoPreset { get; set; } = "veryfast";

        /// <summary>
        /// 质量因子（0–51，越小越好）。默认 20 在 KTV 播放场景下肉眼无损。
        /// <para>硬编时这个值当作 <c>-cq</c>/<c>-qp</c> 用，同等数值画质略差，可以调到 18。</para>
        /// </summary>
        public int VideoCrf { get; set; } = 20;

        /// <summary>叠加元素列表，按顺序渲染（后面的盖在前面的上面）。</summary>
        public List<OverlayElement> Elements { get; private set; }

        public OverlayComposition()
        {
            Elements = new List<OverlayElement>();
        }

        /// <summary>深拷贝（含所有元素）。编辑器"取消"时靠它还原。</summary>
        public OverlayComposition Clone()
        {
            var copy = new OverlayComposition
            {
                Enabled = Enabled,
                ShowInPreview = ShowInPreview,
                BurnOnDownload = BurnOnDownload,
                VideoEncoder = VideoEncoder,
                VideoPreset = VideoPreset,
                VideoCrf = VideoCrf
            };

            foreach (var element in Elements)
            {
                if (element != null) copy.Elements.Add(element.Clone());
            }

            return copy;
        }

        /// <summary>
        /// 用另一份配置<b>原地</b>替换自己（全局设置 + 元素列表）。
        /// <para>
        /// 为什么不直接换个引用：主窗体、预览窗体、下载选项都可能拿着同一个实例，
        /// 编辑器保存后如果只是让某一方指向新对象，其他几方还在用旧的 ——
        /// 表现为"设置里明明改了，预览没变"，而且每次重现的窗口不一样，极难查。
        /// 原地替换保证所有持有者同时看到新配置。
        /// </para>
        /// </summary>
        public void ReplaceWith(OverlayComposition other)
        {
            if (other == null) return;

            Enabled = other.Enabled;
            ShowInPreview = other.ShowInPreview;
            BurnOnDownload = other.BurnOnDownload;
            VideoEncoder = other.VideoEncoder;
            VideoPreset = other.VideoPreset;
            VideoCrf = other.VideoCrf;

            Elements.Clear();
            foreach (var element in other.Elements)
            {
                if (element != null) Elements.Add(element.Clone());
            }

            Normalize();
        }

        /// <summary>修正非法值。</summary>
        public void Normalize()
        {
            if (Elements == null) Elements = new List<OverlayElement>();

            // 逐个规范化，并丢掉 null 项（手改文件时容易留下空行/半截对象）。
            for (var i = Elements.Count - 1; i >= 0; i--)
            {
                var element = Elements[i];
                if (element == null)
                {
                    Elements.RemoveAt(i);
                    continue;
                }

                element.Normalize();
            }

            // 元素太多会让 filter_complex 长得离谱，也拖慢编码。20 个足够任何实际需求。
            if (Elements.Count > 20) Elements.RemoveRange(20, Elements.Count - 20);

            VideoEncoder = string.IsNullOrWhiteSpace(VideoEncoder) ? "libx264" : VideoEncoder.Trim();
            VideoPreset = NormalizePreset(VideoPreset);
            VideoCrf = VideoCrf < 0 ? 20 : (VideoCrf > 51 ? 20 : VideoCrf);
        }

        /// <summary>
        /// 当前<b>配置上</b>该渲染的元素（已过滤关闭的、没填图片路径的、文本为空的）。
        /// <para>
        /// ⚠️ 这里<b>不检查图片文件在不在</b>（理由见 <see cref="OverlayElement.IsRenderable"/>）。
        /// 文件真读不到时，预览端和烧录端各自会跳过它并给出提示。
        /// </para>
        /// </summary>
        public List<OverlayElement> GetRenderableElements()
        {
            var result = new List<OverlayElement>();
            if (Elements == null) return result;

            foreach (var element in Elements)
            {
                if (element != null && element.IsRenderable()) result.Add(element);
            }

            return result;
        }

        /// <summary>这套配置现在会不会产生任何可见效果。</summary>
        public bool HasAnyRenderable()
        {
            return Enabled && GetRenderableElements().Count > 0;
        }

        /// <summary>
        /// libx264 的 preset 白名单。
        /// <para>
        /// 写错一个字母 ffmpeg 直接报错退出，而用户看到的只是"下载失败"，
        /// 所以宁可回落到 veryfast 也不要把非法值传下去。
        /// </para>
        /// </summary>
        private static string NormalizePreset(string value)
        {
            var text = (value ?? string.Empty).Trim().ToLowerInvariant();

            switch (text)
            {
                case "ultrafast":
                case "superfast":
                case "veryfast":
                case "faster":
                case "fast":
                case "medium":
                case "slow":
                case "slower":
                case "veryslow":
                    return text;
                default:
                    return "veryfast";
            }
        }
    }
}
