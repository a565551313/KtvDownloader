using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Infrastructure.Extensions;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Models;
using KtvDownloader.Services.Interfaces;

namespace KtvDownloader.Services
{
    /// <summary>
    /// 媒体处理服务。封装 ffmpeg 做转码与音轨分离。
    /// <para>
    /// 关键点：TS 里的音轨是 mp2。若用 <c>-c copy</c> 塞进 MP4，容器会标成 mp3，
    /// 多数播放器解不出来 —— 表现为「画面正常但完全没声音」。
    /// 所以音频必须重编码为 AAC，视频保持流拷贝（零画质损失）。
    /// </para>
    /// <para>
    /// 参数拼装全部委托给 <see cref="FfmpegArguments"/>（纯函数，可单测），
    /// 本类只负责"找 ffmpeg"和"安全地跑一个子进程"。
    /// </para>
    /// </summary>
    public sealed class MediaService : IMediaService
    {
        /// <summary>ffmpeg 单次执行的超时。</summary>
        private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(30);

        /// <summary>ffmpeg -i 探测的超时。</summary>
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

        private static readonly Regex AudioStreamRegex =
            new Regex(@"Stream #\d+:\d+.*?: Audio:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly ILogger _logger;

        /// <summary>
        /// 用户在设置里指定的 ffmpeg 路径。
        /// <para>
        /// 不是 readonly：<see cref="UpdateFfmpegPath"/> 允许设置保存后立刻生效。
        /// 读写都在 <see cref="_probeLock"/> 内。
        /// </para>
        /// </summary>
        private string _configuredFfmpegPath;

        /// <summary>ffmpeg 路径解析与音轨数缓存的锁。</summary>
        private readonly object _probeLock = new object();

        private string _resolvedFfmpegPath;
        private bool _probed;

        /// <summary>
        /// 音轨数缓存。
        /// <para>
        /// 下载一首歌原本要跑 3 次 <c>ffmpeg -i</c>（下载流程统计一次、
        /// SplitVocal 内部一次、ConvertToMp4 内部一次），每次都是启动进程 + 最多 30 秒超时。
        /// 同一批文件是"写一次读多次"，按路径缓存后只剩一次。
        /// </para>
        /// </summary>
        private readonly ConcurrentDictionary<string, int> _trackCountCache =
            new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public MediaService(ILogger logger, string configuredFfmpegPath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuredFfmpegPath = configuredFfmpegPath;
        }

        /// <summary>
        /// 更新 ffmpeg 路径并让下一次调用重新解析。
        /// <para>
        /// 为什么要有这个方法：设置窗口改完 ffmpeg 路径，老做法是重建整套服务，
        /// 而重建 <see cref="DownloadQueue"/> 会把正在跑的任务全部 CancelAll 掉
        /// —— 用户只是改了个路径，正在下的十几首歌却没了。
        /// 有了原地更新，改路径这件事就不需要碰下载管线。
        /// </para>
        /// <para>音轨数缓存一并清空：换了 ffmpeg 版本，探测结果可能不一样。</para>
        /// </summary>
        public void UpdateFfmpegPath(string configuredFfmpegPath)
        {
            lock (_probeLock)
            {
                if (string.Equals(_configuredFfmpegPath, configuredFfmpegPath ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase))
                    return;

                _configuredFfmpegPath = configuredFfmpegPath;
                _resolvedFfmpegPath = null;
                _probed = false;
                _trackCountCache.Clear();
            }

            _logger.Info("ffmpeg 路径已更新为 " +
                         (string.IsNullOrWhiteSpace(configuredFfmpegPath) ? "（自动查找）" : configuredFfmpegPath));
        }

        public string FfmpegPath
        {
            get
            {
                EnsureFfmpegResolved();
                lock (_probeLock)
                {
                    return _resolvedFfmpegPath;
                }
            }
        }

        public bool IsFfmpegAvailable => !string.IsNullOrEmpty(FfmpegPath);

        /// <summary>
        /// 依次在以下位置找 ffmpeg：
        /// 1. 配置里指定的路径
        /// 2. exe 目录的 tools/ffmpeg.exe
        /// 3. exe 目录的 ffmpeg.exe
        /// 4. 系统 PATH
        /// </summary>
        private void EnsureFfmpegResolved()
        {
            lock (_probeLock)
            {
                if (_probed) return;
                _probed = true;

                var candidates = new List<string>();

                if (!string.IsNullOrWhiteSpace(_configuredFfmpegPath))
                    candidates.Add(_configuredFfmpegPath);

                var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDirectory))
                {
                    candidates.Add(Path.Combine(baseDirectory, "tools", "ffmpeg.exe"));
                    candidates.Add(Path.Combine(baseDirectory, "ffmpeg.exe"));
                }

                foreach (var candidate in candidates)
                {
                    try
                    {
                        if (File.Exists(candidate))
                        {
                            _resolvedFfmpegPath = Path.GetFullPath(candidate);
                            _logger.Info("使用 ffmpeg: " + _resolvedFfmpegPath);
                            return;
                        }
                    }
                    catch
                    {
                        // 继续找下一个。
                    }
                }

                // 尝试 PATH
                var fromPath = FindOnPath("ffmpeg.exe");
                if (fromPath != null)
                {
                    _resolvedFfmpegPath = fromPath;
                    _logger.Info("使用 PATH 中的 ffmpeg: " + fromPath);
                    return;
                }

                _logger.Warn("未找到 ffmpeg，转码与音轨分离功能不可用");
                _resolvedFfmpegPath = null;
            }
        }

        private static string FindOnPath(string executable)
        {
            try
            {
                var pathVariable = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrEmpty(pathVariable)) return null;

                foreach (var directory in pathVariable.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(directory)) continue;

                    try
                    {
                        var full = Path.Combine(directory.Trim(), executable);
                        if (File.Exists(full)) return Path.GetFullPath(full);
                    }
                    catch
                    {
                        // 非法路径，跳过。
                    }
                }
            }
            catch
            {
                // 忽略。
            }
            return null;
        }

        public Task<int> CountAudioTracksAsync(
            string filePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() => CountAudioTracks(filePath, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// 用 <c>ffmpeg -i</c> 读取媒体信息，数出音频轨数量。结果按路径缓存。
        /// </summary>
        public int CountAudioTracks(
            string filePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(filePath)) return 0;

            int cached;
            if (_trackCountCache.TryGetValue(filePath, out cached)) return cached;

            if (!IsFfmpegAvailable || !File.Exists(filePath)) return 0;

            var count = 0;
            var probeUsable = false;
            try
            {
                var result = Run(FfmpegArguments.BuildProbeArguments(filePath), ProbeTimeout, cancellationToken);
                if (!result.TimedOut && result.StdErr.Length > 0)
                {
                    // ffmpeg -i 不指定输出文件时退出码必然非 0，所以只看 stderr 有没有流信息。
                    count = AudioStreamRegex.Matches(result.StdErr).Count;
                    probeUsable = true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn("统计音轨失败: " + ex.Message);
            }

            // 只缓存可信结果：探测超时/无输出时不缓存，下次还有机会重试。
            if (probeUsable && !cancellationToken.IsCancellationRequested)
                _trackCountCache.TryAdd(filePath, count);

            return count;
        }

        public Task<bool> ConvertToMp4Async(
            string sourceTs,
            string outputMp4,
            VocalOption vocal,
            int audioBitrateKbps,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() =>
                ConvertToMp4(sourceTs, outputMp4, vocal, audioBitrateKbps, cancellationToken),
                cancellationToken);
        }

        private bool ConvertToMp4(
            string sourceTs,
            string outputMp4,
            VocalOption vocal,
            int audioBitrateKbps,
            CancellationToken cancellationToken)
        {
            if (!IsFfmpegAvailable)
            {
                _logger.Warn("ffmpeg 不可用，跳过转码");
                return false;
            }

            if (!File.Exists(sourceTs))
            {
                _logger.Warn("源文件不存在: " + sourceTs);
                return false;
            }

            try
            {
                var audioTracks = CountAudioTracks(sourceTs, cancellationToken);
                var arguments = FfmpegArguments.BuildConvertToMp4Arguments(
                    sourceTs, outputMp4, vocal, audioTracks, audioBitrateKbps);

                var result = Run(arguments, RunTimeout, cancellationToken);

                if (result.TimedOut)
                {
                    _logger.Warn("转 MP4 超时，已终止 ffmpeg");
                    SafeDelete(outputMp4);
                    return false;
                }

                if (result.ExitCode != 0 || !File.Exists(outputMp4))
                {
                    _logger.Warn("转 MP4 失败，exitCode=" + result.ExitCode + " " + result.Tail);
                    SafeDelete(outputMp4);
                    return false;
                }

                _logger.Info("转 MP4 完成: " + outputMp4);
                return true;
            }
            catch (OperationCanceledException)
            {
                SafeDelete(outputMp4);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error("转 MP4 异常", ex);
                SafeDelete(outputMp4);
                return false;
            }
        }

        public Task<SplitResult> SplitVocalAsync(
            string sourceTs,
            string outputDirectory,
            string baseFileName,
            VocalOption option,
            int audioBitrateKbps,
            int accomp,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() =>
                SplitVocal(sourceTs, outputDirectory, baseFileName, option,
                    audioBitrateKbps, accomp, cancellationToken),
                cancellationToken);
        }

        private SplitResult SplitVocal(
            string sourceTs,
            string outputDirectory,
            string baseFileName,
            VocalOption option,
            int audioBitrateKbps,
            int accomp,
            CancellationToken cancellationToken)
        {
            var result = new SplitResult();

            if (!IsFfmpegAvailable)
            {
                _logger.Warn("ffmpeg 不可用，无法分离音轨");
                return result;
            }

            if (!File.Exists(sourceTs)) return result;

            try
            {
                if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);

                var audioTracks = CountAudioTracks(sourceTs, cancellationToken);
                result.AudioTrackCount = audioTracks;

                if (audioTracks <= 0)
                {
                    _logger.Warn("源文件没有音轨，无法分离: " + sourceTs);
                    return result;
                }

                var safeBase = string.IsNullOrWhiteSpace(baseFileName)
                    ? Path.GetFileNameWithoutExtension(sourceTs)
                    : baseFileName.ToSafeFileName();

                var originalPath = Path.Combine(outputDirectory,
                    safeBase + SongFileNaming.OriginalSuffix + ".mp4");
                var accompanimentPath = Path.Combine(outputDirectory,
                    safeBase + SongFileNaming.AccompanimentSuffix + ".mp4");

                var wantOriginal = option == VocalOption.OriginalOnly || option == VocalOption.SplitBoth;
                var wantAccompaniment = option == VocalOption.AccompanimentOnly ||
                                        option == VocalOption.SplitBoth;

                if (audioTracks >= 2)
                {
                    // 双音轨：第 1 条=原唱，第 2 条=伴奏（官方约定）。
                    if (wantOriginal &&
                        ExtractSingleTrack(sourceTs, 0, originalPath, audioBitrateKbps, cancellationToken))
                    {
                        result.OriginalPath = originalPath;
                    }

                    if (wantAccompaniment &&
                        ExtractSingleTrack(sourceTs, 1, accompanimentPath, audioBitrateKbps, cancellationToken))
                    {
                        result.AccompanimentPath = accompanimentPath;
                    }
                }
                else
                {
                    // 单音轨：老式卡拉OK 用左右声道区分，方向由曲库的 accomp 字段决定
                    // （1=左原唱右伴奏，2=左伴奏右原唱）。
                    if (wantOriginal)
                    {
                        var filter = FfmpegArguments.BuildPanFilter(accomp, true);
                        if (ExtractChannel(sourceTs, filter, originalPath, audioBitrateKbps, cancellationToken))
                            result.OriginalPath = originalPath;
                    }

                    if (wantAccompaniment)
                    {
                        var filter = FfmpegArguments.BuildPanFilter(accomp, false);
                        if (ExtractChannel(sourceTs, filter, accompanimentPath,
                            audioBitrateKbps, cancellationToken))
                        {
                            result.AccompanimentPath = accompanimentPath;
                        }
                    }

                    _logger.Info(string.Format("单音轨声道分离 accomp={0}（1=左原唱，2=左伴奏）",
                        accomp.ToString(CultureInfo.InvariantCulture)));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error("音轨分离失败", ex);
            }

            return result;
        }

        /// <summary>按音轨索引提取。</summary>
        private bool ExtractSingleTrack(
            string sourceTs,
            int trackIndex,
            string outputPath,
            int audioBitrateKbps,
            CancellationToken cancellationToken)
        {
            var arguments = FfmpegArguments.BuildExtractTrackArguments(
                sourceTs, trackIndex, outputPath, audioBitrateKbps);

            var result = Run(arguments, RunTimeout, cancellationToken);
            if (result.TimedOut)
            {
                _logger.Warn("提取音轨超时，已终止 ffmpeg");
                SafeDelete(outputPath);
                return false;
            }

            if (result.ExitCode != 0 || !File.Exists(outputPath))
            {
                _logger.Warn("提取音轨失败 track=" + trackIndex + " exit=" + result.ExitCode +
                             " " + result.Tail);
                SafeDelete(outputPath);
                return false;
            }

            return true;
        }

        /// <summary>用 pan 滤镜拆左右声道。</summary>
        private bool ExtractChannel(
            string sourceTs,
            string panFilter,
            string outputPath,
            int audioBitrateKbps,
            CancellationToken cancellationToken)
        {
            var arguments = FfmpegArguments.BuildExtractChannelArguments(
                sourceTs, panFilter, outputPath, audioBitrateKbps);

            var result = Run(arguments, RunTimeout, cancellationToken);
            if (result.TimedOut)
            {
                _logger.Warn("声道分离超时，已终止 ffmpeg");
                SafeDelete(outputPath);
                return false;
            }

            if (result.ExitCode != 0 || !File.Exists(outputPath))
            {
                _logger.Warn("声道分离失败 filter=" + panFilter + " exit=" + result.ExitCode +
                             " " + result.Tail);
                SafeDelete(outputPath);
                return false;
            }

            return true;
        }

        #region 进程调用

        /// <summary>
        /// 安全地跑一次 ffmpeg。
        /// <para>
        /// 三条必须的规矩（每一条都踩过坑）：
        /// 1. **stdout 与 stderr 都要异步读干净**。只重定向不读的话，管道写满 4KB 就会
        ///    把子进程堵死，父进程又在等它退出 → 互相等到超时；
        /// 2. 超时或取消时**必须 Kill**，否则 <c>using(process)</c> 只是释放句柄，
        ///    ffmpeg 会继续跑几十分钟并占着输出文件；
        /// 3. 退出后要再调一次无参 <c>WaitForExit()</c>，把异步输出回调冲干净，
        ///    否则拿到的 stderr 可能是半截。
        /// </para>
        /// </summary>
        private FfmpegRunResult Run(string arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            EnsureFfmpegResolved();

            string ffmpegPath;
            lock (_probeLock)
            {
                ffmpegPath = _resolvedFfmpegPath;
            }

            if (string.IsNullOrEmpty(ffmpegPath))
                return new FfmpegRunResult(-1, string.Empty, string.Empty, false);

            cancellationToken.ThrowIfCancellationRequested();

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8
            };

            var stderr = new StringBuilder();
            var stdout = new StringBuilder();
            var timedOut = false;

            using (var process = new Process { StartInfo = startInfo })
            {
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        lock (stderr) stderr.AppendLine(args.Data);
                    }
                };
                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        lock (stdout) stdout.AppendLine(args.Data);
                    }
                };

                if (!process.Start())
                    return new FfmpegRunResult(-1, string.Empty, "进程启动失败", false);

                process.BeginErrorReadLine();
                process.BeginOutputReadLine();

                var deadline = DateTime.UtcNow + timeout;

                while (!process.HasExited)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        TryKill(process);
                        _logger.Info("ffmpeg 已被取消并终止");
                        throw new OperationCanceledException(cancellationToken);
                    }

                    if (DateTime.UtcNow > deadline)
                    {
                        timedOut = true;
                        TryKill(process);
                        _logger.Warn("ffmpeg 执行超时，已终止进程");
                        break;
                    }

                    process.WaitForExit(200);
                }

                var exitCode = -1;
                try
                {
                    // 无参 WaitForExit 会把异步输出回调冲刷完，最多再等 5 秒。
                    if (timedOut) process.WaitForExit(5000);
                    else process.WaitForExit();

                    if (process.HasExited) exitCode = process.ExitCode;
                }
                catch (Exception ex)
                {
                    _logger.Warn("读取 ffmpeg 退出码失败: " + ex.Message);
                }

                string stderrText;
                string stdoutText;
                lock (stderr) stderrText = stderr.ToString();
                lock (stdout) stdoutText = stdout.ToString();

                return new FfmpegRunResult(exitCode, stdoutText, stderrText, timedOut);
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch
            {
                // 已经退出了或没有权限，忽略。
            }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 忽略。
            }
        }

        #endregion

        /// <summary>一次 ffmpeg 执行的结果。</summary>
        internal sealed class FfmpegRunResult
        {
            public FfmpegRunResult(int exitCode, string stdOut, string stdErr, bool timedOut)
            {
                ExitCode = exitCode;
                StdOut = stdOut ?? string.Empty;
                StdErr = stdErr ?? string.Empty;
                TimedOut = timedOut;
            }

            public int ExitCode { get; private set; }

            public string StdOut { get; private set; }

            public string StdErr { get; private set; }

            /// <summary>是否因超时被 Kill。</summary>
            public bool TimedOut { get; private set; }

            /// <summary>stderr 末尾若干字符，用于日志定位。</summary>
            public string Tail
            {
                get
                {
                    var text = StdErr;
                    if (text.Length <= 500) return text;
                    return text.Substring(text.Length - 500);
                }
            }
        }
    }
}
