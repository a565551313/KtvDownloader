using System;
using System.Collections.Generic;
using System.IO;
using KtvDownloader.Forms;
using KtvDownloader.Infrastructure.Config;
using KtvDownloader.Models;
using KtvDownloader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KtvDownloader.Tests
{
    /// <summary>
    /// 叠加层管线的其余纯逻辑部分：滤镜引用转义、进度解析、烧录命令行、配置持久化。
    /// </summary>
    [TestClass]
    public class OverlayPipelineTests
    {
        #region FfmpegFilterText

        [TestMethod]
        public void 引用_普通值直接加单引号()
        {
            Assert.AreEqual("'between(t,0,5)'", FfmpegFilterText.QuoteFilterValue("between(t,0,5)"));
            Assert.AreEqual("'C:/a b/c.png'", FfmpegFilterText.QuoteFilterValue("C:/a b/c.png"));
        }

        [TestMethod]
        public void 引用_值里含单引号时用闭合转义重开序列()
        {
            // ffmpeg 文档的做法：闭合引号 → \' → 重新开引号。
            // 直接写 'foo'bar' 会解析错位。
            Assert.AreEqual("'foo'\\''bar'", FfmpegFilterText.QuoteFilterValue("foo'bar"));
            Assert.AreEqual("''\\'''", FfmpegFilterText.QuoteFilterValue("'"));
        }

        [TestMethod]
        public void 引用_null当成空串()
        {
            Assert.AreEqual("''", FfmpegFilterText.QuoteFilterValue(null));
        }

        [TestMethod]
        public void 路径_反斜杠换成正斜杠()
        {
            // 反斜杠在滤镜语法里是转义符，C:\temp 里的 \t 会被吃掉。
            Assert.AreEqual("C:/Windows/Fonts/simhei.ttf",
                FfmpegFilterText.ToFilterPath(@"C:\Windows\Fonts\simhei.ttf"));
            Assert.AreEqual(string.Empty, FfmpegFilterText.ToFilterPath(null));
        }

        [TestMethod]
        public void 颜色_井号格式转0x格式()
        {
            Assert.AreEqual("0x1E90FF", FfmpegFilterText.Color("#1E90FF"));
            Assert.AreEqual("0x1E90FF", FfmpegFilterText.Color("#1e90ff"));
            Assert.AreEqual("0x1E90FF", FfmpegFilterText.Color("1E90FF"));
        }

        [TestMethod]
        public void 颜色_非法值回落而不是抛异常()
        {
            // 颜色写错不该让整次下载失败。
            Assert.AreEqual("0xFFFFFF", FfmpegFilterText.Color("不是颜色"));
            Assert.AreEqual("0x000000", FfmpegFilterText.Color("#12345", "#000000"));
            Assert.AreEqual("0xFF0000", FfmpegFilterText.Color(null, "#FF0000"));
        }

        [TestMethod]
        public void 数字参数_用不变文化格式()
        {
            // 区域设置是德语/法语时小数点是逗号，ffmpeg 会解析失败。
            Assert.AreEqual("aa=0.5", FfmpegFilterText.Option("aa", 0.5));
            Assert.AreEqual("w=128", FfmpegFilterText.Option("w", 128));
        }

        [TestMethod]
        public void 硬件编码器识别()
        {
            Assert.IsFalse(FfmpegFilterText.IsHardwareEncoder("libx264"));
            Assert.IsFalse(FfmpegFilterText.IsHardwareEncoder(null));
            Assert.IsTrue(FfmpegFilterText.IsHardwareEncoder("h264_nvenc"));
            Assert.IsTrue(FfmpegFilterText.IsHardwareEncoder("hevc_qsv"));
            Assert.IsTrue(FfmpegFilterText.IsHardwareEncoder("h264_amf"));
        }

        #endregion

        #region 编码器参数

        [TestMethod]
        public void 编码器_软编用preset加crf()
        {
            Assert.AreEqual("-c:v libx264 -preset veryfast -crf 20",
                FfmpegArguments.BuildVideoEncoderOptions("libx264", "veryfast", 20));
        }

        [TestMethod]
        public void 编码器_留空时默认libx264()
        {
            Assert.AreEqual("-c:v libx264 -preset veryfast -crf 20",
                FfmpegArguments.BuildVideoEncoderOptions(null, "veryfast", 20));
            Assert.AreEqual("-c:v libx264 -preset veryfast -crf 20",
                FfmpegArguments.BuildVideoEncoderOptions("  ", "veryfast", 20));
        }

        [TestMethod]
        public void 编码器_硬编各家吃不同的质量参数()
        {
            // 传错 ffmpeg 会直接失败退出，所以每家的写法必须对。
            Assert.AreEqual("-c:v h264_nvenc -preset veryfast -cq 20",
                FfmpegArguments.BuildVideoEncoderOptions("h264_nvenc", "veryfast", 20));

            Assert.AreEqual("-c:v h264_qsv -preset veryfast -global_quality 20",
                FfmpegArguments.BuildVideoEncoderOptions("h264_qsv", "veryfast", 20));

            // 没列进来的硬编不猜参数，用编码器默认值 —— 画质不可控但至少能跑通。
            Assert.AreEqual("-c:v h264_amf",
                FfmpegArguments.BuildVideoEncoderOptions("h264_amf", "veryfast", 20));
        }

        [TestMethod]
        public void 质量因子夹到合法区间()
        {
            Assert.AreEqual(0, FfmpegArguments.ClampVideoCrf(-5));
            Assert.AreEqual(51, FfmpegArguments.ClampVideoCrf(999));
            Assert.AreEqual(20, FfmpegArguments.ClampVideoCrf(20));
        }

        #endregion

        #region 烧录命令行

        private static OverlayRenderPlan Plan(string filterComplex, string outputLabel, params string[] imageInputs)
        {
            var plan = new OverlayRenderPlan { FilterComplex = filterComplex };
            foreach (var image in imageInputs) plan.ImageInputs.Add(image);
            plan.OutputLabel = outputLabel;
            plan.MarkNonEmpty();
            return plan;
        }

        [TestMethod]
        public void 烧录_视频走滤镜图输出标签而不是0v()
        {
            var plan = Plan("[1:v]scale=w=128:h=64[ov1];[0:v][ov1]overlay=x=1132:y=20[v0]", "v0", "logo.png");

            var args = FfmpegArguments.BuildBurnInArguments(
                "song.ts", "out.mp4", plan, VocalOption.KeepBoth, 2, 192, "libx264", "veryfast", 20, 0, false);

            // 0:v 已经被滤镜消费掉了，再 map 一次 ffmpeg 会报错。
            Assert.IsTrue(args.Contains("-map \"[v0]\""), "没有映射滤镜输出：" + args);
            Assert.IsFalse(args.Contains("-map 0:v"), "不该再映射原始视频流：" + args);

            Assert.IsTrue(args.Contains("-filter_complex"), args);
            Assert.IsTrue(args.Contains("-c:v libx264 -preset veryfast -crf 20"), args);
            Assert.IsTrue(args.Contains("-pix_fmt yuv420p"), args);
        }

        [TestMethod]
        public void 烧录_图片输入按顺序追加在主视频之后()
        {
            var plan = Plan("[0:v]null[v0]", "v0", "a.png", "b.png");

            var args = FfmpegArguments.BuildBurnInArguments(
                "song.ts", "out.mp4", plan, VocalOption.KeepBoth, 2, 192, "libx264", "veryfast", 20, 0, false);

            var songIndex = args.IndexOf("\"song.ts\"", StringComparison.Ordinal);
            var aIndex = args.IndexOf("\"a.png\"", StringComparison.Ordinal);
            var bIndex = args.IndexOf("\"b.png\"", StringComparison.Ordinal);

            Assert.IsTrue(songIndex >= 0 && aIndex > songIndex && bIndex > aIndex,
                "输入顺序不对（滤镜图用 [1:v]/[2:v] 引用，顺序错了就叠错图）：" + args);
        }

        [TestMethod]
        public void 烧录_不加loop避免变成无限输入()
        {
            // 图片输入只有一帧，overlay 默认 repeatlast=1 会把它保持到最后。
            // 加了 -loop 1 反而变成无限输入，还得配 -shortest，凭空多一个失败点。
            var plan = Plan("[0:v]null[v0]", "v0", "a.png");

            var args = FfmpegArguments.BuildBurnInArguments(
                "song.ts", "out.mp4", plan, VocalOption.KeepBoth, 2, 192, "libx264", "veryfast", 20, 0, false);

            Assert.IsFalse(args.Contains("-loop"), args);
            Assert.IsFalse(args.Contains("-shortest"), args);
        }

        [TestMethod]
        public void 烧录_音频已是AAC时直接拷贝()
        {
            var plan = Plan("[0:v]null[v0]", "v0");

            var args = FfmpegArguments.BuildBurnInArguments(
                "song.mp4", "out.mp4", plan, VocalOption.KeepBoth, 2, 192, "libx264", "veryfast", 20, 0, true);

            // 白转一遍 AAC 只是凭空多一代损失。
            Assert.IsTrue(args.Contains("-c:a copy"), args);
            Assert.IsFalse(args.Contains("-c:a aac"), args);
        }

        [TestMethod]
        public void 烧录_音频是mp2时必须重编码()
        {
            // mp2 直接 copy 进 MP4 会被标成 mp3，多数播放器解不出来，
            // 表现为"画面正常但完全没声音"（docs 坑1）。
            var plan = Plan("[0:v]null[v0]", "v0");

            var args = FfmpegArguments.BuildBurnInArguments(
                "song.ts", "out.mp4", plan, VocalOption.KeepBoth, 2, 192, "libx264", "veryfast", 20, 0, false);

            Assert.IsTrue(args.Contains("-c:a aac -b:a 192k -ac 2"), args);
            Assert.IsFalse(args.Contains("-c:a copy"), args);
        }

        [TestMethod]
        public void 烧录_双音轨默认两条都保留()
        {
            var plan = Plan("[0:v]null[v0]", "v0");

            var both = FfmpegArguments.BuildBurnInArguments(
                "s.ts", "o.mp4", plan, VocalOption.KeepBoth, 2, 192, "libx264", "veryfast", 20, 0, false);
            Assert.IsTrue(both.Contains("-map 0:a:0 -map 0:a:1"), both);

            var original = FfmpegArguments.BuildBurnInArguments(
                "s.ts", "o.mp4", plan, VocalOption.OriginalOnly, 2, 192, "libx264", "veryfast", 20, 0, false);
            Assert.IsTrue(original.Contains("-map 0:a:0"), original);
            Assert.IsFalse(original.Contains("-map 0:a:1"), original);
        }

        [TestMethod]
        public void 烧录_没有音轨时不映射音频()
        {
            // 映射不存在的 0:a:0 会让 ffmpeg 整体失败。
            var plan = Plan("[0:v]null[v0]", "v0");

            var args = FfmpegArguments.BuildBurnInArguments(
                "s.ts", "o.mp4", plan, VocalOption.KeepBoth, 0, 192, "libx264", "veryfast", 20, 0, false);

            Assert.IsTrue(args.Contains(" -an"), args);
            Assert.IsFalse(args.Contains("0:a:"), args);
        }

        [TestMethod]
        public void 烧录_试烧样片的时长限制要放在输入之前()
        {
            var plan = Plan("[0:v]null[v0]", "v0");

            var args = FfmpegArguments.BuildBurnInArguments(
                "s.ts", "o.mp4", plan, VocalOption.KeepBoth, 2, 192, "libx264", "veryfast", 20, 5, false);

            var tIndex = args.IndexOf("-t 5", StringComparison.Ordinal);
            var inputIndex = args.IndexOf("-i ", StringComparison.Ordinal);

            // -t 在 -i 之前才是输入选项（限制读取时长，秒出）；
            // 放在后面是输出选项 —— ffmpeg 会把整首解码完再截断，慢得多。
            Assert.IsTrue(tIndex >= 0 && tIndex < inputIndex,
                "-t 位置不对，样片会跑完整首：" + args);
        }

        [TestMethod]
        public void 烧录_带进度输出()
        {
            var plan = Plan("[0:v]null[v0]", "v0");

            var args = FfmpegArguments.BuildBurnInArguments(
                "s.ts", "o.mp4", plan, VocalOption.KeepBoth, 2, 192, "libx264", "veryfast", 20, 0, false);

            // 重编码要一两分钟，没有进度上报的话队列项看着像卡死。
            Assert.IsTrue(args.Contains("-progress pipe:1"), args);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void 烧录_空计划直接抛异常()
        {
            // 空计划意味着没有任何可烧的东西，走到这里说明调用方的分支判断错了。
            // 宁可当场炸，也不要拼出一条没有 -filter_complex 却 -map "[v0]" 的命令。
            FfmpegArguments.BuildBurnInArguments(
                "s.ts", "o.mp4", new OverlayRenderPlan(), VocalOption.KeepBoth, 2, 192,
                "libx264", "veryfast", 20, 0, false);
        }

        // ── SplitBoth 的视频流复用 ──────────────────────────────────────
        //
        // 原唱版和伴奏版的画面完全相同（SplitVocal 两个抽取路径都是 -c:v copy），
        // 各烧一遍等于把同一段视频重编码两次：一首 4 分钟的歌从 1–3 分钟变成
        // 2–6 分钟，画质还多损失一代。所以只烧第一个，剩下的拷视频流 + 配各自音轨。

        [TestMethod]
        public void 复用_视频取第一个输入音频取第二个且两边都流拷贝()
        {
            var args = FfmpegArguments.BuildVideoReuseArguments(
                "burned-原唱.mp4", "伴奏.mp4", "out.mp4", 1, true, 192);

            // 视频不重编码 —— 这正是省时间的全部来源。
            Assert.IsTrue(args.Contains("-c:v copy"), args);
            Assert.IsFalse(args.Contains("-c:v libx264"), "不该重编码视频：" + args);

            // ⚠️ 不写 -map 的话 ffmpeg 的默认流选择会各挑一条"最好的"，
            // 很可能把烧好视频那个文件的音轨也带进来，成品就变成两条音轨。
            Assert.IsTrue(args.Contains("-map 0:v:0"), args);
            Assert.IsTrue(args.Contains("-map 1:a:0"), args);

            // 音轨来自第二个输入，不是第一个（第一个的音轨是原唱，不能带到伴奏版里）。
            var videoInput = args.IndexOf("\"burned-原唱.mp4\"", StringComparison.Ordinal);
            var audioInput = args.IndexOf("\"伴奏.mp4\"", StringComparison.Ordinal);
            Assert.IsTrue(videoInput >= 0 && audioInput > videoInput,
                "输入顺序错了：0 号必须是烧好视频的那个：" + args);
        }

        [TestMethod]
        public void 复用_音轨已是AAC时直接拷贝()
        {
            var args = FfmpegArguments.BuildVideoReuseArguments(
                "v.mp4", "a.mp4", "o.mp4", 1, true, 192);

            Assert.IsTrue(args.Contains("-c:a copy"), args);
            Assert.IsFalse(args.Contains("-c:a aac"), args);
        }

        [TestMethod]
        public void 复用_音轨不是AAC时必须重编码()
        {
            // 不是 AAC 的编码拷进 MP4 会踩 docs 坑1：容器标签和实际数据不符，
            // 表现为"画面正常但完全没声音"。
            var args = FfmpegArguments.BuildVideoReuseArguments(
                "v.mp4", "a.ts", "o.mp4", 1, false, 192);

            Assert.IsTrue(args.Contains("-c:a aac -b:a 192k -ac 2"), args);
            Assert.IsFalse(args.Contains("-c:a copy"), args);
        }

        [TestMethod]
        public void 复用_没有音轨时不映射音频()
        {
            var args = FfmpegArguments.BuildVideoReuseArguments(
                "v.mp4", "silent.mp4", "o.mp4", 0, true, 192);

            Assert.IsTrue(args.Contains(" -an"), args);
            Assert.IsFalse(args.Contains("1:a:"), "映射不存在的音轨会让整条命令失败：" + args);

            // 视频照样要拷过来。
            Assert.IsTrue(args.Contains("-map 0:v:0"), args);
            Assert.IsTrue(args.Contains("-c:v copy"), args);
        }

        [TestMethod]
        public void 复用_输出带faststart()
        {
            var args = FfmpegArguments.BuildVideoReuseArguments(
                "v.mp4", "a.mp4", "o.mp4", 1, true, 192);

            Assert.IsTrue(args.Contains("-movflags +faststart"), args);
            Assert.IsTrue(args.EndsWith("\"o.mp4\""), "输出必须是最后一个参数：" + args);
        }

        [TestMethod]
        public void 复用_空的源路径直接抛异常()
        {
            // 拼出一条 -i "" 的命令，ffmpeg 的报错和真正的原因隔好几层。
            try
            {
                FfmpegArguments.BuildVideoReuseArguments(null, "a.mp4", "o.mp4", 1, true, 192);
                Assert.Fail("视频源为空时应该抛 ArgumentException");
            }
            catch (ArgumentException) { }

            try
            {
                FfmpegArguments.BuildVideoReuseArguments("v.mp4", "  ", "o.mp4", 1, true, 192);
                Assert.Fail("音轨源为空时应该抛 ArgumentException");
            }
            catch (ArgumentException) { }
        }

        #endregion

        #region 进度解析

        [TestMethod]
        public void 进度_out_time_ms的单位其实是微秒()
        {
            // ⚠️ 这是 ffmpeg 的历史遗留命名。按毫秒算的话进度会快 1000 倍，
            // 刚开头就报 100%。
            var parser = new FfmpegProgressParser(60.0);

            double percent;
            Assert.IsTrue(parser.TryParse("out_time_ms=30000000", out percent));
            Assert.AreEqual(50.0, percent, 0.001);
        }

        [TestMethod]
        public void 进度_新版的out_time_us同样按微秒解析()
        {
            var parser = new FfmpegProgressParser(120.0);

            double percent;
            Assert.IsTrue(parser.TryParse("out_time_us=30000000", out percent));
            Assert.AreEqual(25.0, percent, 0.001);
        }

        [TestMethod]
        public void 进度_整数百分比没变时不重复上报()
        {
            // ffmpeg 每帧吐一整块，一秒几十行。逐行上报会把 UI 消息队列淹掉。
            var parser = new FfmpegProgressParser(100.0);
            double percent;

            Assert.IsTrue(parser.TryParse("out_time_ms=10000000", out percent));   // 10%
            Assert.AreEqual(10.0, percent, 0.001);

            Assert.IsFalse(parser.TryParse("out_time_ms=10400000", out percent));   // 10.4% → 还是 10
            Assert.IsTrue(parser.TryParse("out_time_ms=11000000", out percent));    // 11%
            Assert.AreEqual(11.0, percent, 0.001);
        }

        [TestMethod]
        public void 进度_100百分比一定会发一次()
        {
            var parser = new FfmpegProgressParser(10.0);
            double percent;

            Assert.IsTrue(parser.TryParse("out_time_ms=10000000", out percent));
            Assert.AreEqual(100.0, percent, 0.001);
        }

        [TestMethod]
        public void 进度_超出总时长时夹到100()
        {
            var parser = new FfmpegProgressParser(10.0);
            double percent;

            Assert.IsTrue(parser.TryParse("out_time_ms=99000000", out percent));
            Assert.AreEqual(100.0, percent, 0.001);
        }

        [TestMethod]
        public void 进度_总时长未知时不上报()
        {
            // 瞎算不如不算：报一个乱跳的数字比没有进度更让人困惑。
            var parser = new FfmpegProgressParser(0);
            double percent;

            Assert.IsFalse(parser.TryParse("out_time_ms=5000000", out percent));
        }

        [TestMethod]
        public void 进度_无关的行直接忽略()
        {
            var parser = new FfmpegProgressParser(60.0);
            double percent;

            Assert.IsFalse(parser.TryParse("frame=123", out percent));
            Assert.IsFalse(parser.TryParse("fps=25.0", out percent));
            Assert.IsFalse(parser.TryParse("progress=continue", out percent));
            Assert.IsFalse(parser.TryParse("", out percent));
            Assert.IsFalse(parser.TryParse(null, out percent));
            Assert.IsFalse(parser.TryParse("=5", out percent));
            Assert.IsFalse(parser.TryParse("out_time_ms=不是数字", out percent));
            Assert.IsFalse(parser.TryParse("out_time_ms=-5", out percent));
        }

        #endregion

        #region 探测输出解析

        // 下面几段都是真机 ffmpeg -i 的 stderr 原样（只改了文件名）。
        // 这几条正则决定了烧录时音频是重编码还是流拷贝、坐标按多大分辨率算，
        // 判错不会报错，只会安静地出一个坏文件 —— 所以必须钉住。

        /// <summary>双音轨 TS：KTV 片源的典型形态，音轨是 mp2。</summary>
        private const string DualTrackTs = @"ffmpeg version 4.4.2 Copyright (c) 2000-2021 the FFmpeg developers
Input #0, mpegts, from 'C:\\ktv\\发如雪.ts':
  Duration: 00:04:12.34, start: 0.100000, bitrate: 8123 kb/s
  Program 1
    Metadata:
      service_name    : Service01
      service_provider: FFmpeg
  Stream #0:1[0x101]: Video: h264 (Main) ([27][0][0][0] / 0x001B), yuv420p(tv, bt709), 1280x720 [SAR 1:1 DAR 16:9], 25 fps, 25 tbr, 90k tbn, 50 tbc
  Stream #0:2[0x102]: Audio: mp2 ([4][0][0][0] / 0x0004), 48000 Hz, stereo, fltp, 384 kb/s
  Stream #0:3[0x103]: Audio: mp2 ([4][0][0][0] / 0x0004), 48000 Hz, stereo, fltp, 384 kb/s
At least one output file must be specified";

        /// <summary>
        /// 中间产物 MP4：视频 h264 + 音频 aac。
        /// 烧录第二步就是探它，靠 AudioIsAac 决定能不能 -c:a copy。
        /// </summary>
        private const string IntermediateMp4 = @"ffmpeg version 4.4.2 Copyright (c) 2000-2021 the FFmpeg developers
Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'C:\\ktv\\发如雪.mp4':
  Metadata:
    major_brand     : isom
    encoder         : Lavf59.27.100
  Duration: 00:04:12.34, start: 0.000000, bitrate: 5021 kb/s
  Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(tv, bt709), 1280x720 [SAR 1:1 DAR 16:9], 4800 kb/s, 25 fps, 25 tbr, 12800 tbn, 50 tbc (default)
  Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, stereo, fltp, 128 kb/s (default)
At least one output file must be specified";

        [TestMethod]
        public void 探测_双音轨TS解析出两条mp2和分辨率时长()
        {
            var probe = MediaService.ParseProbeOutput(DualTrackTs);

            Assert.IsTrue(probe.Usable);
            Assert.AreEqual(2, probe.AudioTrackCount);
            Assert.AreEqual("mp2", probe.AudioCodec);

            // 关键：mp2 绝不能被当成可以直接拷进 MP4 的编码（docs 坑1）。
            Assert.IsFalse(probe.AudioIsAac, "mp2 判成 AAC 会导致拷进 MP4 后静音播放");

            Assert.AreEqual(1280, probe.VideoWidth);
            Assert.AreEqual(720, probe.VideoHeight);
            Assert.IsTrue(probe.HasVideoSize);

            Assert.AreEqual(252.34, probe.DurationSeconds, 0.001);
        }

        [TestMethod]
        public void 探测_中间产物MP4的音频是AAC可以直接拷贝()
        {
            var probe = MediaService.ParseProbeOutput(IntermediateMp4);

            Assert.IsTrue(probe.Usable);
            Assert.AreEqual(1, probe.AudioTrackCount);
            Assert.AreEqual("aac", probe.AudioCodec);

            // 这是"两步只编码一遍"的前提：判成 false 就会白转一遍音频，多一代损失。
            Assert.IsTrue(probe.AudioIsAac, "AAC 没认出来会导致音频被无谓重编码");
        }

        [TestMethod]
        public void 探测_十六进制编码标签不会被当成视频分辨率()
        {
            // MP4 的视频行里有 (avc1 / 0x31637661) 这种标签，
            // 里面既有数字又有 x，正则写松一点就会把 0x31637661 抠成尺寸。
            var probe = MediaService.ParseProbeOutput(IntermediateMp4);

            Assert.AreEqual(1280, probe.VideoWidth);
            Assert.AreEqual(720, probe.VideoHeight);
        }

        /// <summary>
        /// 单音轨老式卡拉OK（原唱/伴奏压在左右声道，docs §7.5）。
        /// 刻意写成独立常量而不是从 DualTrackTs 删一行 ——
        /// 逐字串替换依赖源码文件的换行符，git 在 Windows 上 checkout 成 CRLF 后就失配了。
        /// </summary>
        private const string SingleTrackTs = @"Input #0, mpegts, from 'C:\\ktv\\老歌.ts':
  Duration: 00:03:20.10, start: 0.100000, bitrate: 6000 kb/s
  Program 1
  Stream #0:1[0x101]: Video: mpeg2video (Main), yuv420p(tv, progressive), 720x576 [SAR 16:15 DAR 4:3], 25 fps, 25 tbr, 90k tbn, 50 tbc
  Stream #0:2[0x102]: Audio: mp2 ([4][0][0][0] / 0x0004), 48000 Hz, stereo, fltp, 384 kb/s
At least one output file must be specified";

        [TestMethod]
        public void 探测_单音轨老式卡拉OK只数出一条()
        {
            var probe = MediaService.ParseProbeOutput(SingleTrackTs);

            Assert.IsTrue(probe.Usable);
            Assert.AreEqual(1, probe.AudioTrackCount);
            Assert.AreEqual("mp2", probe.AudioCodec);

            // 单音轨走的不是 SetAudioTrack 而是 libVLC 声道切换（docs §7.5）。
            // 数成 2 条会让预览界面摆出一个切了没用的音轨下拉框。
            Assert.IsFalse(probe.AudioIsAac);

            // mpeg2video + 720x576 的 PAL 片源同样要认出尺寸，
            // 否则叠加坐标按 0x0 算，水印直接消失。
            Assert.AreEqual(720, probe.VideoWidth);
            Assert.AreEqual(576, probe.VideoHeight);
            Assert.AreEqual(200.10, probe.DurationSeconds, 0.001);
        }

        [TestMethod]
        public void 探测_没有音轨时计数为零且编码为空()
        {
            const string stderr = @"Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'silent.mp4':
  Duration: 00:00:30.00, start: 0.000000, bitrate: 1000 kb/s
  Stream #0:0(und): Video: h264 (High), yuv420p, 1920x1080, 25 fps, 25 tbr, 12800 tbn, 50 tbc (default)";

            var probe = MediaService.ParseProbeOutput(stderr);

            Assert.IsTrue(probe.Usable);
            Assert.AreEqual(0, probe.AudioTrackCount);
            Assert.IsNull(probe.AudioCodec);
            Assert.IsFalse(probe.AudioIsAac);
            Assert.AreEqual(1920, probe.VideoWidth);
            Assert.AreEqual(1080, probe.VideoHeight);
        }

        [TestMethod]
        public void 探测_纯视频无音频时调用方会加an()
        {
            // AudioTrackCount = 0 → BuildBurnInArguments 必须走 -an，
            // 映射不存在的 0:a:0 会让整条命令失败。
            var args = FfmpegArguments.BuildBurnInArguments(
                "silent.mp4", "out.mp4",
                Plan("[0:v]null[v0]", "v0"),
                VocalOption.KeepBoth, 0, 192, "libx264", "veryfast", 20, 0, true);

            Assert.IsTrue(args.Contains(" -an"), args);
        }

        [TestMethod]
        public void 探测_空输出或垃圾输出都标记为不可用()
        {
            // Usable = false 的结果不该进缓存：探测失败往往是暂时的
            // （文件还在写、杀毒软件占着句柄），缓存下来就永久坏了。
            Assert.IsFalse(MediaService.ParseProbeOutput(null).Usable);
            Assert.IsFalse(MediaService.ParseProbeOutput(string.Empty).Usable);
            Assert.IsFalse(MediaService.ParseProbeOutput("   ").Usable);

            // 一条流信息都认不出来时必须判为不可用。
            // 判成可用的话 AudioTrackCount=0 会被当真 → 烧录加 -an
            // → 产出"画面正常但完全没声音"的文件（docs 坑1），而且还被缓存住。
            var garbage = MediaService.ParseProbeOutput("ffmpeg: command not found");
            Assert.IsFalse(garbage.Usable, "垃圾输出不该算探测成功");
            Assert.AreEqual(0, garbage.AudioTrackCount);
            Assert.IsFalse(garbage.HasVideoSize);

            // ffmpeg 报了错、没给出流信息（文件损坏 / 格式不支持）也一样。
            var failed = MediaService.ParseProbeOutput(
                "Input #0, mpegts, from 'broken.ts':" + "\n" +
                "broken.ts: Invalid data found when processing input");
            Assert.IsFalse(failed.Usable);
        }

        [TestMethod]
        public void 探测_空结果的默认值不会让烧录误判()
        {
            var empty = MediaProbeResult.Empty();

            Assert.IsFalse(empty.Usable);
            Assert.IsFalse(empty.HasVideoSize, "没有分辨率就不该去算叠加坐标");
            Assert.IsFalse(empty.AudioIsAac, "默认必须偏向重编码：拷错会静音，重编码只是慢一点");
            Assert.AreEqual(0, empty.AudioTrackCount);
        }

        [TestMethod]
        public void 探测_时长用于进度百分比()
        {
            // FfmpegProgressParser 拿 DurationSeconds 当分母，
            // 解析成 0 的话进度会一直不上报（分母 <= 0 直接返回 false）。
            var probe = MediaService.ParseProbeOutput(DualTrackTs);

            var parser = new FfmpegProgressParser(probe.DurationSeconds);
            double percent;

            Assert.IsTrue(parser.TryParse("out_time_ms=126170000", out percent));
            Assert.AreEqual(50.0, percent, 0.01);
        }

        #endregion

        #region 配置持久化

        [TestMethod]
        public void 存储_往返保存与读取()
        {
            var path = Path.Combine(Path.GetTempPath(), "overlay-test-" + Guid.NewGuid().ToString("N") + ".jsonl");

            try
            {
                var store = new OverlayStore(path, NullLogger.Instance);

                var original = new OverlayComposition
                {
                    Enabled = true,
                    ShowInPreview = true,
                    BurnOnDownload = false,
                    VideoEncoder = "h264_nvenc",
                    VideoPreset = "medium",
                    VideoCrf = 18
                };

                var image = OverlayTextPresets.CreateImageElement(@"C:\img\logo.png");
                image.Anchor = OverlayAnchor.TopRight;
                image.MarginX = 33;
                image.RelativeWidth = 0.2;
                image.Opacity = 0.75;
                original.Elements.Add(image);

                var text = OverlayTextPresets.CreateTextElement("欢迎光临\n第二行");
                text.HasTimeWindow = true;
                text.StartSeconds = 2.5;
                text.EndSeconds = 7.5;
                text.FontSizePx = 64;
                text.FillColor = "#FFD700";
                text.BorderWidthPx = 5;
                original.Elements.Add(text);

                original.Normalize();
                Assert.IsTrue(store.Save(original), store.LastError);

                var loaded = store.Load();

                Assert.IsTrue(loaded.Enabled);
                Assert.IsTrue(loaded.ShowInPreview);
                Assert.IsFalse(loaded.BurnOnDownload);
                Assert.AreEqual("h264_nvenc", loaded.VideoEncoder);
                Assert.AreEqual("medium", loaded.VideoPreset);
                Assert.AreEqual(18, loaded.VideoCrf);

                Assert.AreEqual(2, loaded.Elements.Count);

                var loadedImage = loaded.Elements[0];
                Assert.AreEqual(OverlayElementKind.Image, loadedImage.Kind);
                Assert.AreEqual(OverlayAnchor.TopRight, loadedImage.Anchor);
                Assert.AreEqual(33, loadedImage.MarginX);
                Assert.AreEqual(0.2, loadedImage.RelativeWidth, 0.0001);
                Assert.AreEqual(0.75, loadedImage.Opacity, 0.0001);

                var loadedText = loaded.Elements[1];
                Assert.AreEqual(OverlayElementKind.Text, loadedText.Kind);
                Assert.AreEqual("欢迎光临\n第二行", loadedText.Text);
                Assert.IsTrue(loadedText.HasTimeWindow);
                Assert.AreEqual(2.5, loadedText.StartSeconds, 0.001);
                Assert.AreEqual(7.5, loadedText.EndSeconds, 0.001);
                Assert.AreEqual(64, loadedText.FontSizePx);
                Assert.AreEqual("#FFD700", loadedText.FillColor);
                Assert.AreEqual(5, loadedText.BorderWidthPx);
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        [TestMethod]
        public void 存储_文件不存在时返回未启用的空配置()
        {
            var path = Path.Combine(Path.GetTempPath(), "overlay-missing-" + Guid.NewGuid().ToString("N") + ".jsonl");

            var store = new OverlayStore(path, NullLogger.Instance);
            var loaded = store.Load();

            Assert.IsFalse(loaded.Enabled);
            Assert.AreEqual(0, loaded.Elements.Count);
        }

        [TestMethod]
        public void 存储_注释行空行和坏行都跳过()
        {
            var path = Path.Combine(Path.GetTempPath(), "overlay-bad-" + Guid.NewGuid().ToString("N") + ".jsonl");

            try
            {
                File.WriteAllLines(path, new[]
                {
                    "# 这是注释",
                    "",
                    "   ",
                    "这根本不是 JSON",
                    "{\"Kind\":\"Meta\",\"Enabled\":true}",
                    "{\"Kind\":\"Image\",\"ImagePath\":\"C:/a.png\",\"Anchor\":2}"
                });

                var store = new OverlayStore(path, NullLogger.Instance);
                var loaded = store.Load();

                // 坏行只该跳过，不能让整个配置读不出来。
                Assert.IsTrue(loaded.Enabled);
                Assert.AreEqual(1, loaded.Elements.Count);
                Assert.AreEqual(OverlayAnchor.TopRight, loaded.Elements[0].Anchor);
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        [TestMethod]
        public void 存储_手改出的非法锚点回落默认值()
        {
            // 这文件用户可以拿记事本改，写成 99 就会得到一个不存在的枚举值，
            // 一路传到几何计算那里才出问题，而现象只是"水印位置不对"。
            var path = Path.Combine(Path.GetTempPath(), "overlay-anchor-" + Guid.NewGuid().ToString("N") + ".jsonl");

            try
            {
                File.WriteAllLines(path, new[]
                {
                    "{\"Kind\":\"Meta\",\"Enabled\":true}",
                    "{\"Kind\":\"Image\",\"ImagePath\":\"C:/a.png\",\"Anchor\":99,\"MarginX\":-500}"
                });

                var loaded = new OverlayStore(path, NullLogger.Instance).Load();

                Assert.AreEqual(1, loaded.Elements.Count);
                Assert.AreEqual(OverlayAnchor.TopRight, loaded.Elements[0].Anchor);
                Assert.IsTrue(loaded.Elements[0].MarginX >= 0);
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        [TestMethod]
        public void 存储_写入是UTF8且文本里的引号能安全往返()
        {
            var path = Path.Combine(Path.GetTempPath(), "overlay-quote-" + Guid.NewGuid().ToString("N") + ".jsonl");

            try
            {
                var composition = new OverlayComposition { Enabled = true };
                var element = OverlayTextPresets.CreateTextElement("他说：\"欢迎光临\" 100%");
                composition.Elements.Add(element);

                var store = new OverlayStore(path, NullLogger.Instance);
                Assert.IsTrue(store.Save(composition), store.LastError);

                var loaded = store.Load();
                Assert.AreEqual(1, loaded.Elements.Count);
                Assert.AreEqual("他说：\"欢迎光临\" 100%", loaded.Elements[0].Text);
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        #endregion

        #region 预设与样片时长

        [TestMethod]
        public void 预设_KTV蓝字白描边()
        {
            var preset = OverlayTextPresets.Find("KTV 蓝字白描边");
            Assert.IsNotNull(preset);

            var element = new OverlayElement();
            preset.ApplyTo(element);

            Assert.AreEqual(OverlayElementKind.Text, element.Kind);
            Assert.AreEqual("#1E90FF", element.FillColor);
            Assert.AreEqual("#FFFFFF", element.BorderColor);
            Assert.IsTrue(element.BorderWidthPx > 0);
        }

        [TestMethod]
        public void 预设_不用ttc字体()
        {
            // 微软雅黑是 msyh.ttc（一个文件里装多个字面），
            // ffmpeg 的 drawtext 经 libfreetype 打开 ttc 时取到哪个字面并不稳定。
            foreach (var preset in OverlayTextPresets.All)
            {
                Assert.IsFalse(preset.FontFamily.Contains("雅黑"),
                    preset.Name + " 用了 ttc 字体");
            }
        }

        [TestMethod]
        public void 预设_套用样式不动位置和时间窗口()
        {
            var element = OverlayTextPresets.CreateTextElement("原文");
            element.Anchor = OverlayAnchor.BottomLeft;
            element.MarginX = 77;
            element.StartSeconds = 3;
            element.EndSeconds = 9;
            element.Text = "改过的文字";

            OverlayTextPresets.Find("白字黑描边").ApplyTo(element);

            Assert.AreEqual(OverlayAnchor.BottomLeft, element.Anchor);
            Assert.AreEqual(77, element.MarginX);
            Assert.AreEqual(3, element.StartSeconds, 0.001);
            Assert.AreEqual(9, element.EndSeconds, 0.001);
            Assert.AreEqual("改过的文字", element.Text);
            Assert.AreEqual("#FFFFFF", element.FillColor);
        }

        [TestMethod]
        public void 样片时长_要覆盖最后一个时间窗口()
        {
            // 用户设了"10–15 秒"的片头，样片只烧前 5 秒的话什么都看不到，
            // 会以为功能坏了。
            var composition = new OverlayComposition { Enabled = true };

            var element = OverlayTextPresets.CreateTextElement("片头");
            element.HasTimeWindow = true;
            element.StartSeconds = 10;
            element.EndSeconds = 15;
            element.Normalize();
            composition.Elements.Add(element);

            Assert.AreEqual(16, OverlayEditorForm.SuggestSampleSeconds(composition));
        }

        [TestMethod]
        public void 样片时长_没有时间窗口时烧5秒就够()
        {
            var composition = new OverlayComposition { Enabled = true };
            composition.Elements.Add(OverlayTextPresets.CreateImageElement("a.png"));

            Assert.AreEqual(5, OverlayEditorForm.SuggestSampleSeconds(composition));
        }

        [TestMethod]
        public void 样片时长_上限30秒()
        {
            // 样片是为了快速确认效果，不该烧一整首。
            var composition = new OverlayComposition { Enabled = true };

            var element = OverlayTextPresets.CreateTextElement("片尾");
            element.HasTimeWindow = true;
            element.StartSeconds = 200;
            element.EndSeconds = 240;
            element.Normalize();
            composition.Elements.Add(element);

            Assert.AreEqual(30, OverlayEditorForm.SuggestSampleSeconds(composition));
        }

        #endregion

        #region 配置对象

        [TestMethod]
        public void 配置_原地替换让所有持有者同时看到新值()
        {
            // 主窗体、预览窗体、下载选项拿的是同一个实例。
            // 编辑器保存后如果只让某一方指向新对象，其他几方还在用旧的 ——
            // 表现为"设置里明明改了，预览没变"，而且每次重现的窗口还不一样。
            var live = new OverlayComposition { Enabled = false };
            var holder = live;

            var edited = new OverlayComposition { Enabled = true, VideoCrf = 25 };
            edited.Elements.Add(OverlayTextPresets.CreateTextElement("新片头"));

            live.ReplaceWith(edited);

            Assert.IsTrue(holder.Enabled, "原地替换失败：其他持有者看不到新配置");
            Assert.AreEqual(25, holder.VideoCrf);
            Assert.AreEqual(1, holder.Elements.Count);

            // 而且必须是深拷贝：之后再改 edited 不该影响 live。
            edited.Elements[0].Text = "又被改了";
            Assert.AreEqual("新片头", holder.Elements[0].Text);
        }

        [TestMethod]
        public void 配置_克隆是深拷贝()
        {
            var original = new OverlayComposition { Enabled = true };
            original.Elements.Add(OverlayTextPresets.CreateTextElement("片头"));

            var copy = original.Clone();
            copy.Elements[0].Text = "改副本";
            copy.Enabled = false;

            Assert.AreEqual("片头", original.Elements[0].Text);
            Assert.IsTrue(original.Enabled);
        }

        [TestMethod]
        public void 配置_元素数量有上限()
        {
            // 元素太多会让 filter_complex 长得离谱，也拖慢编码。
            var composition = new OverlayComposition();
            for (var i = 0; i < 50; i++)
            {
                composition.Elements.Add(OverlayTextPresets.CreateTextElement("文字 " + i));
            }

            composition.Normalize();

            Assert.AreEqual(20, composition.Elements.Count);
        }

        [TestMethod]
        public void 配置_非法编码器preset回落veryfast()
        {
            // 写错一个字母 ffmpeg 直接报错退出，用户看到的只是"下载失败"。
            var composition = new OverlayComposition { VideoPreset = "verfast" };
            composition.Normalize();

            Assert.AreEqual("veryfast", composition.VideoPreset);
        }

        [TestMethod]
        public void 配置_非法颜色回落默认值()
        {
            var element = new OverlayElement
            {
                Kind = OverlayElementKind.Text,
                Text = "x",
                FillColor = "蓝色",
                BorderColor = "12345"
            };
            element.Normalize();

            Assert.AreEqual("#1E90FF", element.FillColor);
            Assert.AreEqual("#FFFFFF", element.BorderColor);
        }

        [TestMethod]
        public void 元素_配置不完整时不可渲染并给出原因()
        {
            var noPath = OverlayTextPresets.CreateImageElement("   ");
            Assert.IsFalse(noPath.IsRenderable());
            Assert.AreEqual("未选择图片", noPath.GetSkipReason());

            var empty = OverlayTextPresets.CreateTextElement("   ");
            Assert.IsFalse(empty.IsRenderable());
            Assert.AreEqual("文本为空", empty.GetSkipReason());

            var hidden = OverlayTextPresets.CreateTextElement("正常");
            hidden.Visible = false;
            Assert.IsFalse(hidden.IsRenderable());
            Assert.AreEqual("已关闭", hidden.GetSkipReason());
        }

        [TestMethod]
        public void 元素_图片文件不存在时依然算可渲染()
        {
            // ⚠️ 这条是**故意**钉住"Models 不碰磁盘"的，别当成漏测给"修"回去。
            //
            // IsRenderable 里曾经有一句 File.Exists(ImagePath)，看着天经地义，实际有两个坏处：
            //
            //   1) docs §3.3 的分层里 Models 是"纯数据，无依赖"。一个 POCO 的判断方法去读磁盘，
            //      单元测试就没法用桩路径了 —— OverlayFilterBuilderTests 全程用
            //      C:\img\logo.png 这种故意不存在的路径，加上那句之后整组图片测试全红。
            //
            //   2) 更隐蔽的是行为退化：水印图放在移动硬盘上、下载时硬盘没插，
            //      HasAnyRenderable() 就变 false → NeedsBurnIn 变 false
            //      → **整首静默不烧录**，日志里一条 warning 都没有，
            //      用户拿到一个干干净净没水印的文件，还以为功能坏了。
            //
            // 文件到底读不读得到，只有真去读的那一刻才知道，而三个消费者都已经各自处理好了：
            // 预览端 OverlayRenderer.GetImage 返回 null 就跳过；
            // 烧录端 IOverlayAssetResolver.TryGetImageSize 失败就记 warning 跳过；
            // 编辑器在元素列表里显示［图片缺失］。
            var path = Path.Combine(Path.GetTempPath(),
                "肯定不存在的文件-" + Guid.NewGuid().ToString("N") + ".png");
            Assert.IsFalse(File.Exists(path), "测试前提：这个路径不该存在");

            var element = OverlayTextPresets.CreateImageElement(path);

            Assert.IsTrue(element.IsRenderable(), "图片暂时读不到不等于配置有错");
            Assert.IsNull(element.GetSkipReason());

            // 于是烧录照样会跑起来：跳过这个元素，把原因写进 BurnInResult.Warnings
            // 告诉用户（DownloadService 会把第一条塞进进度消息），而不是整首静默不烧。
            var composition = new OverlayComposition { Enabled = true, BurnOnDownload = true };
            composition.Elements.Add(element);
            Assert.IsTrue(composition.HasAnyRenderable());
        }

        [TestMethod]
        public void 元素_描述文字里带上位置和时间窗口()
        {
            var element = OverlayTextPresets.CreateTextElement("欢迎光临");
            element.Anchor = OverlayAnchor.TopCenter;
            element.HasTimeWindow = true;
            element.StartSeconds = 0;
            element.EndSeconds = 5;
            element.Normalize();

            var text = element.Describe();

            Assert.IsTrue(text.Contains("欢迎光临"), text);
            Assert.IsTrue(text.Contains("中上"), text);
            Assert.IsFalse(text.Contains("全程"), "设了时间窗口就不该显示\"全程\"：" + text);
            Assert.IsTrue(text.Contains("0") && text.Contains("5"), text);
        }

        #endregion
    }
}
