using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// </summary>
    public sealed class MediaService : IMediaService
    {
        private readonly ILogger _logger;
        private readonly string _configuredFfmpegPath;
        private string _resolvedFfmpegPath;
        private bool _probed;

        public MediaService(ILogger logger, string configuredFfmpegPath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuredFfmpegPath = configuredFfmpegPath;
        }

        public string FfmpegPath
        {
            get
            {
                EnsureFfmpegResolved();
                return _resolvedFfmpegPath;
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

        public Task<int> CountAudioTracksAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() => CountAudioTracks(filePath), cancellationToken);
        }

        /// <summary>
        /// 用 ffmpeg -i 读取媒体信息，数出音频轨数量。
        /// </summary>
        private int CountAudioTracks(string filePath)
        {
            if (!IsFfmpegAvailable || !File.Exists(filePath)) return 0;

            try
            {
                var output = RunFfmpeg("-i " + Quote(filePath), TimeSpan.FromSeconds(30));
                return Regex.Matches(output, @"Stream #\d+:\d+.*?: Audio:", RegexOptions.IgnoreCase).Count;
            }
            catch (Exception ex)
            {
                _logger.Warn("统计音轨失败: " + ex.Message);
                return 0;
            }
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
                var audioTracks = CountAudioTracks(sourceTs);
                var arguments = new StringBuilder();

                arguments.Append("-y -i ").Append(Quote(sourceTs));
                arguments.Append(" -map 0:v:0");

                // 按音轨处理方式决定映射哪条音轨。
                if (audioTracks >= 2)
                {
                    switch (vocal)
                    {
                        case VocalOption.OriginalOnly:
                            arguments.Append(" -map 0:a:0");
                            break;
                        case VocalOption.AccompanimentOnly:
                            arguments.Append(" -map 0:a:1");
                            break;
                        default:
                            arguments.Append(" -map 0:a:0 -map 0:a:1");
                            break;
                    }
                }
                else
                {
                    arguments.Append(" -map 0:a:0");
                }

                // 视频流拷贝，音频重编码为 AAC —— 否则 MP4 里的 mp2 会静音。
                arguments.Append(" -c:v copy -c:a aac");
                arguments.Append(" -b:a ").Append(Math.Max(64, Math.Min(320, audioBitrateKbps))).Append("k");
                arguments.Append(" -ac 2");
                arguments.Append(" -movflags +faststart");
                arguments.Append(' ').Append(Quote(outputMp4));

                cancellationToken.ThrowIfCancellationRequested();

                var exitCode = RunFfmpegForExit(arguments.ToString(), TimeSpan.FromMinutes(30), cancellationToken);

                if (exitCode != 0 || !File.Exists(outputMp4))
                {
                    _logger.Warn("转 MP4 失败，exitCode=" + exitCode);
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
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() =>
                SplitVocal(sourceTs, outputDirectory, baseFileName, option, audioBitrateKbps, cancellationToken),
                cancellationToken);
        }

        private SplitResult SplitVocal(
            string sourceTs,
            string outputDirectory,
            string baseFileName,
            VocalOption option,
            int audioBitrateKbps,
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

                var audioTracks = CountAudioTracks(sourceTs);
                result.AudioTrackCount = audioTracks;

                var safeBase = string.IsNullOrWhiteSpace(baseFileName)
                    ? Path.GetFileNameWithoutExtension(sourceTs)
                    : baseFileName.ToSafeFileName();

                if (audioTracks >= 2)
                {
                    // 双音轨：第 1 条=原唱，第 2 条=伴奏（官方约定）。
                    if (option == VocalOption.OriginalOnly || option == VocalOption.SplitBoth)
                    {
                        var path = Path.Combine(outputDirectory, safeBase + "-原唱.mp4");
                        if (ExtractSingleTrack(sourceTs, 0, path, audioBitrateKbps, cancellationToken))
                            result.OriginalPath = path;
                    }

                    if (option == VocalOption.AccompanimentOnly || option == VocalOption.SplitBoth)
                    {
                        var path = Path.Combine(outputDirectory, safeBase + "-伴奏.mp4");
                        if (ExtractSingleTrack(sourceTs, 1, path, audioBitrateKbps, cancellationToken))
                            result.AccompanimentPath = path;
                    }
                }
                else
                {
                    // 单音轨：老式卡拉OK 左右声道分离（左伴右原 或 反之，无法从文件判断，
                    // 按官方 accomp 字段在调用方决定；这里默认 左=伴奏 右=原唱）。
                    if (option == VocalOption.OriginalOnly || option == VocalOption.SplitBoth)
                    {
                        var path = Path.Combine(outputDirectory, safeBase + "-原唱.mp4");
                        if (ExtractChannel(sourceTs, "pan=stereo|c0=c1|c0=c1", path,
                            audioBitrateKbps, cancellationToken))
                            result.OriginalPath = path;
                    }

                    if (option == VocalOption.AccompanimentOnly || option == VocalOption.SplitBoth)
                    {
                        var path = Path.Combine(outputDirectory, safeBase + "-伴奏.mp4");
                        if (ExtractChannel(sourceTs, "pan=stereo|c0=c0|c0=c0", path,
                            audioBitrateKbps, cancellationToken))
                            result.AccompanimentPath = path;
                    }
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
            var arguments = "-y -i " + Quote(sourceTs) +
                            " -map 0:v:0 -map 0:a:" + trackIndex +
                            " -c:v copy -c:a aac -b:a " +
                            Math.Max(64, Math.Min(320, audioBitrateKbps)) + "k -ac 2" +
                            " -movflags +faststart " + Quote(outputPath);

            var exitCode = RunFfmpegForExit(arguments, TimeSpan.FromMinutes(30), cancellationToken);
            return exitCode == 0 && File.Exists(outputPath);
        }

        /// <summary>用 pan 滤镜拆左右声道。</summary>
        private bool ExtractChannel(
            string sourceTs,
            string panFilter,
            string outputPath,
            int audioBitrateKbps,
            CancellationToken cancellationToken)
        {
            var arguments = "-y -i " + Quote(sourceTs) +
                            " -map 0:v:0 -map 0:a:0" +
                            " -af " + Quote(panFilter) +
                            " -c:v copy -c:a aac -b:a " +
                            Math.Max(64, Math.Min(320, audioBitrateKbps)) + "k" +
                            " -movflags +faststart " + Quote(outputPath);

            var exitCode = RunFfmpegForExit(arguments, TimeSpan.FromMinutes(30), cancellationToken);
            return exitCode == 0 && File.Exists(outputPath);
        }

        #region 进程调用

        private string RunFfmpeg(string arguments, TimeSpan timeout)
        {
            EnsureFfmpegResolved();
            if (string.IsNullOrEmpty(_resolvedFfmpegPath)) return string.Empty;

            var startInfo = new ProcessStartInfo
            {
                FileName = _resolvedFfmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null) return string.Empty;

                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit((int)timeout.TotalMilliseconds);
                return stderr;
            }
        }

        private int RunFfmpegForExit(string arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            EnsureFfmpegResolved();
            if (string.IsNullOrEmpty(_resolvedFfmpegPath)) return -1;

            var startInfo = new ProcessStartInfo
            {
                FileName = _resolvedFfmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null) return -1;

                var stderrBuilder = new StringBuilder();
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null) stderrBuilder.AppendLine(args.Data);
                };
                process.BeginErrorReadLine();

                var deadline = DateTime.UtcNow + timeout;
                while (!process.HasExited)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        try { process.Kill(); } catch { }
                        throw new OperationCanceledException(cancellationToken);
                    }

                    if (DateTime.UtcNow > deadline)
                    {
                        try { process.Kill(); } catch { }
                        _logger.Warn("ffmpeg 执行超时");
                        return -1;
                    }

                    process.WaitForExit(200);
                }

                if (process.ExitCode != 0)
                {
                    var detail = stderrBuilder.ToString();
                    if (detail.Length > 500) detail = detail.Substring(detail.Length - 500);
                    _logger.Warn("ffmpeg 失败: " + detail);
                }

                return process.ExitCode;
            }
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
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
    }
}
