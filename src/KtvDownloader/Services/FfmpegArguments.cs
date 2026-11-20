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

        /// <summary>视频质量因子下限。</summary>
        public const int MinVideoCrf = 0;

        /// <summary>视频质量因子上限（再大就是纯粹的垃圾画质了）。</summary>
        public const int MaxVideoCrf = 51;

        /// <summary>把质量因子夹到合法区间。</summary>
        public static int ClampVideoCrf(int crf)
        {
            return Math.Max(MinVideoCrf, Math.Min(MaxVideoCrf, crf));
        }

        /// <summary>
        /// 视频编码器参数。
        /// <para>
        /// 软编（libx264）用 <c>-preset</c> + <c>-crf</c>；硬编各家吃的不一样，传错直接失败退出：
        /// nvenc 认 <c>-cq</c>、qsv 认 <c>-global_quality</c>，两者都认 <c>-preset</c>。
        /// 没列进来的编码器就只给 <c>-c:v</c>，用它自己的默认参数 —— 画质不可控，但至少能跑通。
        /// </para>
        /// </summary>
        public static string BuildVideoEncoderOptions(string encoder, string preset, int crf)
        {
            var name = (encoder ?? string.Empty).Trim().ToLowerInvariant();
            if (name.Length == 0) name = "libx264";

            var quality = ClampVideoCrf(crf).ToString(CultureInfo.InvariantCulture);
            var head = "-c:v " + name;

            if (name.EndsWith("_nvenc", StringComparison.Ordinal))
                return head + " -preset " + preset + " -cq " + quality;

            if (name.EndsWith("_qsv", StringComparison.Ordinal))
                return head + " -preset " + preset + " -global_quality " + quality;

            if (FfmpegFilterText.IsHardwareEncoder(name))
                return head;   // amf / videotoolbox / mf：不猜参数，用编码器默认值

            return head + " -preset " + preset + " -crf " + quality;
        }

        /// <summary>
        /// 烧录叠加层（水印图片 / 文字卡片）的转码参数。
        /// <para>
        /// 与 <see cref="BuildConvertToMp4Arguments"/> 的<b>根本区别</b>：那边视频是
        /// <c>-c:v copy</c>（流拷贝，几秒完事），这边任何叠加都要求把视频<b>解码→绘制→重编码</b>，
        /// 一首歌从几秒变成一两分钟甚至更久。H.264 的帧间预测决定了没有捷径 ——
        /// 哪怕水印只出现 5 秒，也必须整首重编码。
        /// </para>
        /// <para>
        /// 所有叠加元素必须合并进<b>同一次</b>调用（见 <see cref="OverlayFilterBuilder"/>）：
        /// 分几趟做等于重编码几遍，时间按倍数涨，画质还多损失几代。
        /// </para>
        /// <para>
        /// 加了 <c>-progress pipe:1</c>：重编码耗时长，没有进度上报的话队列项会长时间
        /// 停在"正在转码"不动，看着像卡死。ffmpeg 会往 stdout 吐 <c>out_time_ms=</c>
        /// （<b>单位其实是微秒</b>，这是 ffmpeg 的老命名坑）和 <c>progress=continue|end</c>。
        /// </para>
        /// </summary>
        /// <param name="plan">渲染计划，含滤镜图、图片输入顺序、待写文本文件。</param>
        /// <param name="sampleSeconds">
        /// &gt;0 时只烧前 N 秒（「试烧样片」用，几秒就能验证效果）。
        /// 0 表示完整烧录。
        /// </param>
        /// <param name="copyAudio">
        /// 音频是否直接流拷贝。<b>仅当源的音频已经是 AAC 时才可以传 true</b>：
        /// 源是 TS 时音轨是 mp2，copy 进 MP4 会被标成 mp3，
        /// 多数播放器解不出来，表现为"画面正常但完全没声音"（docs 坑1）。
        /// </param>
        public static string BuildBurnInArguments(
            string source,
            string output,
            OverlayRenderPlan plan,
            VocalOption vocal,
            int audioTrackCount,
            int audioBitrateKbps,
            string videoEncoder,
            string videoPreset,
            int videoCrf,
            int sampleSeconds,
            bool copyAudio)
        {
            if (plan == null || plan.IsEmpty || string.IsNullOrEmpty(plan.FilterComplex))
                throw new ArgumentException("渲染计划为空，不应走烧录路径", nameof(plan));

            var builder = new StringBuilder();
            builder.Append("-y -hide_banner");

            // 试烧样片：-t 放在 -i **之前**才是输入选项（限制读取时长），
            // 放在后面会变成输出选项 —— 那样 ffmpeg 会把整首解码完再截断，慢得多。
            if (sampleSeconds > 0)
            {
                builder.Append(" -t ")
                       .Append(sampleSeconds.ToString(CultureInfo.InvariantCulture));
            }

            builder.Append(" -i ").Append(Quote(source));

            // 图片输入按渲染计划里的顺序追加，滤镜图用 [1:v]、[2:v]… 引用它们。
            // ⚠️ 不需要 -loop 1：图片输入只有一帧，overlay 默认 repeatlast=1 会把它
            //    一直保持到主视频结束，这正是我们要的。加了 -loop 1 反而变成无限输入，
            //    还得配 -shortest，凭空多一个失败点。
            foreach (var image in plan.ImageInputs)
            {
                builder.Append(" -i ").Append(Quote(image));
            }

            builder.Append(" -filter_complex ").Append(Quote(plan.FilterComplex));

            // 视频走滤镜图的输出标签，不能再写 0:v（那条流已经被滤镜消费掉了）。
            builder.Append(" -map ").Append(Quote("[" + plan.OutputLabel + "]"));

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
                        builder.Append(" -map 0:a:0 -map 0:a:1");
                        break;
                }
            }
            else if (audioTrackCount == 1)
            {
                builder.Append(" -map 0:a:0");
            }

            builder.Append(' ').Append(BuildVideoEncoderOptions(videoEncoder, videoPreset, videoCrf));
            builder.Append(" -pix_fmt yuv420p");

            if (audioTrackCount <= 0)
            {
                builder.Append(" -an");
            }
            else if (copyAudio)
            {
                builder.Append(" -c:a copy");
            }
            else
            {
                builder.Append(" -c:a aac -b:a ")
                       .Append(ClampBitrate(audioBitrateKbps).ToString(CultureInfo.InvariantCulture))
                       .Append("k -ac 2");
            }

            builder.Append(" -movflags +faststart");
            builder.Append(" -progress pipe:1");
            builder.Append(' ').Append(Quote(output));

            return builder.ToString();
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
