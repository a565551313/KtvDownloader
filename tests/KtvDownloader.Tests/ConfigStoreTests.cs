using System;
using System.IO;
using System.Text;
using KtvDownloader.Infrastructure.Config;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KtvDownloader.Tests
{
    /// <summary>
    /// 配置读写。
    /// <para>
    /// 重点是两件事：往返一致，以及**写入是原子的** ——
    /// 配置文件写坏了会让用户的下载目录、曲库路径全部静默回落到默认值，
    /// 而且界面上完全看不出来。
    /// </para>
    /// </summary>
    [TestClass]
    public class ConfigStoreTests
    {
        private static string NewPath(TempDirectory temp)
        {
            return temp.Combine("config.json");
        }

        [TestMethod]
        public void 往返_所有字段都能读回来()
        {
            using (var temp = new TempDirectory())
            {
                var store = new ConfigStore(NewPath(temp), NullLogger.Instance);

                var config = AppConfig.CreateDefault();
                config.DatabasePath = @"D:\Ktv\muse.db";
                config.OutputDirectory = @"D:\Music\新歌";
                config.FfmpegPath = @"D:\tools\ffmpeg.exe";
                config.MaxConcurrency = 5;
                config.DefaultResolution = "1080";
                config.DefaultVocal = VocalOption.AccompanimentOnly;
                config.ConvertToMp4 = false;
                config.PageSize = 120;
                config.PreviewDurationSeconds = 45;
                config.LogLevel = "Warn";

                Assert.IsTrue(store.Save(config), store.LastError);

                var loaded = store.Load();

                Assert.AreEqual(@"D:\Ktv\muse.db", loaded.DatabasePath);
                Assert.AreEqual(@"D:\Music\新歌", loaded.OutputDirectory);
                Assert.AreEqual(@"D:\tools\ffmpeg.exe", loaded.FfmpegPath);
                Assert.AreEqual(5, loaded.MaxConcurrency);
                Assert.AreEqual("1080", loaded.DefaultResolution);
                Assert.AreEqual(VocalOption.AccompanimentOnly, loaded.DefaultVocal);
                Assert.IsFalse(loaded.ConvertToMp4);
                Assert.AreEqual(120, loaded.PageSize);
                Assert.AreEqual(45, loaded.PreviewDurationSeconds);
                Assert.AreEqual("Warn", loaded.LogLevel);
            }
        }

        [TestMethod]
        public void 往返_路径里的引号与反斜杠不会破坏JSON()
        {
            using (var temp = new TempDirectory())
            {
                var store = new ConfigStore(NewPath(temp), NullLogger.Instance);

                var config = AppConfig.CreateDefault();
                config.OutputDirectory = @"D:\Music ""精选""\2024\";

                Assert.IsTrue(store.Save(config), store.LastError);

                // 注意 Normalize() 只 TrimEnd 了空白，结尾反斜杠会被保留，
                // 而它正好是 JSON 转义最容易出错的地方。
                var loaded = store.Load();
                Assert.IsTrue(loaded.OutputDirectory.StartsWith(@"D:\Music ""精选""", StringComparison.Ordinal),
                    "实际读回：" + loaded.OutputDirectory);
            }
        }

        [TestMethod]
        public void 保存后_不留下临时文件()
        {
            using (var temp = new TempDirectory())
            {
                var path = NewPath(temp);
                var store = new ConfigStore(path, NullLogger.Instance);

                Assert.IsTrue(store.Save(AppConfig.CreateDefault()), store.LastError);

                Assert.IsTrue(File.Exists(path));
                Assert.IsFalse(File.Exists(path + ".tmp"), "原子写入的临时文件应该已经被换掉");
            }
        }

        [TestMethod]
        public void 重复保存_第二次也走Replace分支()
        {
            using (var temp = new TempDirectory())
            {
                var store = new ConfigStore(NewPath(temp), NullLogger.Instance);

                Assert.IsTrue(store.Save(AppConfig.CreateDefault()), store.LastError);

                var second = AppConfig.CreateDefault();
                second.MaxConcurrency = 7;
                Assert.IsTrue(store.Save(second), store.LastError);

                Assert.AreEqual(7, store.Load().MaxConcurrency);
            }
        }

        [TestMethod]
        public void 加载_文件不存在时返回默认配置()
        {
            using (var temp = new TempDirectory())
            {
                var store = new ConfigStore(NewPath(temp), NullLogger.Instance);
                var config = store.Load();

                Assert.IsNotNull(config);
                Assert.IsNull(store.LastError, "文件不存在是正常情况，不该记成错误");
                Assert.AreEqual(2, config.MaxConcurrency);
            }
        }

        [TestMethod]
        public void 加载_文件损坏时回落默认并记下原因()
        {
            using (var temp = new TempDirectory())
            {
                var path = NewPath(temp);
                File.WriteAllText(path, "{ 这不是 JSON", Encoding.UTF8);

                var logger = new RecordingLogger();
                var store = new ConfigStore(path, logger);
                var config = store.Load();

                Assert.IsNotNull(config, "配置文件坏了也必须能启动");
                Assert.IsNotNull(store.LastError);
                Assert.IsTrue(logger.WarnCount > 0, "静默回落是排查噩梦，必须留一条日志");
            }
        }

        [TestMethod]
        public void 加载_空文件回落默认()
        {
            using (var temp = new TempDirectory())
            {
                var path = NewPath(temp);
                File.WriteAllText(path, string.Empty, Encoding.UTF8);

                var store = new ConfigStore(path, NullLogger.Instance);

                Assert.IsNotNull(store.Load());
                Assert.IsNotNull(store.LastError);
            }
        }

        [TestMethod]
        public void 加载_缺失字段用默认值补()
        {
            using (var temp = new TempDirectory())
            {
                var path = NewPath(temp);
                File.WriteAllText(path, "{\"MaxConcurrency\":4}", Encoding.UTF8);

                var config = new ConfigStore(path, NullLogger.Instance).Load();

                Assert.AreEqual(4, config.MaxConcurrency);
                Assert.AreEqual("720", config.DefaultResolution);
                Assert.AreEqual(50, config.PageSize);
                Assert.AreEqual(60, config.PreviewDurationSeconds);
            }
        }

        [TestMethod]
        public void 加载_非法枚举值回落默认()
        {
            using (var temp = new TempDirectory())
            {
                var path = NewPath(temp);

                // 配置文件是人手改的，写个 99 完全可能。
                // 早期版本直接 (VocalOption)99 强转，非法值会一路传到 ffmpeg 参数拼装才炸。
                File.WriteAllText(path, "{\"DefaultVocal\":99}", Encoding.UTF8);

                var config = new ConfigStore(path, NullLogger.Instance).Load();

                Assert.AreEqual(VocalOption.KeepBoth, config.DefaultVocal);
                Assert.IsTrue(Enum.IsDefined(typeof(VocalOption), (int)config.DefaultVocal));
            }
        }

        [TestMethod]
        public void 加载_布尔值认true与false()
        {
            using (var temp = new TempDirectory())
            {
                var path = NewPath(temp);
                File.WriteAllText(path, "{\"ConvertToMp4\":false}", Encoding.UTF8);
                Assert.IsFalse(new ConfigStore(path, NullLogger.Instance).Load().ConvertToMp4);

                File.WriteAllText(path, "{\"ConvertToMp4\":true}", Encoding.UTF8);
                Assert.IsTrue(new ConfigStore(path, NullLogger.Instance).Load().ConvertToMp4);
            }
        }

        [TestMethod]
        public void 归一化_并发数夹在1到8()
        {
            var config = AppConfig.CreateDefault();

            config.MaxConcurrency = 0;
            config.Normalize();
            Assert.AreEqual(1, config.MaxConcurrency);

            config.MaxConcurrency = 99;
            config.Normalize();
            Assert.AreEqual(8, config.MaxConcurrency);
        }

        [TestMethod]
        public void 归一化_每页条数夹在10到500()
        {
            var config = AppConfig.CreateDefault();

            config.PageSize = 1;
            config.Normalize();
            Assert.AreEqual(10, config.PageSize);

            config.PageSize = 99999;
            config.Normalize();
            Assert.AreEqual(500, config.PageSize);
        }

        [TestMethod]
        public void 归一化_预览时长为0表示完整下载()
        {
            var config = AppConfig.CreateDefault();
            config.PreviewDurationSeconds = 0;
            config.Normalize();

            Assert.AreEqual(0, config.PreviewDurationSeconds);
        }

        [TestMethod]
        public void 归一化_预览时长1到9秒会被抬到10秒()
        {
            // 回归：早期只挡了 <0 和 >600，1~9 能直接穿过去，
            // 而这么短的切片常常凑不齐一个完整的 TS 关键帧 → VLC 黑屏。
            var config = AppConfig.CreateDefault();

            config.PreviewDurationSeconds = 1;
            config.Normalize();
            Assert.AreEqual(10, config.PreviewDurationSeconds);

            config.PreviewDurationSeconds = 9;
            config.Normalize();
            Assert.AreEqual(10, config.PreviewDurationSeconds);

            config.PreviewDurationSeconds = 10;
            config.Normalize();
            Assert.AreEqual(10, config.PreviewDurationSeconds);
        }

        [TestMethod]
        public void 归一化_预览时长上限600秒()
        {
            var config = AppConfig.CreateDefault();
            config.PreviewDurationSeconds = 99999;
            config.Normalize();

            Assert.AreEqual(600, config.PreviewDurationSeconds);
        }

        [TestMethod]
        public void 归一化_空输出目录回落到视频目录()
        {
            var config = AppConfig.CreateDefault();
            config.OutputDirectory = "   ";
            config.Normalize();

            Assert.IsFalse(string.IsNullOrWhiteSpace(config.OutputDirectory));
        }

        [TestMethod]
        public void 归一化_空日志级别回落Debug()
        {
            var config = AppConfig.CreateDefault();
            config.LogLevel = null;
            config.Normalize();

            Assert.AreEqual("Debug", config.LogLevel);
        }

        [TestMethod]
        public void 保存_null配置返回false而不是抛异常()
        {
            using (var temp = new TempDirectory())
            {
                var store = new ConfigStore(NewPath(temp), NullLogger.Instance);

                Assert.IsFalse(store.Save(null));
                Assert.IsNotNull(store.LastError);
            }
        }

        [TestMethod]
        public void 保存_目录不存在时自动创建()
        {
            using (var temp = new TempDirectory())
            {
                var nested = Path.Combine(temp.Path, "a", "b", "config.json");
                var store = new ConfigStore(nested, NullLogger.Instance);

                Assert.IsTrue(store.Save(AppConfig.CreateDefault()), store.LastError);
                Assert.IsTrue(File.Exists(nested));
            }
        }
    }

    /// <summary>日志级别解析。</summary>
    [TestClass]
    public class LogLevelParserTests
    {
        [TestMethod]
        public void 解析_认得四种级别与常见别名()
        {
            Assert.AreEqual(LogLevel.Debug, LogLevelParser.Parse("debug", LogLevel.Info));
            Assert.AreEqual(LogLevel.Debug, LogLevelParser.Parse("TRACE", LogLevel.Info));
            Assert.AreEqual(LogLevel.Info, LogLevelParser.Parse(" Info ", LogLevel.Debug));
            Assert.AreEqual(LogLevel.Warn, LogLevelParser.Parse("warning", LogLevel.Debug));
            Assert.AreEqual(LogLevel.Warn, LogLevelParser.Parse("WARN", LogLevel.Debug));
            Assert.AreEqual(LogLevel.Error, LogLevelParser.Parse("err", LogLevel.Debug));
        }

        [TestMethod]
        public void 解析_认不出时返回fallback()
        {
            Assert.AreEqual(LogLevel.Info, LogLevelParser.Parse("verbose", LogLevel.Info));
            Assert.AreEqual(LogLevel.Info, LogLevelParser.Parse(null, LogLevel.Info));
            Assert.AreEqual(LogLevel.Info, LogLevelParser.Parse("  ", LogLevel.Info));
        }

        [TestMethod]
        public void 反向转换_与解析互逆()
        {
            foreach (var level in new[] { LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error })
            {
                var text = LogLevelParser.ToString(level);
                Assert.AreEqual(level, LogLevelParser.Parse(text, LogLevel.Debug));
            }
        }
    }
}
