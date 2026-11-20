using System;
using System.Globalization;
using System.Text;
using KtvDownloader.Models;

namespace KtvDownloader.Services
{
    /// <summary>
    /// ffmpeg 命令行参数拼装。**纯函数**，不启动进程、不碰文件系统，因此可单元测试。
    /// <para>
    /// 为什么单独抽出来：音轨映射与 pan 滤镜是这个项目最容易写错的地方
    /// （历史上就写错过：单音轨的 pan 滤镜把同一个输出声道赋值两次，
    /// 导致产物右声道恒为静音），而错误只有听成品才能发现。
    /// 抽成纯函数后可以用断言把参数钉死。
    /// </para>
    /// </summary>
    public static class FfmpegArguments
    {
        /// <summary>音频码率下限（kbps）。</summary>
        public const int MinAudioBitrateKbps = 64;

        /// <summary>音频码率上限（kbps）。</summary>
        public const int MaxAudioBitrateKbps = 320;

        /// <summary>把码率夹到合法区间。</summary>
        public static int ClampBitrate(int kbps)
        {
            return Math.Max(MinAudioBitrateKbps, Math.Min(MaxAudioBitrateKbps, kbps));
        }

        /// <summary>
        /// 按 Windows 命令行解析规则引用一个参数。
        /// <para>
        /// 规则（MSVCRT）：结尾的 N 个反斜杠要写成 2N 个；
        /// 引号前的 N 个反斜杠要写成 2N+1 个再跟 <c>\"</c>。
        /// 简单地把 <c>"</c> 换成 <c>\"</c> 在路径以反斜杠结尾时会解析错位。
        /// </para>
        /// </summary>
        public static string Quote(string value)
        {
            if (value == null) return "\"\"";

            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');

            var i = 0;
            while (i < value.Length)
            {
                var backslashes = 0;
                while (i < value.Length && value[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i >= value.Length)
                {
                    // 结尾的反斜杠：翻倍，否则会把闭合引号吃掉。
                    builder.Append('\\', backslashes * 2);
                    break;
                }

                if (value[i] == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                }
                else
                {
                    builder.Append('\\', backslashes);
                    builder.Append(value[i]);
                }

                i++;
            }

            builder.Append('"');
            return builder.ToString();
        }

        /// <summary>
        /// 生成"探测媒体信息"的参数（ffmpeg 会把流信息写到 stderr）。
        /// </summary>
        public static string BuildProbeArguments(string source)
        {
            return "-hide_banner -i " + Quote(source);
        }

        /// <summary>
        /// 老式卡拉OK 单音轨的左右声道拆分滤镜。
        /// <para>
        /// 声道规则（来自上游 VocalSwitchHelper，见 docs §7.5）：
        /// <list type="bullet">
        /// <item><c>accomp == 2</c>：左声道 = 伴奏，右声道 = 原唱</item>
        /// <item>其它（含 <c>accomp == 1</c>）：左声道 = 原唱，右声道 = 伴奏</item>
        /// </list>
        /// </para>
        /// <para>
        /// pan 滤镜的语义是 <c>输出声道=输入声道</c>，**每个输出声道都要显式赋值**，
        /// 漏掉的那个 gain 为 0（静音）。所以要把选中的输入声道复制到 c0 与 c1 两路，
        /// 产出单声道内容的立体声文件。
        /// </para>
        /// </summary>
        /// <param name="accomp">曲库 songs.accomp 字段。</param>
        /// <param name="wantOriginal">true 要原唱，false 要伴奏。</param>
        public static string BuildPanFilter(int accomp, bool wantOriginal)
        {
            // accomp==2 → 原唱在右声道(c1)；否则原唱在左声道(c0)。
            // 规则本身在 VocalChannelRule 里，预览播放（libVLC 切声道）也用它，
            // 两边共用一份，不会出现"预览里是原唱、下载下来变伴奏"。
            var sourceChannel = VocalChannelRule.ResolveSourceChannel(accomp, wantOriginal);

            return "pan=stereo|c0=" + sourceChannel + "|c1=" + sourceChannel;
        }

        /// <summary>
        /// TS → MP4 的转码参数。
        /// <para>
        /// 视频流拷贝（零画质损失），音频**必须**重编码为 AAC：
        /// TS 里的音轨是 mp2，直接 <c>-c copy</c> 塞进 MP4 会被标成 mp3，
        /// 多数播放器解不出来 —— 表现为"画面正常但完全没声音"（docs §坑1）。
        /// </para>
        /// </summary>
        /// <param name="audioTrackCount">源文件的音轨数，决定能不能映射第二条轨。</param>
        public static string BuildConvertToMp4Arguments(
            string source,
            string output,
            VocalOption vocal,
            int audioTrackCount,
            int audioBitrateKbps)
        {
            var builder = new StringBuilder();
            builder.Append("-y -hide_banner -i ").Append(Quote(source));
            builder.Append(" -map 0:v:0");

            if (audioTrackCount >= 2)
            {
                switch (vocal)
                {
                    case VocalOption.OriginalOnly:
                        builder.Append(" -map 0:a:0");
                        break;
                    case VocalOption.AccompanimentOnly:
                        builder.Append(" -map 0:a:1");
                        break;
                    default:
                        // KeepBoth / SplitBoth：两条都留着，播放器可切换。
                        builder.Append(" -map 0:a:0 -map 0:a:1");
                        break;
                }
            }
            else
            {
                // 0 条音轨时不映射音频，否则 ffmpeg 会因 0:a:0 不存在而整体失败。
                if (audioTrackCount > 0) builder.Append(" -map 0:a:0");
                else builder.Append(" -an");
            }

            builder.Append(" -c:v copy");
            if (audioTrackCount > 0)
            {
                builder.Append(" -c:a aac -b:a ")
                       .Append(ClampBitrate(audioBitrateKbps).ToString(CultureInfo.InvariantCulture))
                       .Append("k -ac 2");
            }
            builder.Append(" -movflags +faststart");
            builder.Append(' ').Append(Quote(output));

            return builder.ToString();
        }

        /// <summary>按音轨索引提取一条音轨（双音轨 TS 的原唱/伴奏分离）。</summary>
        public static string BuildExtractTrackArguments(
            string source,
            int trackIndex,
            string output,
            int audioBitrateKbps)
        {
            return "-y -hide_banner -i " + Quote(source)
                   + " -map 0:v:0 -map 0:a:" + trackIndex.ToString(CultureInfo.InvariantCulture)
                   + " -c:v copy -c:a aac -b:a "
                   + ClampBitrate(audioBitrateKbps).ToString(CultureInfo.InvariantCulture)
                   + "k -ac 2 -movflags +faststart " + Quote(output);
        }

        /// <summary>用 pan 滤镜拆左右声道（老式单音轨卡拉OK）。</summary>
        public static string BuildExtractChannelArguments(
            string source,
            string panFilter,
            string output,
            int audioBitrateKbps)
        {
            if (string.IsNullOrWhiteSpace(panFilter))
                throw new ArgumentException("pan 滤镜为空", nameof(panFilter));

            return "-y -hide_banner -i " + Quote(source)
                   + " -map 0:v:0 -map 0:a:0"
                   + " -af " + Quote(panFilter)
                   + " -c:v copy -c:a aac -b:a "
                   + ClampBitrate(audioBitrateKbps).ToString(CultureInfo.InvariantCulture)
                   + "k -movflags +faststart " + Quote(output);
        }

        /// <summary>
        /// 预览转码参数：裁前 N 秒、视频流拷贝、音频转 AAC、fMP4 分片（可边下边播）。
        /// </summary>
        /// <param name="durationSeconds">0 表示不裁剪（完整转码）。</param>
        public static string BuildPreviewArguments(
            string source,
            string output,
            int durationSeconds,
            VocalOption vocal,
            int audioTrackCount)
        {
            var builder = new StringBuilder();
            builder.Append("-y -hide_banner -i ").Append(Quote(source));

            if (durationSeconds > 0)
            {
                builder.Append(" -t ")
                       .Append(durationSeconds.ToString(CultureInfo.InvariantCulture));
            }

            builder.Append(" -map 0:v:0");

            if (vocal == VocalOption.AccompanimentOnly && audioTrackCount >= 2)
                builder.Append(" -map 0:a:1?");
            else if (audioTrackCount > 0)
                builder.Append(" -map 0:a:0");
            else
                builder.Append(" -an");

            if (audioTrackCount > 0) builder.Append(" -c:v copy -c:a aac -b:a 128k -ac 2");
            else builder.Append(" -c:v copy");

            // moov 前置 + 分片，播放器可以先播已下载部分。
            builder.Append(" -movflags +faststart+frag_keyframe+empty_moov");
            builder.Append(' ').Append(Quote(output));

            return builder.ToString();
        }
    }
}
