using KtvDownloader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KtvDownloader.Tests
{
    /// <summary>
    /// 老式单音轨卡拉OK的声道规则（docs §7.5）。
    /// <para>
    /// 这条规则同时被两条执行路径用到：下载走 ffmpeg 的 pan 滤镜，
    /// 预览走 libVLC 的声道切换。两边必须给出**同一个**答案，
    /// 否则会出现"预览里听到的是原唱、下载下来变成伴奏"。
    /// 所以这里成对断言，任何一边单独改都会红。
    /// </para>
    /// </summary>
    [TestClass]
    public class VocalChannelRuleTests
    {
        [TestMethod]
        public void accomp为2时原唱在右声道()
        {
            Assert.IsTrue(VocalChannelRule.OriginalIsRightChannel(2));
            Assert.IsFalse(VocalChannelRule.OriginalIsRightChannel(1));
            Assert.IsFalse(VocalChannelRule.OriginalIsRightChannel(0));
            Assert.IsFalse(VocalChannelRule.OriginalIsRightChannel(9));
        }

        [TestMethod]
        public void 声道_accomp为1或未知时原唱取左()
        {
            Assert.AreEqual(VocalChannelRule.ChannelLeft, VocalChannelRule.ResolveChannel(1, true));
            Assert.AreEqual(VocalChannelRule.ChannelRight, VocalChannelRule.ResolveChannel(1, false));

            // accomp == 0 表示曲库没标注，按「左=原唱」猜（多数老歌如此）。
            Assert.AreEqual(VocalChannelRule.ChannelLeft, VocalChannelRule.ResolveChannel(0, true));
            Assert.AreEqual(VocalChannelRule.ChannelRight, VocalChannelRule.ResolveChannel(0, false));
        }

        [TestMethod]
        public void 声道_accomp为2时原唱取右()
        {
            Assert.AreEqual(VocalChannelRule.ChannelRight, VocalChannelRule.ResolveChannel(2, true));
            Assert.AreEqual(VocalChannelRule.ChannelLeft, VocalChannelRule.ResolveChannel(2, false));
        }

        [TestMethod]
        public void 声道常量_与libVLC的audio_channel_t一致()
        {
            // libvlc_AudioChannel_Stereo=1, Left=3, Right=4。
            // 这几个数字写错不会报错，只会安静地切到错误的声道。
            Assert.AreEqual(1, VocalChannelRule.ChannelStereo);
            Assert.AreEqual(3, VocalChannelRule.ChannelLeft);
            Assert.AreEqual(4, VocalChannelRule.ChannelRight);
        }

        [TestMethod]
        public void ffmpeg与libVLC_两条路径结论必须一致()
        {
            foreach (var accomp in new[] { 0, 1, 2, 3 })
            {
                foreach (var wantOriginal in new[] { true, false })
                {
                    var panChannel = VocalChannelRule.ResolveSourceChannel(accomp, wantOriginal);
                    var vlcChannel = VocalChannelRule.ResolveChannel(accomp, wantOriginal);

                    // ffmpeg 的 c0/c1 与 libVLC 的 Left/Right 必须指向同一侧。
                    var expectedVlc = panChannel == "c0"
                        ? VocalChannelRule.ChannelLeft
                        : VocalChannelRule.ChannelRight;

                    Assert.AreEqual(expectedVlc, vlcChannel,
                        string.Format("accomp={0} wantOriginal={1} 时两条路径不一致：ffmpeg={2} libVLC={3}",
                            accomp, wantOriginal, panChannel, vlcChannel));
                }
            }
        }

        [TestMethod]
        public void ffmpeg声道_c0是左c1是右()
        {
            // pan 滤镜里 c0 = 左声道、c1 = 右声道，别搞反。
            Assert.AreEqual("c0", VocalChannelRule.ResolveSourceChannel(1, true));   // 左=原唱
            Assert.AreEqual("c1", VocalChannelRule.ResolveSourceChannel(1, false));  // 右=伴奏
            Assert.AreEqual("c1", VocalChannelRule.ResolveSourceChannel(2, true));   // 右=原唱
            Assert.AreEqual("c0", VocalChannelRule.ResolveSourceChannel(2, false));  // 左=伴奏
        }

        [TestMethod]
        public void pan滤镜_与声道规则一致()
        {
            Assert.AreEqual("pan=stereo|c0=c0|c1=c0", FfmpegArguments.BuildPanFilter(1, true));
            Assert.AreEqual("pan=stereo|c0=c1|c1=c1", FfmpegArguments.BuildPanFilter(1, false));
            Assert.AreEqual("pan=stereo|c0=c1|c1=c1", FfmpegArguments.BuildPanFilter(2, true));
            Assert.AreEqual("pan=stereo|c0=c0|c1=c0", FfmpegArguments.BuildPanFilter(2, false));
        }

        [TestMethod]
        public void accomp是否可信_0和越界值都算未知()
        {
            Assert.IsTrue(VocalChannelRule.IsAccompKnown(1));
            Assert.IsTrue(VocalChannelRule.IsAccompKnown(2));
            Assert.IsFalse(VocalChannelRule.IsAccompKnown(0));
            Assert.IsFalse(VocalChannelRule.IsAccompKnown(3));
            Assert.IsFalse(VocalChannelRule.IsAccompKnown(-1));
        }
    }
}
