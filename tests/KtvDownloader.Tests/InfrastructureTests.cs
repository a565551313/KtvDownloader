using System;
using System.IO;
using System.Linq;
using System.Threading;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KtvDownloader.Tests
{
    /// <summary>
    /// 歌手名归一化（批量补名的纯函数部分）。
    /// </summary>
    [TestClass]
    public class SingerNameLookupTests
    {
        [TestMethod]
        public void 归一化_去掉空白与重复分隔符()
        {
            Assert.AreEqual("周杰伦", SingerNameLookup.NormalizeSinger(" 周杰伦 "));
            Assert.AreEqual("A/B", SingerNameLookup.NormalizeSinger("A//B"));
            Assert.AreEqual("A/B", SingerNameLookup.NormalizeSinger(" A / B "));
            Assert.AreEqual("A/B", SingerNameLookup.NormalizeSinger("A、B"));
        }

        [TestMethod]
        public void 归一化_去掉重复歌手()
        {
            Assert.AreEqual("A/B", SingerNameLookup.NormalizeSinger("A/B/A"));
        }

        [TestMethod]
        public void 归一化_空输入返回空串()
        {
            Assert.AreEqual(string.Empty, SingerNameLookup.NormalizeSinger(null));
            Assert.AreEqual(string.Empty, SingerNameLookup.NormalizeSinger("   "));
            Assert.AreEqual(string.Empty, SingerNameLookup.NormalizeSinger("//"));
        }

        [TestMethod]
        public void 归一化_单人不受影响()
        {
            Assert.AreEqual("王菲", SingerNameLookup.NormalizeSinger("王菲"));
        }
    }

    /// <summary>
    /// 文件日志：级别过滤、缓冲刷盘、按天切分。
    /// </summary>
    [TestClass]
    public class FileLoggerTests
    {
        private static string TodayFile(string directory)
        {
            return Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd") + ".log");
        }

        private static string ReadAll(string directory)
        {
            var file = TodayFile(directory);
            return File.Exists(file) ? File.ReadAllText(file) : string.Empty;
        }

        [TestMethod]
        public void 级别过滤_低于门槛的直接丢弃()
        {
            using (var temp = new TempDirectory())
            {
                using (var logger = new FileLogger(temp.Path, 14, LogLevel.Warn))
                {
                    logger.Debug("调试信息");
                    logger.Info("普通信息");
                    logger.Warn("警告信息");
                    logger.Error("错误信息");
                    logger.Flush();
                }

                var text = ReadAll(temp.Path);

                Assert.IsFalse(text.Contains("调试信息"), "Debug 应该被过滤掉");
                Assert.IsFalse(text.Contains("普通信息"), "Info 应该被过滤掉");
                StringAssert.Contains(text, "警告信息");
                StringAssert.Contains(text, "错误信息");
            }
        }

        [TestMethod]
        public void 默认级别_什么都记()
        {
            using (var temp = new TempDirectory())
            {
                using (var logger = new FileLogger(temp.Path))
                {
                    logger.Debug("调试信息");
                    logger.Info("普通信息");
                    logger.Flush();
                }

                var text = ReadAll(temp.Path);

                StringAssert.Contains(text, "调试信息");
                StringAssert.Contains(text, "普通信息");
                StringAssert.Contains(text, "DEBUG");
                StringAssert.Contains(text, "INFO ");
            }
        }

        [TestMethod]
        public void 错误级别_不缓冲立刻落盘()
        {
            using (var temp = new TempDirectory())
            {
                // 刻意不 Dispose：崩溃时往往来不及走清理流程，
                // ERROR/WARN 必须自己刷盘，否则最关键的现场正好丢掉。
                var logger = new FileLogger(temp.Path, 14, LogLevel.Debug);
                logger.Error("崩溃前最后一条", new InvalidOperationException("boom"));

                var text = ReadAll(temp.Path);

                StringAssert.Contains(text, "崩溃前最后一条");
                StringAssert.Contains(text, "boom");
                StringAssert.Contains(text, "ERROR");
            }
        }

        [TestMethod]
        public void 缓冲_攒够阈值才落盘但不会丢()
        {
            using (var temp = new TempDirectory())
            {
                using (var logger = new FileLogger(temp.Path, 14, LogLevel.Debug))
                {
                    for (var i = 0; i < 200; i++) logger.Info("第 " + i + " 行");

                    // 200 行 > 64 行阈值，中途已经刷过盘了。
                    var partial = ReadAll(temp.Path);
                    StringAssert.Contains(partial, "第 0 行");

                    logger.Flush();
                }

                var text = ReadAll(temp.Path);
                StringAssert.Contains(text, "第 199 行");
            }
        }

        [TestMethod]
        public void Dispose_把缓冲区全部写出去()
        {
            using (var temp = new TempDirectory())
            {
                var logger = new FileLogger(temp.Path, 14, LogLevel.Debug);
                logger.Info("还没刷盘的一条");
                logger.Dispose();

                StringAssert.Contains(ReadAll(temp.Path), "还没刷盘的一条");
            }
        }

        [TestMethod]
        public void 多线程_并发写不丢行不串行()
        {
            using (var temp = new TempDirectory())
            {
                using (var logger = new FileLogger(temp.Path, 14, LogLevel.Debug))
                {
                    var threads = Enumerable.Range(0, 8).Select(t => new Thread(() =>
                    {
                        for (var i = 0; i < 50; i++) logger.Info("线程" + t + " 行" + i);
                    })).ToArray();

                    foreach (var thread in threads) thread.Start();
                    foreach (var thread in threads) thread.Join();

                    logger.Flush();
                }

                var lines = ReadAll(temp.Path)
                    .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                Assert.AreEqual(8 * 50, lines.Length, "并发写丢了日志行");
            }
        }

        [TestMethod]
        public void 两个实例同时写同一个文件不会互相踢掉()
        {
            // 回归：Program.cs 早期在异常兜底里又 new 了一个 FileLogger，
            // 两个实例抢同一个 .log 会让"崩溃时的最后一条日志"写不进去。
            using (var temp = new TempDirectory())
            {
                using (var first = new FileLogger(temp.Path, 14, LogLevel.Debug))
                using (var second = new FileLogger(temp.Path, 14, LogLevel.Debug))
                {
                    first.Info("来自第一个实例");
                    first.Flush();

                    second.Warn("来自第二个实例");
                    second.Flush();
                }

                var text = ReadAll(temp.Path);
                StringAssert.Contains(text, "来自第一个实例");
                StringAssert.Contains(text, "来自第二个实例");
            }
        }

        [TestMethod]
        public void 清理_只删过期日志()
        {
            using (var temp = new TempDirectory())
            {
                var old = temp.WriteFile("20000101.log", new byte[] { 1 });
                File.SetLastWriteTime(old, DateTime.Now.AddDays(-60));

                var recent = temp.WriteFile("20000102.log", new byte[] { 1 });
                File.SetLastWriteTime(recent, DateTime.Now.AddDays(-3));

                using (var logger = new FileLogger(temp.Path, 14, LogLevel.Debug))
                {
                    logger.CleanupOldLogs();
                }

                Assert.IsFalse(File.Exists(old), "超过保留天数的日志应该被删掉");
                Assert.IsTrue(File.Exists(recent), "保留期内的日志不该被删");
            }
        }

        [TestMethod]
        public void 目录不可用时_日志失败不影响主流程()
        {
            // 日志绝不能把程序带崩：目录建不出来就退化成只写 Debug 输出，调用方感知不到异常。
            // '<' '>' '|' 在 Windows 上是非法路径字符，CreateDirectory 一定失败。
            var logger = new FileLogger(
                Path.Combine(Path.GetTempPath(), "ktvdl<>|invalid-" + Guid.NewGuid().ToString("N")),
                14, LogLevel.Debug);

            logger.Info("随便写点什么");
            logger.Warn("再写一条");
            logger.Flush();
            logger.Dispose();
        }

        [TestMethod]
        public void 保留天数非法时回落到14天()
        {
            using (var temp = new TempDirectory())
            {
                using (var logger = new FileLogger(temp.Path, 0, LogLevel.Debug))
                {
                    logger.Info("一条");
                    logger.Flush();
                }

                StringAssert.Contains(ReadAll(temp.Path), "一条");
            }
        }
    }
}
