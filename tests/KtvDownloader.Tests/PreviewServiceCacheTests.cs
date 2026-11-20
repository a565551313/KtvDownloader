using System;
using KtvDownloader.Models;
using KtvDownloader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KtvDownloader.Tests
{
    /// <summary>
    /// 预览缓存的键与体积估算（<see cref="PreviewService"/> 的 internal 纯函数）。
    /// </summary>
    [TestClass]
    public class PreviewServiceCacheTests
    {
        [TestMethod]
        public void 缓存键_同一首歌同样参数必须一致()
        {
            var song = TestSongs.Create();

            var a = PreviewService.BuildCacheKey(song, 60, VocalOption.KeepBoth);
            var b = PreviewService.BuildCacheKey(TestSongs.Create(), 60, VocalOption.KeepBoth);

            Assert.AreEqual(a, b, "同歌号同参数的缓存键必须相同，否则缓存永远命中不了");
        }

        [TestMethod]
        public void 缓存键_歌号不同则不同()
        {
            var a = PreviewService.BuildCacheKey(TestSongs.Create(musicNo: "1"), 60, VocalOption.KeepBoth);
            var b = PreviewService.BuildCacheKey(TestSongs.Create(musicNo: "2"), 60, VocalOption.KeepBoth);

            Assert.AreNotEqual(a, b);
        }

        [TestMethod]
        public void 缓存键_预览时长不同则不同()
        {
            var song = TestSongs.Create();

            Assert.AreNotEqual(
                PreviewService.BuildCacheKey(song, 30, VocalOption.KeepBoth),
                PreviewService.BuildCacheKey(song, 60, VocalOption.KeepBoth),
                "30 秒的切片不能当成 60 秒的用");
        }

        [TestMethod]
        public void 缓存键_音轨选项不同则不同()
        {
            var song = TestSongs.Create();

            Assert.AreNotEqual(
                PreviewService.BuildCacheKey(song, 60, VocalOption.KeepBoth),
                PreviewService.BuildCacheKey(song, 60, VocalOption.AccompanimentOnly));
        }

        [TestMethod]
        public void 缓存键_是合法文件名且带前缀()
        {
            var key = PreviewService.BuildCacheKey(TestSongs.Create(), 60, VocalOption.KeepBoth);

            StringAssert.StartsWith(key, "pv_");
            Assert.AreEqual(3 + 16, key.Length, "前缀 + 8 字节十六进制");

            foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
                Assert.IsFalse(key.IndexOf(invalid) >= 0);
        }

        [TestMethod]
        public void 缓存键_歌号为空时不抛异常()
        {
            var song = new Song { Name = "无名", Singer = "某人" };

            var key = PreviewService.BuildCacheKey(song, 60, VocalOption.KeepBoth);

            StringAssert.StartsWith(key, "pv_");
        }

        [TestMethod]
        public void 体积估算_非正数表示完整下载()
        {
            Assert.AreEqual(0L, PreviewService.EstimateBytes(0));
            Assert.AreEqual(0L, PreviewService.EstimateBytes(-30));
        }

        [TestMethod]
        public void 体积估算_至少8MB()
        {
            // 太短的估算值会让进度条一直在 0% 附近跳，也扛不住码率偏高的源。
            Assert.AreEqual(8L * 1024 * 1024, PreviewService.EstimateBytes(1));
            Assert.AreEqual(8L * 1024 * 1024, PreviewService.EstimateBytes(10));
        }

        [TestMethod]
        public void 体积估算_按时长线性增长并留一半余量()
        {
            // 250KB/s × 60s × 1.5
            Assert.AreEqual((long)(60 * 250L * 1024 * 1.5), PreviewService.EstimateBytes(60));
            Assert.IsTrue(PreviewService.EstimateBytes(120) > PreviewService.EstimateBytes(60));
        }
    }
}
