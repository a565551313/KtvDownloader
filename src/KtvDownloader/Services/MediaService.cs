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

        /// <summary>
        /// 从 <c>ffmpeg -i</c> 的 stderr 里抠视频分辨率。
        /// <para>
        /// 目标行长这样（逗号后面还有一堆 SAR/DAR/码率）：
        /// <c>Stream #0:0[0x100]: Video: h264 (Main), yuv420p(tv, bt709), 1280x720 [SAR 1:1 DAR 16:9], 25 fps</c>
        /// 所以要先锚定 <c>Video:</c>，再取第一个"数字x数字"，
        /// 否则可能把 <c>yuv420p</c> 之后别的东西或者音频行里的数字当成尺寸。
        /// </para>
        /// </summary>
        private static readonly Regex VideoSizeRegex =
            new Regex(@"Video:.*?(\d{2,5})x(\d{2,5})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// <c>ffmpeg -encoders</c> 输出里的视频编码器行：能力标志 + 编码器名。
        /// 首字符必须是 V（视频），排除掉 A（音频）和 S（字幕）。
        /// </summary>
        private static readonly Regex EncoderLineRegex =
            new Regex(@"^\s*V\S*\s+(\S+)", RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        /// 第一条音轨的编码名。目标行形如：
        /// <c>Stream #0:1[0x101]: Audio: mp2 ([4][0][0][0] / 0x0004), 48000 Hz, stereo, fltp, 384 kb/s</c>
        /// 只取 <c>Audio:</c> 后第一个单词。
        /// </summary>
        private static readonly Regex AudioCodecRegex =
            new Regex(@"Stream #\d+:\d+.*?: Audio:\s*([A-Za-z0-9_]+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>时长行：<c>Duration: 00:04:12.34, start: ...</c>。</summary>
        private static readonly Regex DurationRegex =
            new Regex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        private readonly ConcurrentDictionary<string, MediaProbeResult> _probeCache =
            new ConcurrentDictionary<string, MediaProbeResult>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 叠加层的资源解析器（读图片尺寸、解析字体、写文本临时文件）。
        /// 一个实例长期复用：图片尺寸和字体解析结果都在里面缓存着，
        /// 批量下载几百首歌时不至于每首都去扫一遍 C:\Windows\Fonts。
        /// </summary>
        private readonly OverlayAssetResolver _overlayAssets;

        /// <summary><c>ffmpeg -encoders</c> 的结果，只探测一次。</summary>
        private HashSet<string> _availableEncoders;

        /// <summary>
        /// 「这个编码器在本机实测能不能跑通」的结果，按编码器名缓存。
        /// <para>和 <see cref="_availableEncoders"/> 是两件事：那边是"ffmpeg 编译进了它"，
        /// 这边是"这台机器此刻真能用它"。见 <see cref="VerifyEncoder"/>。</para>
        /// </summary>
        private readonly Dictionary<string, bool> _verifiedEncoders =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary><see cref="SuggestVideoEncoder"/> 的结论，只算一次。</summary>
        private string _suggestedEncoder;

        public MediaService(ILogger logger, string configuredFfmpegPath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuredFfmpegPath = configuredFfmpegPath;
            _overlayAssets = new OverlayAssetResolver(new FontFileResolver(null, logger), null, logger);
        }

        /// <summary>叠加层资源解析器（编辑器界面要用它做字体/图片校验）。</summary>
        public OverlayAssetResolver OverlayAssets => _overlayAssets;

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
                _probeCache.Clear();
                _availableEncoders = null;   // 换了 ffmpeg 版本，编码器清单也得重探
                _verifiedEncoders.Clear();   // 实测结论同样作废：新 ffmpeg 可能带不同的编码器
                _suggestedEncoder = null;
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
        /// 用 <c>ffmpeg -i</c> 读一次媒体信息：音轨数、分辨率、时长。结果按路径缓存。
        /// <para>
        /// 三样东西<b>来自同一条命令的同一段 stderr</b>，所以合成一次探测。
        /// 原来只有音轨数，烧录叠加层还要分辨率（算坐标）和时长（算进度），
        /// 分开探就是把同一条命令跑三遍，每次都是启动进程 + 最多 30 秒超时。
        /// </para>
        /// </summary>
        /// <summary>
        /// 把 <c>ffmpeg -i</c> 的 stderr 解析成 <see cref="MediaProbeResult"/>。<b>纯函数</b>。
        /// <para>
        /// <b>为什么要从 <see cref="Probe"/> 里抽出来：</b>
        /// 这几条正则决定了烧录叠加层时音频是重编码还是流拷贝、以及坐标按多大分辨率算。
        /// 判错的后果不是报错，而是<b>安静地出一个坏文件</b> ——
        /// <see cref="MediaProbeResult.AudioIsAac"/> 误判成 false 只是白转一遍音频（多一代损失），
        /// 误判成 true 就会把 TS 里的 mp2 直接拷进 MP4，被标成 mp3，
        /// 多数播放器解不出来，表现为"画面正常但完全没声音"（docs 坑1）。
        /// 原来这段内联在 Probe 里，和 ffmpeg 调用、缓存、文件检查混在一起，
        /// 没有 ffmpeg 就一行都测不到 —— 而沙箱和 CI 里恰好都没有 ffmpeg。
        /// </para>
        /// <para>
        /// 解析不出任何东西时返回 <see cref="MediaProbeResult.Empty"/>（<c>Usable = false</c>），
        /// 调用方<b>不应缓存</b>这种结果：探测失败往往是暂时的（文件还在写、杀毒软件占着句柄）。
        /// </para>
        /// </summary>
        /// <param name="stderr"><c>ffmpeg -i</c> 的标准错误输出（流信息全在这里，不在 stdout）。</param>
        internal static MediaProbeResult ParseProbeOutput(string stderr)
        {
            var info = MediaProbeResult.Empty();
            if (string.IsNullOrWhiteSpace(stderr)) return info;

            info.AudioTrackCount = AudioStreamRegex.Matches(stderr).Count;

            var codec = AudioCodecRegex.Match(stderr);
            if (codec.Success) info.AudioCodec = codec.Groups[1].Value;

            var size = VideoSizeRegex.Match(stderr);
            if (size.Success)
            {
                int width, height;
                if (int.TryParse(size.Groups[1].Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out width) &&
                    int.TryParse(size.Groups[2].Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out height))
                {
                    info.VideoWidth = width;
                    info.VideoHeight = height;
                }
            }

            var duration = DurationRegex.Match(stderr);
            if (duration.Success)
            {
                int hours, minutes;
                double seconds;
                if (int.TryParse(duration.Groups[1].Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out hours) &&
                    int.TryParse(duration.Groups[2].Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out minutes) &&
                    double.TryParse(duration.Groups[3].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out seconds))
                {
                    info.DurationSeconds = hours * 3600 + minutes * 60 + seconds;
                }
            }

            // ⚠️ Usable 不等于"跑完了"，而是"真的认出了流信息"。
            //
            // 一条都没解析出来，说明 stderr 不是我们认识的形态（ffmpeg 版本差异、
            // 文件损坏、句柄被杀毒软件占着、路径里有解析不了的字符）。
            // 这种情况**绝不能**当探测成功：Usable=true 会让 AudioTrackCount=0 被当真，
            // 烧录时于是加上 -an，产出一个"画面正常但完全没声音"的文件 ——
            // 正是 docs 坑1 的症状，而且用户要等到播放时才发现。
            //
            // 反过来，调用方看到 Usable=false 就不会缓存（见 Probe），
            // 下次还会重新探一遍；HasVideoSize=false 时也不会去算叠加坐标。
            // 暂时探不到，远好过默默出一个坏文件。
            info.Usable = info.HasVideoSize || info.AudioTrackCount > 0;
            return info;
        }

        public MediaProbeResult Probe(
            string filePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(filePath)) return MediaProbeResult.Empty();

            MediaProbeResult cached;
            if (_probeCache.TryGetValue(filePath, out cached)) return cached;

            if (!IsFfmpegAvailable || !File.Exists(filePath)) return MediaProbeResult.Empty();

            var info = MediaProbeResult.Empty();

            try
            {
                var result = Run(FfmpegArguments.BuildProbeArguments(filePath), ProbeTimeout, cancellationToken);
                if (!result.TimedOut && result.StdErr.Length > 0)
                {
                    // ffmpeg -i 不指定输出文件时退出码必然非 0，所以只看 stderr 有没有流信息。
                    info = ParseProbeOutput(result.StdErr);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn("探测媒体信息失败: " + ex.Message);
            }

            // 只缓存可信结果：探测超时/无输出时不缓存，下次还有机会重试。
            if (info.Usable && !cancellationToken.IsCancellationRequested)
                _probeCache.TryAdd(filePath, info);

            return info;
        }

        /// <summary><see cref="Probe"/> 的异步版。</summary>
        public Task<MediaProbeResult> ProbeAsync(
            string filePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() => Probe(filePath, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// 数音频轨数量。等价于 <c>Probe(filePath).AudioTrackCount</c>，
        /// 保留这个入口是因为下载流程里大量调用点只关心音轨数。
        /// </summary>
        public int CountAudioTracks(
            string filePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Probe(filePath, cancellationToken).AudioTrackCount;
        }

        /// <summary>让某个路径的探测缓存失效（文件被重写过之后调，否则会拿着旧尺寸算坐标）。</summary>
        public void InvalidateProbe(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            MediaProbeResult removed;
            _probeCache.TryRemove(filePath, out removed);
        }

        #region 叠加层烧录

        /// <summary>
        /// 烧录叠加层（水印图片 / 文字卡片）。
        /// <para>
        /// ⚠️ 与 <see cref="ConvertToMp4Async"/> 的代价完全不是一个量级：那边视频是流拷贝，
        /// 几秒完事；这边必须<b>整首重编码</b>，一首歌一两分钟起步。
        /// 哪怕水印只出现 5 秒也一样 —— H.264 的帧间预测决定了每一帧都得解码再编码。
        /// </para>
        /// </summary>
        /// <param name="composition">叠加配置。为空或没有可渲染元素时返回 <c>NothingToBurn</c>。</param>
        /// <param name="progress">0–100 的百分比进度。重编码耗时长，没有进度看着像卡死。</param>
        /// <param name="sampleSeconds">&gt;0 时只烧前 N 秒（「试烧样片」用）。0 = 完整烧录。</param>
        public Task<BurnInResult> BurnInAsync(
            string source,
            string output,
            OverlayComposition composition,
            VocalOption vocal,
            int audioBitrateKbps,
            IProgress<double> progress,
            int sampleSeconds = 0,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() => BurnIn(source, output, composition, vocal,
                audioBitrateKbps, progress, sampleSeconds, cancellationToken), cancellationToken);
        }

        private BurnInResult BurnIn(
            string source,
            string output,
            OverlayComposition composition,
            VocalOption vocal,
            int audioBitrateKbps,
            IProgress<double> progress,
            int sampleSeconds,
            CancellationToken cancellationToken)
        {
            var result = new BurnInResult();

            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(output))
            {
                result.Error = "源文件或输出路径为空";
                return result;
            }

            if (!File.Exists(source))
            {
                result.Error = "源文件不存在: " + source;
                return result;
            }

            if (!IsFfmpegAvailable)
            {
                result.Error = "ffmpeg 不可用，无法烧录叠加层";
                return result;
            }

            // 坐标是按视频真实分辨率算的，拿不到分辨率就没法烧 ——
            // 与其猜一个 1280x720 让水印跑偏，不如直接说清楚为什么失败。
            var probe = Probe(source, cancellationToken);
            if (!probe.Usable || !probe.HasVideoSize)
            {
                result.Error = "读不到视频分辨率，无法计算叠加位置（探测结果: " + probe + "）";
                return result;
            }

            var plan = OverlayFilterBuilder.Build(
                composition, probe.VideoWidth, probe.VideoHeight, _overlayAssets);

            foreach (var warning in plan.Warnings)
            {
                if (!result.Warnings.Contains(warning)) result.Warnings.Add(warning);
            }

            if (plan.IsEmpty)
            {
                result.NothingToBurn = true;
                result.Error = plan.Warnings.Count > 0
                    ? string.Join("；", plan.Warnings.ToArray())
                    : "没有可渲染的叠加元素";
                _logger.Warn("叠加层烧录跳过: " + result.Error);
                return result;
            }

            // 编码器解析放在 IsEmpty 检查**之后**：VideoEncoder 为 auto 时要逐个
            // 真跑微编码来实测硬件编码器，最多要花上一秒。没什么可烧的时候
            // 这一秒是纯浪费 —— 而这条路径本该是立刻返回的。
            var encoder = ResolveEncoder(composition == null ? null : composition.VideoEncoder, result.Warnings);

            if (!_overlayAssets.WriteTextFiles(plan.TextFiles))
            {
                _overlayAssets.CleanupTextFiles(plan.TextFiles);
                result.Error = "写入文字临时文件失败";
                return result;
            }

            try
            {
                // 中间产物已经是 AAC 就直接拷贝音频，别白转一遍多损失一代；
                // 是 TS（mp2）则必须重编码，否则塞进 MP4 会被标成 mp3，播放器静音（坑1）。
                var copyAudio = probe.AudioIsAac;

                var arguments = FfmpegArguments.BuildBurnInArguments(
                    source, output, plan, vocal, probe.AudioTrackCount, audioBitrateKbps,
                    encoder,
                    composition == null ? "veryfast" : composition.VideoPreset,
                    composition == null ? 20 : composition.VideoCrf,
                    sampleSeconds,
                    copyAudio);

                _logger.Info(string.Format("烧录叠加层 {0}x{1} → {2}",
                    probe.VideoWidth, probe.VideoHeight, output));
                _logger.Debug("ffmpeg " + arguments);

                // 试烧样片时总时长就是样片长度，否则进度会一直停在个位数。
                var totalSeconds = sampleSeconds > 0
                    ? Math.Min(sampleSeconds, probe.DurationSeconds > 0 ? probe.DurationSeconds : sampleSeconds)
                    : probe.DurationSeconds;

                var parser = new FfmpegProgressParser(totalSeconds);

                var run = Run(arguments, RunTimeout, cancellationToken, line =>
                {
                    double percent;
                    if (progress != null && parser.TryParse(line, out percent)) progress.Report(percent);
                });

                if (run.TimedOut)
                {
                    result.Error = "烧录超时（超过 " + (int)RunTimeout.TotalMinutes + " 分钟），已终止 ffmpeg";
                    _logger.Warn(result.Error);
                    return result;
                }

                if (run.ExitCode != 0)
                {
                    result.Error = "ffmpeg 退出码 " + run.ExitCode + "：" + Tail(run.StdErr, 600);
                    _logger.Warn("烧录失败: " + result.Error);
                    return result;
                }

                if (!File.Exists(output))
                {
                    result.Error = "ffmpeg 报告成功但输出文件不存在: " + output;
                    return result;
                }

                progress?.Report(100);
                result.Success = true;
                result.OutputPath = output;
                _logger.Info("叠加层烧录完成: " + output);
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Error = "烧录异常: " + ex.Message;
                _logger.Error("烧录叠加层异常", ex);
                return result;
            }
            finally
            {
                // 临时文本文件必须删：批量下载几百首会在临时目录堆出几百个 txt。
                _overlayAssets.CleanupTextFiles(plan.TextFiles);
            }
        }

        /// <inheritdoc/>
        public Task<bool> ReuseBurnedVideoAsync(
            string burnedVideoSource,
            string audioSource,
            string output,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() => ReuseBurnedVideo(
                burnedVideoSource, audioSource, output, cancellationToken), cancellationToken);
        }

        private bool ReuseBurnedVideo(
            string burnedVideoSource,
            string audioSource,
            string output,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(burnedVideoSource) ||
                string.IsNullOrWhiteSpace(audioSource) ||
                string.IsNullOrWhiteSpace(output))
            {
                return false;
            }

            if (!File.Exists(burnedVideoSource) || !File.Exists(audioSource)) return false;
            if (!IsFfmpegAvailable) return false;

            try
            {
                // 探一下提供音轨的那个文件：音轨是不是 AAC 决定能不能 -c:a copy，
                // 音轨数决定要不要 -an。探测结果带缓存，同一次批量下载里几乎不花钱。
                //
                // 这里刻意**保守**：探不出音轨数（Usable=false）就放弃复用、
                // 让调用方回落到老老实实重编码一遍。省时间不值得拿"静音成品"去换。
                var probe = Probe(audioSource, cancellationToken);
                if (!probe.Usable)
                {
                    _logger.Warn("复用烧好视频流前探测失败，回落为重新烧录: " + audioSource);
                    return false;
                }

                var arguments = FfmpegArguments.BuildVideoReuseArguments(
                    burnedVideoSource, audioSource, output,
                    probe.AudioTrackCount, probe.AudioIsAac, 192);

                var run = Run(arguments, RunTimeout, cancellationToken);

                if (run.TimedOut)
                {
                    _logger.Warn("复用视频流超时: " + output);
                    return false;
                }

                if (run.ExitCode != 0)
                {
                    _logger.Warn("复用视频流失败（退出码 " + run.ExitCode + "）: " +
                                 Tail(run.StdErr, 400));
                    return false;
                }

                if (!File.Exists(output))
                {
                    _logger.Warn("ffmpeg 报告成功但输出不存在: " + output);
                    return false;
                }

                InvalidateProbe(output);
                _logger.Info("已复用烧好的视频流（未重编码）: " + output);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn("复用视频流异常，回落为重新烧录: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 校验用户配置的编码器是否真的存在，不存在就回落 libx264。
        /// <para>
        /// 为什么必须校验：<c>-c:v h264_nvenc</c> 在没有 N 卡的机器上会让 ffmpeg
        /// <b>直接失败退出</b>，用户看到的只是"下载失败"，而真正的原因（填了个用不了的编码器）
        /// 埋在 stderr 里。宁可自动回落 + 记一条警告。
        /// </para>
        /// </summary>
        private string ResolveEncoder(string requested, List<string> warnings)
        {
            var name = (requested ?? string.Empty).Trim();

            // ── auto / 留空：实测后自动挑 ────────────────────────────────
            // 这是默认路径，也是绝大多数用户会走的路径。
            if (name.Length == 0 ||
                string.Equals(name, OverlayComposition.AutoEncoder, StringComparison.OrdinalIgnoreCase))
            {
                var suggested = SuggestVideoEncoder();

                if (!string.Equals(suggested, "libx264", StringComparison.OrdinalIgnoreCase))
                {
                    var note = "自动选用硬件编码器 " + suggested + "（比 libx264 快 5–10 倍）";
                    if (warnings != null && !warnings.Contains(note)) warnings.Add(note);
                    _logger.Info(note);
                }

                return suggested;
            }

            // ── 用户显式钉死 libx264：尊重，不再自动切到硬编 ──────────────
            // 否则"我明明选了软编它又自己开回硬编"这种行为没法解释。
            if (string.Equals(name, "libx264", StringComparison.OrdinalIgnoreCase)) return "libx264";

            var available = GetAvailableEncoders();
            if (available != null && !available.Contains(name.ToLowerInvariant()))
            {
                return FallBack(warnings,
                    string.Format("ffmpeg 不支持编码器「{0}」，已回落 libx264", name));
            }

            // 清单里有 ≠ 真能用（驱动太旧、没有对应显卡、nvenc 会话数被占满）。
            // 不实测就照用户填的发命令，结果是 ffmpeg 在初始化编码器那一刻失败退出，
            // 用户看到的只有"下载失败"，真正的原因埋在 stderr 里。
            // 实测不通过就回落软编：慢一点，但成品拿得到。
            if (FfmpegFilterText.IsHardwareEncoder(name) && !VerifyEncoder(name))
            {
                return FallBack(warnings,
                    string.Format("编码器「{0}」在本机实测不可用，已回落 libx264", name));
            }

            return name;
        }

        private string FallBack(List<string> warnings, string message)
        {
            if (warnings != null && !warnings.Contains(message)) warnings.Add(message);
            _logger.Warn(message);
            return "libx264";
        }

        /// <inheritdoc/>
        public Task<string> SuggestVideoEncoderAsync()
        {
            return Task.Run(() => SuggestVideoEncoder());
        }

        /// <summary>
        /// 硬件编码器的优先顺序（从高到低）。
        /// <para>
        /// nvenc 排第一：独显用户最多、驱动最稳。<c>h264_mf</c> 垫底兜底 ——
        /// 它走 Windows Media Foundation，NVIDIA/AMD/Intel 的驱动都可能注册上来，
        /// 所以就算前三个都没编译进 ffmpeg，它也有机会能用。
        /// </para>
        /// </summary>
        internal static readonly string[] HardwareEncoderPreference =
        {
            "h264_nvenc",
            "h264_qsv",
            "h264_amf",
            "h264_mf"
        };

        /// <summary>
        /// 把"本机 ffmpeg 编译进了的编码器"按 <see cref="HardwareEncoderPreference"/>
        /// 排成待实测的候选队列。<b>纯函数</b>，可单测。
        /// <para>
        /// 为什么要单独抽出来：自动挑编码器这件事最容易出错的地方就是优先级 ——
        /// 挑错了用户拿到的是慢 10 倍的软编，而界面上看不出任何异常。
        /// </para>
        /// </summary>
        internal static List<string> OrderEncoderCandidates(IEnumerable<string> available)
        {
            var result = new List<string>();
            if (available == null) return result;

            var pool = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in available)
            {
                if (!string.IsNullOrWhiteSpace(name)) pool.Add(name.Trim());
            }

            foreach (var candidate in HardwareEncoderPreference)
            {
                if (pool.Contains(candidate)) result.Add(candidate);
            }

            return result;
        }

        /// <inheritdoc/>
        public string SuggestVideoEncoder()
        {
            lock (_probeLock)
            {
                if (_suggestedEncoder != null) return _suggestedEncoder;
            }

            var chosen = "libx264";

            try
            {
                if (IsFfmpegAvailable)
                {
                    var available = GetAvailableEncoders();

                    // 按优先级逐个实测，**第一个通过的就收工** ——
                    // 不必把剩下的也测一遍，每次实测都是几百毫秒。
                    foreach (var candidate in OrderEncoderCandidates(available))
                    {
                        if (VerifyEncoder(candidate))
                        {
                            chosen = candidate;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("自动挑选视频编码器失败，用 libx264: " + ex.Message);
            }

            lock (_probeLock)
            {
                _suggestedEncoder = chosen;
                return _suggestedEncoder;
            }
        }

        /// <summary>
        /// 真跑一次微编码，验证这个编码器在<b>这台机器上此刻</b>能不能用。结果按名字缓存。
        /// <para>
        /// 64x64、5 帧，实测几百毫秒（nvenc 第一次初始化会话会慢一些）。
        /// 输入用 <c>nullsrc</c> 由 lavfi 现造，不需要任何文件；输出用 <c>-f null</c> 丢掉。
        /// </para>
        /// <para>
        /// ⚠️ 失败一律当"不能用"处理，包括 lavfi 缺失、超时、ffmpeg 崩了。
        /// 这里宁可保守回落到 libx264（慢但一定能出成品），
        /// 也不要赌一个没验证过的编码器把整次下载搞失败。
        /// </para>
        /// </summary>
        private bool VerifyEncoder(string encoderName)
        {
            if (string.IsNullOrWhiteSpace(encoderName)) return false;

            lock (_probeLock)
            {
                bool cached;
                if (_verifiedEncoders.TryGetValue(encoderName, out cached)) return cached;
            }

            var works = false;

            try
            {
                var arguments = "-hide_banner -loglevel error -f lavfi"
                                + " -i nullsrc=size=64x64:rate=25:duration=0.2"
                                + " -frames:v 5 -c:v " + encoderName
                                + " -f null -";

                var result = Run(arguments, ProbeTimeout, CancellationToken.None);
                works = !result.TimedOut && result.ExitCode == 0;

                if (!works)
                {
                    _logger.Info(string.Format("编码器 {0} 本机实测不可用（退出码 {1}）: {2}",
                        encoderName, result.ExitCode, Tail(result.StdErr, 200)));
                }
            }
            catch (Exception ex)
            {
                _logger.Info("验证编码器 " + encoderName + " 异常，当作不可用: " + ex.Message);
            }

            lock (_probeLock)
            {
                _verifiedEncoders[encoderName] = works;
                return works;
            }
        }

        /// <summary>
        /// 跑一次 <c>ffmpeg -encoders</c> 并缓存清单。失败返回 null（表示"不知道"，此时不拦用户的选择）。
        /// </summary>
        private HashSet<string> GetAvailableEncoders()
        {
            lock (_probeLock)
            {
                if (_availableEncoders != null) return _availableEncoders;
            }

            HashSet<string> encoders = null;

            try
            {
                var result = Run("-hide_banner -encoders", ProbeTimeout, CancellationToken.None);
                if (!result.TimedOut && result.StdOut.Length > 0)
                {
                    encoders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // 行形如：  V..... h264_nvenc           NVIDIA NVENC H.264 encoder
                    foreach (var match in EncoderLineRegex.Matches(result.StdOut))
                    {
                        encoders.Add(match.Groups[1].Value);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("枚举 ffmpeg 编码器失败: " + ex.Message);
            }

            lock (_probeLock)
            {
                _availableEncoders = encoders;
                return _availableEncoders;
            }
        }

        /// <summary>取 stderr 的末尾若干字符。ffmpeg 的报错总在最后，前面全是帧统计。</summary>
        private static string Tail(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var trimmed = text.Trim();
            return trimmed.Length <= maxLength
                ? trimmed
                : "…" + trimmed.Substring(trimmed.Length - maxLength);
        }

        #endregion

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
        private FfmpegRunResult Run(
            string arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            Action<string> onStdoutLine = null)
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
                    if (args.Data == null) return;

                    lock (stdout) stdout.AppendLine(args.Data);

                    // 进度回调（-progress pipe:1 的逐行输出）。
                    // ⚠️ 必须整个包在 try 里：这个回调跑在 ffmpeg 的输出泵线程上，
                    //    抛出去会中断读取，管道写满 4KB 就把子进程堵死 ——
                    //    而堵死的现象是"下载卡住"，和回调里那个 UI 异常隔了十万八千里。
                    if (onStdoutLine != null)
                    {
                        try { onStdoutLine(args.Data); }
                        catch (Exception ex) { _logger.Warn("ffmpeg 进度回调异常: " + ex.Message); }
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
