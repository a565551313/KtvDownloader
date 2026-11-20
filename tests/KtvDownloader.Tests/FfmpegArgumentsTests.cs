using System;
using KtvDownloader.Models;
using KtvDownloader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KtvDownloader.Tests
{
    /// <summary>
    /// ffmpeg 参数拼装。
    /// <para>
    /// 这些断言的价值在于：参数错了 ffmpeg **不一定报错**，
    /// 它可能安静地产出一个右声道全静音的文件，只有听成品才能发现。
    /// </para>
    /// </summary>
    [TestClass]
    public class FfmpegArgumentsTests
    {
        [TestMethod]
        public void Pan_左声道原唱时伴奏取c1()
        {
            // accomp != 2 → 左声道(c0)是原唱，右声道(c1)是伴奏。
            Assert.AreEqual("pan=stereo|c0=c1|c1=c1",
                FfmpegArguments.BuildPanFilter(0, false));
        }

        [TestMethod]
        public void Pan_左声道原唱时原唱取c0()
        {
            Assert.AreEqual("pan=stereo|c0=c0|c1=c0",
                FfmpegArguments.BuildPanFilter(1, true));
        }

        [TestMethod]
        public void Pan_accomp为2时声道对调()
        {
            // accomp == 2 → 左声道是伴奏，右声道是原唱（docs §7.5）。
            Assert.AreEqual("pan=stereo|c0=c1|c1=c1",
                FfmpegArguments.BuildPanFilter(2, true));
            Assert.AreEqual("pan=stereo|c0=c0|c1=c0",
                FfmpegArguments.BuildPanFilter(2, false));
        }

        [TestMethod]
        public void Pan_两个输出声道都恰好被赋值一次()
        {
            // pan 滤镜的语义是「输出声道 = 输入声道」，漏赋值的那一路 gain 为 0（静音）。
            var filter = FfmpegArguments.BuildPanFilter(0, false);

            StringAssert.Contains(filter, "|c0=");
            StringAssert.Contains(filter, "|c1=");
            Assert.AreEqual(1, CountOf(filter, "|c0="), "c0 只能被赋值一次");
            Assert.AreEqual(1, CountOf(filter, "|c1="), "c1 只能被赋值一次");

            // 回归：早期版本写成 "c0=c1|c0=c1" —— 同一个声道赋值两次，
            // c1 从未被赋值，产物右声道恒为静音。
            Assert.IsFalse(filter.Contains("|c0=c1|c0="), "同一个输出声道不能被赋值两次");
        }

        private static int CountOf(string text, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }

        [TestMethod]
        public void 码率_夹在64到320之间()
        {
            Assert.AreEqual(64, FfmpegArguments.ClampBitrate(0));
            Assert.AreEqual(64, FfmpegArguments.ClampBitrate(-128));
            Assert.AreEqual(320, FfmpegArguments.ClampBitrate(9999));
            Assert.AreEqual(192, FfmpegArguments.ClampBitrate(192));
        }

        [TestMethod]
        public void 引用_结尾反斜杠要翻倍()
        {
            // MSVCRT 规则：结尾的 N 个反斜杠要写成 2N 个，否则会把闭合引号吃掉，
            // 后面的参数会被并进这个路径里。
            Assert.AreEqual("\"C:\\\\Music\\\\\"", FfmpegArguments.Quote("C:\\Music\\"));
            Assert.AreEqual("\"C:\\\\Music\"", FfmpegArguments.Quote("C:\\Music"));
            Assert.AreEqual("\"a\\\\b\"", FfmpegArguments.Quote("a\\b"));
        }

        [TestMethod]
        public void 引用_内嵌引号要转义()
        {
            Assert.AreEqual("\"say \\\"hi\\\"\"", FfmpegArguments.Quote("say \"hi\""));
            Assert.AreEqual("\"\"", FfmpegArguments.Quote(null));
            Assert.AreEqual("\"\"", FfmpegArguments.Quote(string.Empty));
        }

        [TestMethod]
        public void 引用_中文与方括号原样保留()
        {
            var name = "发如雪-周杰伦 [123456].mp4";
            Assert.AreEqual("\"" + name + "\"", FfmpegArguments.Quote(name));
        }

        [TestMethod]
        public void 探测参数_只给输入不给输出()
        {
            var args = FfmpegArguments.BuildProbeArguments("in.ts");

            Assert.AreEqual("-hide_banner -i \"in.ts\"", args);
        }

        [TestMethod]
        public void 转MP4_双音轨保留两条()
        {
            var args = FfmpegArguments.BuildConvertToMp4Arguments(
                "in.ts", "out.mp4", VocalOption.KeepBoth, 2, 192);

            StringAssert.Contains(args, "-map 0:v:0");
            StringAssert.Contains(args, "-map 0:a:0 -map 0:a:1");
            StringAssert.Contains(args, "-c:v copy");

            // 回归 docs §坑1：TS 里的 mp2 音轨不能 -c copy 进 MP4，
            // 否则会被标成 mp3，多数播放器解不出来 → "画面正常但完全没声音"。
            StringAssert.Contains(args, "-c:a aac");
            Assert.IsFalse(args.Contains("-c:a copy"), "音频绝不能直接 copy");

            StringAssert.Contains(args, "-b:a 192k");
            StringAssert.Contains(args, "-ac 2");
            StringAssert.Contains(args, "-movflags +faststart");
        }

        [TestMethod]
        public void 转MP4_只要伴奏时映射第二条音轨()
        {
            var args = FfmpegArguments.BuildConvertToMp4Arguments(
                "in.ts", "out.mp4", VocalOption.AccompanimentOnly, 2, 128);

            StringAssert.Contains(args, "-map 0:a:1");
            Assert.IsFalse(args.Contains("-map 0:a:0"), "只要伴奏时不该带上原唱轨");
        }

        [TestMethod]
        public void 转MP4_只要原唱时映射第一条音轨()
        {
            var args = FfmpegArguments.BuildConvertToMp4Arguments(
                "in.ts", "out.mp4", VocalOption.OriginalOnly, 2, 128);

            StringAssert.Contains(args, "-map 0:a:0");
            Assert.IsFalse(args.Contains("-map 0:a:1"), "只要原唱时不该带上伴奏轨");
        }

        [TestMethod]
        public void 转MP4_无音轨时不能映射音频()
        {
            // 映射一个不存在的 0:a:0 会让 ffmpeg 整体失败。
            var args = FfmpegArguments.BuildConvertToMp4Arguments(
                "in.ts", "out.mp4", VocalOption.KeepBoth, 0, 192);

            StringAssert.Contains(args, "-an");
            Assert.IsFalse(args.Contains("-map 0:a:"), "0 条音轨时不能出现音频映射");
            Assert.IsFalse(args.Contains("-c:a aac"), "0 条音轨时不该重编码音频");
        }

        [TestMethod]
        public void 转MP4_单音轨时只映射一条()
        {
            var args = FfmpegArguments.BuildConvertToMp4Arguments(
                "in.ts", "out.mp4", VocalOption.SplitBoth, 1, 192);

            StringAssert.Contains(args, "-map 0:a:0");
            Assert.IsFalse(args.Contains("-map 0:a:1"), "只有一条音轨时不能映射第二条");
        }

        [TestMethod]
        public void 提取音轨_按索引映射并夹码率()
        {
            var args = FfmpegArguments.BuildExtractTrackArguments("in.ts", 1, "out.mp4", 999);

            StringAssert.Contains(args, "-map 0:a:1");
            StringAssert.Contains(args, "-b:a 320k");   // 999 被夹到上限
        }

        [TestMethod]
        public void 提取声道_必须带pan滤镜()
        {
            var args = FfmpegArguments.BuildExtractChannelArguments(
                "in.ts", "pan=stereo|c0=c1|c1=c1", "out.mp4", 192);

            StringAssert.Contains(args, "-af \"pan=stereo|c0=c1|c1=c1\"");
            StringAssert.Contains(args, "-map 0:a:0");
        }

        [TestMethod]
        public void 提取声道_空滤镜直接拒绝()
        {
            // 空滤镜会让 ffmpeg 产出一个双声道全静音的文件，比报错更难查。
            Assert.ThrowsException<ArgumentException>(() =>
                FfmpegArguments.BuildExtractChannelArguments("in.ts", "  ", "out.mp4", 192));
        }

        [TestMethod]
        public void 预览参数_裁剪时长并分片()
        {
            var args = FfmpegArguments.BuildPreviewArguments(
                "in.ts", "out.mp4", 60, VocalOption.KeepBoth, 2);

            StringAssert.Contains(args, "-t 60");
            StringAssert.Contains(args, "+faststart+frag_keyframe+empty_moov");
        }

        [TestMethod]
        public void 预览参数_时长为0表示不裁剪()
        {
            var args = FfmpegArguments.BuildPreviewArguments(
                "in.ts", "out.mp4", 0, VocalOption.KeepBoth, 1);

            Assert.IsFalse(args.Contains("-t "), "0 秒表示完整转码，不该出现 -t");
        }

        [TestMethod]
        public void 预览参数_伴奏优先取第二条音轨()
        {
            var args = FfmpegArguments.BuildPreviewArguments(
                "in.ts", "out.mp4", 30, VocalOption.AccompanimentOnly, 2);

            StringAssert.Contains(args, "-map 0:a:1?");
        }
    }
}
