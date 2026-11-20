using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KtvDownloader.Infrastructure.Extensions;
using KtvDownloader.Models;
using KtvDownloader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KtvDownloader.Tests
{
    /// <summary>
    /// 下载产物的命名规则。
    /// <para>
    /// 写入（DownloadService）与反查（MainForm.FindLocalFile）必须共用这一套规则，
    /// 否则就会出现"明明下载过，预览时却又下一遍"。
    /// </para>
    /// </summary>
    [TestClass]
    public class SongFileNamingTests
    {
        [TestMethod]
        public void 基础文件名_歌名歌手歌号()
        {
            var song = TestSongs.Create("发如雪", "周杰伦", "123456");

            Assert.AreEqual("发如雪-周杰伦 [123456]", SongFileNaming.BuildBaseFileName(song));
        }

        [TestMethod]
        public void 基础文件名_没有歌手时不加连字符()
        {
            var song = TestSongs.Create("发如雪", null, "123456");

            Assert.AreEqual("发如雪 [123456]", SongFileNaming.BuildBaseFileName(song));
        }

        [TestMethod]
        public void 基础文件名_没有歌号时不加方括号()
        {
            var song = TestSongs.Create("发如雪", "周杰伦", null);

            Assert.AreEqual("发如雪-周杰伦", SongFileNaming.BuildBaseFileName(song));
        }

        [TestMethod]
        public void 基础文件名_歌名为空时退回NameTrim()
        {
            var song = new Song { Name = "   ", NameTrim = "发如雪", Singer = "周杰伦", MusicNo = "1" };

            Assert.AreEqual("发如雪-周杰伦 [1]", SongFileNaming.BuildBaseFileName(song));
        }

        [TestMethod]
        public void 基础文件名_null歌对象返回空串()
        {
            Assert.AreEqual(string.Empty, SongFileNaming.BuildBaseFileName(null));
        }

        [TestMethod]
        public void 基础文件名_非法字符被替换()
        {
            var song = TestSongs.Create("a/b:c*d", "e?f", "1");
            var name = SongFileNaming.BuildBaseFileName(song);

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                Assert.IsFalse(name.IndexOf(invalid) >= 0,
                    "文件名里不该出现非法字符 0x" + ((int)invalid).ToString("X2"));
            }
        }

        [TestMethod]
        public void 基础文件名_超长歌名被截断()
        {
            var song = TestSongs.Create(new string('长', 500), "周杰伦", "123456");
            var name = SongFileNaming.BuildBaseFileName(song);

            // 80（歌名部分上限）+ " [123456]"。留足余量是为了不撞 MAX_PATH。
            Assert.IsTrue(name.Length < 110, "实际长度 " + name.Length);
        }

        [TestMethod]
        public void 候选路径_覆盖三种扩展名与两种音轨后缀()
        {
            var song = TestSongs.Create();
            var candidates = SongFileNaming.EnumerateCandidatePaths(@"D:\Music", song).ToList();

            Assert.IsTrue(candidates.Count >= 7, "至少要覆盖 mp4/ts/mp3 与 -原唱/-伴奏");

            StringAssert.Contains(string.Join("|", candidates.ToArray()), ".mp4");
            StringAssert.Contains(string.Join("|", candidates.ToArray()), ".ts");

            // 必须能找到「早期版本（不带歌号）」下载好的文件。
            Assert.IsTrue(candidates.Any(p => p.EndsWith("发如雪-周杰伦.mp4", StringComparison.Ordinal)),
                "缺少旧命名（不含歌号）的候选");
        }

        [TestMethod]
        public void 候选路径_目录为空时返回空集合()
        {
            Assert.AreEqual(0, SongFileNaming.EnumerateCandidatePaths(null, TestSongs.Create()).Count());
            Assert.AreEqual(0, SongFileNaming.EnumerateCandidatePaths("  ", TestSongs.Create()).Count());
            Assert.AreEqual(0, SongFileNaming.EnumerateCandidatePaths(@"D:\Music", null).Count());
        }

        [TestMethod]
        public void 查找已下载文件_命中旧命名()
        {
            using (var temp = new TempDirectory())
            {
                var song = TestSongs.Create();

                // 旧版本下载的文件：只有「歌名-歌手」，没有歌号。
                temp.WriteFile("发如雪-周杰伦.mp4", new byte[] { 1, 2, 3 });

                var found = SongFileNaming.FindExistingFile(temp.Path, song);

                Assert.IsNotNull(found, "旧命名的文件也该被认出来，否则会重复下载");
                Assert.AreEqual(Path.Combine(temp.Path, "发如雪-周杰伦.mp4"), found);
            }
        }

        [TestMethod]
        public void 查找已下载文件_命中伴奏产物()
        {
            using (var temp = new TempDirectory())
            {
                var song = TestSongs.Create();
                temp.WriteFile("发如雪-周杰伦 [123456]" + SongFileNaming.AccompanimentSuffix + ".mp4",
                    new byte[] { 1 });

                var found = SongFileNaming.FindExistingFile(temp.Path, song);

                Assert.IsNotNull(found);
                StringAssert.Contains(found, SongFileNaming.AccompanimentSuffix);
            }
        }

        [TestMethod]
        public void 查找已下载文件_目录不存在返回null()
        {
            Assert.IsNull(SongFileNaming.FindExistingFile(
                Path.Combine(Path.GetTempPath(), "ktvdl-不存在的目录-" + Guid.NewGuid().ToString("N")),
                TestSongs.Create()));
        }

        [TestMethod]
        public void 查找已下载文件_优先当前命名()
        {
            using (var temp = new TempDirectory())
            {
                var song = TestSongs.Create();

                temp.WriteFile("发如雪-周杰伦.mp4", new byte[] { 1 });
                var current = temp.WriteFile("发如雪-周杰伦 [123456].mp4", new byte[] { 1 });

                Assert.AreEqual(current, SongFileNaming.FindExistingFile(temp.Path, song));
            }
        }
    }

    /// <summary>字符串工具。</summary>
    [TestClass]
    public class CommonExtensionsTests
    {
        [TestMethod]
        public void 安全文件名_替换全部非法字符()
        {
            var invalid = Path.GetInvalidFileNameChars();
            var input = "a" + new string(invalid) + "b";

            var result = input.ToSafeFileName(200);

            foreach (var c in invalid)
                Assert.IsFalse(result.IndexOf(c) >= 0, "非法字符没被替换: 0x" + ((int)c).ToString("X2"));

            Assert.IsTrue(result.StartsWith("a", StringComparison.Ordinal));
            Assert.IsTrue(result.EndsWith("b", StringComparison.Ordinal));
        }

        [TestMethod]
        public void 安全文件名_按长度截断()
        {
            Assert.AreEqual(10, new string('x', 500).ToSafeFileName(10).Length);
        }

        [TestMethod]
        public void 安全文件名_去掉结尾的点和空格()
        {
            // Windows 不允许文件名以点或空格结尾（会被静默去掉，导致后续找不到文件）。
            Assert.AreEqual("abc", "abc...  ".ToSafeFileName(50));
        }

        [TestMethod]
        public void 安全文件名_空输入返回空串()
        {
            Assert.AreEqual(string.Empty, ((string)null).ToSafeFileName(10));
            Assert.AreEqual(string.Empty, "".ToSafeFileName(10));
        }

        [TestMethod]
        public void HasValue_只看是否有实际内容()
        {
            Assert.IsFalse(((string)null).HasValue());
            Assert.IsFalse("   ".HasValue());
            Assert.IsTrue("a".HasValue());
        }
    }
}
