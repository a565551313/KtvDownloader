using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Models;
using KtvDownloader.Services.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KtvDownloader.Tests
{
    /// <summary>
    /// 把日志收进内存的假 logger。测试里既不想写文件，也想断言"确实记了一条 WARN"。
    /// </summary>
    internal sealed class RecordingLogger : ILogger
    {
        private readonly List<string> _lines = new List<string>();
        private readonly object _sync = new object();

        public IReadOnlyList<string> Lines
        {
            get { lock (_sync) return _lines.ToArray(); }
        }

        public int WarnCount
        {
            get { lock (_sync) return CountOf("WARN"); }
        }

        public int ErrorCount
        {
            get { lock (_sync) return CountOf("ERROR"); }
        }

        private int CountOf(string prefix)
        {
            var count = 0;
            foreach (var line in _lines)
                if (line.StartsWith(prefix, StringComparison.Ordinal)) count++;
            return count;
        }

        public void Debug(string message) { Add("DEBUG", message); }
        public void Info(string message) { Add("INFO", message); }
        public void Warn(string message) { Add("WARN", message); }
        public void Error(string message, Exception exception = null)
        {
            Add("ERROR", message + (exception == null ? string.Empty : " | " + exception.Message));
        }

        private void Add(string level, string message)
        {
            lock (_sync) _lines.Add(level + " " + message);
        }
    }

    /// <summary>什么都不记的 logger。</summary>
    internal sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new NullLogger();

        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception exception = null) { }
    }

    /// <summary>
    /// 可编排的假下载服务。用来测 <c>DownloadQueue</c> 的排队、并发与计数，
    /// 完全不碰网络与磁盘。
    /// </summary>
    internal sealed class FakeDownloadService : IDownloadService
    {
        /// <summary>每个任务的处理逻辑。默认直接标记完成。</summary>
        public Func<DownloadTask, CancellationToken, Task<DownloadTask>> Handler { get; set; }

        private int _maxConcurrency;

        public int MaxConcurrency
        {
            get { return _maxConcurrency; }
            set { _maxConcurrency = Math.Max(1, Math.Min(8, value)); }
        }

        /// <summary>观测到的最大同时在跑的任务数。</summary>
        public int ObservedPeakConcurrency;

        private int _inFlight;

        public int StartedCount;

        public async Task<DownloadTask> ExecuteAsync(
            DownloadTask task,
            DownloadOptions options,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref StartedCount);

            var current = Interlocked.Increment(ref _inFlight);
            UpdatePeak(current);

            try
            {
                var handler = Handler;
                if (handler != null)
                    return await handler(task, cancellationToken).ConfigureAwait(false);

                task.State = DownloadState.Completed;
                task.StatusText = "已完成";
                return task;
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        private void UpdatePeak(int current)
        {
            int peak;
            do
            {
                peak = Volatile.Read(ref ObservedPeakConcurrency);
                if (current <= peak) return;
            }
            while (Interlocked.CompareExchange(ref ObservedPeakConcurrency, current, peak) != peak);
        }
    }

    /// <summary>
    /// 一个用完即删的临时目录。
    /// </summary>
    internal sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ktvdl-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; private set; }

        public string Combine(string fileName)
        {
            return System.IO.Path.Combine(Path, fileName);
        }

        public string WriteFile(string fileName, byte[] content)
        {
            var full = Combine(fileName);
            File.WriteAllBytes(full, content);
            return full;
        }

        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
            catch { /* 临时目录删不掉就算了，不影响测试结论 */ }
        }
    }

    internal static class TestSongs
    {
        public static Song Create(
            string name = "发如雪",
            string singer = "周杰伦",
            string musicNo = "123456",
            int accomp = 0)
        {
            return new Song
            {
                Id = musicNo,
                MusicNo = musicNo,
                Name = name,
                NameTrim = name,
                Singer = singer,
                Language = "国语",
                Accomp = accomp,
                FileName = musicNo + ".ts"
            };
        }
    }

    /// <summary>轮询等待一个条件成立，超时就让测试失败。</summary>
    internal static class Wait
    {
        public static void Until(Func<bool> condition, int timeoutMilliseconds, string message)
        {
            var deadline = Environment.TickCount + timeoutMilliseconds;
            while (Environment.TickCount < deadline)
            {
                if (condition()) return;
                Thread.Sleep(20);
            }

            Assert.IsTrue(condition(), "等待超时：" + message);
        }
    }
}
