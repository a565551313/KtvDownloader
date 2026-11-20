using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using KtvDownloader.Controls;
using KtvDownloader.Infrastructure.Config;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Infrastructure.Media;
using KtvDownloader.Models;
using KtvDownloader.Services;
using KtvDownloader.Services.Interfaces;

namespace KtvDownloader.Forms
{
    /// <summary>
    /// 预览窗体。使用 libVLC 直接播放 TS，实时切换音轨。
    /// <para>
    /// 相比之前的 WebBrowser 方案，这里有两个根本改进：
    /// 1. VLC 原生支持 MPEG-TS + mp2，解密后可直接播放，**无需转码**，首屏接近 0 秒；
    /// 2. 音轨切换是 VLC 的实时 API 调用，播放中即可切换原唱/伴奏，和安卓端行为一致。
    /// </para>
    /// </summary>
    public partial class PreviewForm : Form
    {
        private readonly ILogger _logger;
        private readonly Song _song;
        private readonly IPreviewService _previewService;

        private readonly int _durationSeconds;

        private VlcMediaPlayer _player;
        private string _playablePath;
        private CancellationTokenSource _cts;
        // 保留字段以便将来区分本地/缓存来源（当前 VLC 两者处理一致，故未使用）

        /// <summary>VLC 各音轨的 ID（按顺序：原唱、伴奏）。</summary>
        private int[] _audioTrackIds = new int[0];

        /// <summary>当前选择的音轨序号（0=原唱，1=伴奏）。</summary>
        private int _currentTrackIndex;

        /// <summary>
        /// true = 这首歌只有一条音轨，原唱/伴奏靠**左右声道**区分，
        /// 切换要走 <see cref="VlcMediaPlayer.SetAudioChannel"/> 而不是 <see cref="VlcMediaPlayer.SetAudioTrack"/>。
        /// </summary>
        private bool _useChannelSwitching;

        /// <summary>音轨探测定时器（轮询，不是一次性的）。</summary>
        private System.Windows.Forms.Timer _audioProbeTimer;

        /// <summary>已经探测了多少轮。</summary>
        private int _audioProbeAttempts;

        /// <summary>连续几轮都只看到 1 条音轨。用来避免把双音轨误判成单音轨。</summary>
        private int _singleTrackHits;

        /// <summary>探测间隔（毫秒）。</summary>
        private const int AudioProbeIntervalMs = 300;

        /// <summary>最多探测多少轮（300ms × 40 = 12 秒）。</summary>
        private const int AudioProbeMaxAttempts = 40;

        /// <summary>连续命中几轮"只有 1 条音轨"才认定是单音轨。</summary>
        private const int SingleTrackConfirmHits = 3;

        /// <summary>音量（0-100）。</summary>
        // ── 叠加层（水印 / 片头卡片）──────────────────────────────
        //
        // 预览时叠加层是"软"的：GDI+ 画在一个分层窗口上盖住画面，
        // 不动视频文件、不重编码，所以"首屏接近 0 秒"这个特性完全不受影响。
        // 真正烧进文件只发生在下载（DownloadService.ApplyBurnInAsync）。

        /// <summary>叠加配置。为 null 表示调用方没启用这个功能。</summary>
        private readonly OverlayComposition _composition;

        /// <summary>叠加配置的存储，编辑器保存后用它把新配置读回来。</summary>
        private readonly OverlayStore _overlayStore;

        /// <summary>媒体服务，供编辑器"试烧样片"用。为 null 时该按钮不可用。</summary>
        private readonly IMediaService _mediaService;

        private OverlayLayeredWindow _overlayWindow;
        private Button _btnOverlay;
        private bool _overlayVideoSizeKnown;
        private bool _overlayHooksAttached;

        private int _volume = 80;

        /// <summary>进度刷新定时器。</summary>
        private System.Windows.Forms.Timer _progressTimer;

        /// <summary>用户是否正在拖动进度条（拖动时不刷新，避免抢走滑块）。</summary>
        private bool _isSeeking;

        /// <summary>媒体总时长（毫秒）。</summary>
        private long _mediaLength;

        /// <param name="durationSeconds">预览时长（秒），0 表示完整预览。</param>
        /// <param name="composition">叠加配置；传 null 则完全不启用叠加层。</param>
        /// <param name="overlayStore">叠加配置的存储，编辑器要用它保存。</param>
        /// <param name="mediaService">媒体服务，编辑器的"试烧样片"要用。</param>
        public PreviewForm(
            ILogger logger,
            Song song,
            IPreviewService previewService,
            int durationSeconds = 60,
            OverlayComposition composition = null,
            OverlayStore overlayStore = null,
            IMediaService mediaService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _previewService = previewService ?? throw new ArgumentNullException(nameof(previewService));
            _song = song;
            _durationSeconds = durationSeconds;
            _composition = composition;
            _overlayStore = overlayStore;
            _mediaService = mediaService;

            InitializeComponent();

            Text = "预览 - " + (song != null ? song.DisplayName : "未知歌曲");
            lblTitle.Text = song != null ? song.DisplayName : "未知歌曲";
            lblStatus.Text = "准备中...";

            cboVocalMode.Items.Clear();
            cboVocalMode.Items.Add("原唱");
            cboVocalMode.Items.Add("伴奏");
            cboVocalMode.SelectedIndex = 0;
            cboVocalMode.Enabled = false;

            AddOverlayButton();
        }

        /// <summary>
        /// 加「叠加层」按钮。
        /// <para>
        /// 用代码加、不改 Designer：<c>panelTop</c> 里的 <c>lblStatus</c> 是
        /// <c>Dock = Fill</c>，再塞一个 <c>Dock = Right</c> 的按钮进去，
        /// 布局引擎会自动让状态标签缩短，不会和现有控件重叠。
        /// 手写绝对坐标的话，窗口一拉宽就会撞上。
        /// </para>
        /// </summary>
        private void AddOverlayButton()
        {
            _btnOverlay = new Button
            {
                Text = "叠加层…",
                Dock = DockStyle.Right,
                Width = 96,
                FlatStyle = FlatStyle.Flat,
                TabStop = false
            };
            _btnOverlay.Click += OnOverlayButtonClick;

            // Dock=Right 的控件要排在 Fill 之前处理，所以插到集合最前面。
            panelTop.Controls.Add(_btnOverlay);
            _btnOverlay.BringToFront();

            if (_composition == null)
            {
                // 没传配置就说明调用方没启用这个功能，别放一个点了没反应的按钮在那儿。
                _btnOverlay.Visible = false;
            }
        }

        /// <summary>
        /// 直接播放本地已下载的文件（支持双音轨 MP4）。
        /// </summary>
        public void PlayLocalFile(string path)
        {
            _playablePath = path;
            StartPlayback();
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // 初始化 VLC。
            _player = new VlcMediaPlayer(_logger);
            if (!_player.IsAvailable)
            {
                lblStatus.Text = "VLC 不可用：" + _player.ErrorMessage;
                MessageBox.Show(
                    "VLC 播放内核不可用，预览功能无法使用。" + Environment.NewLine + Environment.NewLine +
                    _player.ErrorMessage,
                    "预览", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _player.AttachToWindow(panelVideo.Handle);
            _player.SetVolume(_volume);

            // 本地文件模式直接播。
            if (!string.IsNullOrEmpty(_playablePath) && File.Exists(_playablePath))
            {
                StartPlayback();
                return;
            }

            await PrepareAndPlayAsync();
        }

        /// <summary>
        /// 准备并播放。VLC 能直接读 TS，所以这里只需「下载 + 解密」，
        /// 不做转码，速度比之前快一个数量级。
        /// </summary>
        private async Task PrepareAndPlayAsync()
        {
            if (_song == null)
            {
                lblStatus.Text = "没有歌曲信息";
                return;
            }

            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
            }
            _cts = new CancellationTokenSource();

            try
            {
                progressBar.Visible = true;
                progressBar.Value = 0;
                lblStatus.Text = "正在准备...";

                var progress = new Progress<Tuple<int, string>>(p =>
                {
                    if (IsDisposed) return;
                    lblStatus.Text = string.Format("{0}... {1}%", p.Item2, p.Item1);
                    progressBar.Value = Math.Max(0, Math.Min(100, p.Item1));
                });

                // 只做「取址 → 下载前 N 秒 → 解密」，产物是 .ts，交给 libVLC 直接播。
                var source = await _previewService.PreparePlayableAsync(
                    _song, _durationSeconds, _cts.Token, progress);

                if (IsDisposed) return;

                progressBar.Visible = false;

                if (!source.Success)
                {
                    lblStatus.Text = "准备失败：" + source.Error;
                    return;
                }

                _playablePath = source.PlayablePath;
                StartPlayback();
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) lblStatus.Text = "已取消";
            }
            catch (Exception ex)
            {
                _logger.Error("准备预览失败", ex);
                if (!IsDisposed)
                {
                    progressBar.Visible = false;
                    lblStatus.Text = "失败：" + ex.Message;
                }
            }
        }

        /// <summary>
        /// 开始播放并在就绪后枚举音轨。
        /// </summary>
        private void StartPlayback()
        {
            if (_player == null || !_player.IsAvailable) return;
            if (string.IsNullOrEmpty(_playablePath)) return;

            try
            {
                _mediaLength = 0;
                _player.Play(_playablePath);
                lblStatus.Text = "正在播放：" + Path.GetFileName(_playablePath);

                StartProgressTimer();
                StartAudioProbe();
                StartOverlay();
            }
            catch (Exception ex)
            {
                _logger.Error("播放失败", ex);
                lblStatus.Text = "播放失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 开始轮询探测音轨。
        /// <para>
        /// ⚠️ 这里原本是一个 <b>800ms 的一次性定时器</b>：到点枚举一次，
        /// 拿到几条算几条，之后再也不看。而 libVLC 打开 MPEG-TS 是异步的，
        /// 要先解出 PAT/PMT 才知道有几条音轨 —— 文件大一点、磁盘慢一点，
        /// 800ms 时链表还是空的，于是双音轨的歌被判成"单音轨"，
        /// 原唱/伴奏下拉框直接灰掉，用户看到的就是「正在播放（单音轨，无法切换）」。
        /// </para>
        /// <para>
        /// 改成轮询：拿到 &gt;= 2 条立刻收工；只有 1 条时要连续确认
        /// <see cref="SingleTrackConfirmHits"/> 轮（约 1 秒）才敢下结论；
        /// 一直没就绪就等到 <see cref="AudioProbeMaxAttempts"/> 轮为止。
        /// </para>
        /// </summary>
        private void StartAudioProbe()
        {
            StopAudioProbe();

            _audioProbeAttempts = 0;
            _singleTrackHits = 0;
            _useChannelSwitching = false;

            _audioProbeTimer = new System.Windows.Forms.Timer { Interval = AudioProbeIntervalMs };
            _audioProbeTimer.Tick += OnAudioProbeTick;
            _audioProbeTimer.Start();
        }

        private void StopAudioProbe()
        {
            if (_audioProbeTimer == null) return;

            _audioProbeTimer.Stop();
            _audioProbeTimer.Tick -= OnAudioProbeTick;
            _audioProbeTimer.Dispose();
            _audioProbeTimer = null;
        }

        private void OnAudioProbeTick(object sender, EventArgs e)
        {
            if (IsDisposed || _player == null || !_player.IsAvailable)
            {
                StopAudioProbe();
                return;
            }

            _audioProbeAttempts++;

            int[] ids;
            try
            {
                ids = _player.GetAudioTrackIds();
            }
            catch (Exception ex)
            {
                _logger.Warn("枚举音轨失败: " + ex.Message);
                ids = new int[0];
            }

            // 双音轨：立刻收工，按音轨切。
            if (ids.Length >= 2)
            {
                StopAudioProbe();
                _audioTrackIds = ids;
                _useChannelSwitching = false;
                EnableVocalSwitching("双音轨");
                return;
            }

            // 只有 1 条：可能是真单音轨，也可能 libVLC 还没解完 PMT。
            // 要求"已经开始播放"且连续几轮都是 1 条，才认定是单音轨。
            var demuxReady = _player.IsPlaying() || _player.GetLength() > 0;

            if (ids.Length == 1 && demuxReady)
            {
                _singleTrackHits++;

                if (_singleTrackHits >= SingleTrackConfirmHits)
                {
                    StopAudioProbe();
                    _audioTrackIds = ids;
                    // 老式卡拉OK：原唱和伴奏压在同一条音轨的左右声道里，
                    // 切音轨没有意义（就一条），只能切声道。
                    _useChannelSwitching = true;
                    EnableVocalSwitching("单音轨·左右声道");
                    return;
                }
            }
            else
            {
                _singleTrackHits = 0;
            }

            // 预算用尽：如实报告，不假装能切。
            if (_audioProbeAttempts >= AudioProbeMaxAttempts)
            {
                StopAudioProbe();
                _audioTrackIds = ids;

                if (ids.Length == 1)
                {
                    _useChannelSwitching = true;
                    EnableVocalSwitching("单音轨·左右声道（未确认）");
                }
                else
                {
                    cboVocalMode.Enabled = false;
                    lblStatus.Text = "正在播放（未检测到可切换的音轨）";
                    _logger.Warn(string.Format("探测 {0} 轮后仍未拿到音轨列表（ids={1}）",
                        _audioProbeAttempts, ids.Length));
                }
            }
        }

        /// <summary>
        /// 打开原唱/伴奏下拉框，并应用当前选择。
        /// </summary>
        private void EnableVocalSwitching(string mode)
        {
            if (IsDisposed) return;

            cboVocalMode.Enabled = true;

            _logger.Info(string.Format("音轨模式={0}，音轨 id=[{1}]，accomp={2}",
                mode,
                string.Join(",", Array.ConvertAll(_audioTrackIds, x => x.ToString())),
                _song == null ? "?" : _song.Accomp.ToString()));

            // 应用当前选择（默认原唱 = 第 0 项）。
            ApplyVocalMode(cboVocalMode.SelectedIndex < 0 ? 0 : cboVocalMode.SelectedIndex);
        }

        /// <summary>
        /// 应用「原唱 / 伴奏」选择（index 0=原唱，1=伴奏）。
        /// 按这首歌的音轨结构自动选切换手段：双音轨切音轨，单音轨切左右声道。
        /// </summary>
        private void ApplyVocalMode(int index)
        {
            if (_player == null || IsDisposed) return;

            var wantOriginal = index <= 0;

            if (_useChannelSwitching)
            {
                ApplyChannel(wantOriginal);
                return;
            }

            ApplyTrack(wantOriginal ? 0 : 1);
        }

        /// <summary>
        /// 双音轨：实时切音轨，不重新加载文件、不重新转码。
        /// </summary>
        private void ApplyTrack(int index)
        {
            if (_player == null || _audioTrackIds.Length == 0) return;

            var clamped = Math.Max(0, Math.Min(index, _audioTrackIds.Length - 1));
            var trackId = _audioTrackIds[clamped];

            if (_player.SetAudioTrack(trackId))
            {
                _currentTrackIndex = clamped;
                var name = clamped == 0 ? "原唱" : "伴奏";
                lblStatus.Text = string.Format("正在播放 - {0}（音轨 {1}）", name, trackId);
            }
            else
            {
                lblStatus.Text = string.Format("切换音轨失败（id={0}），这首歌可能只有一条音轨", trackId);
                _logger.Warn("libvlc_audio_set_track 返回失败，id=" + trackId);
            }
        }

        /// <summary>
        /// 单音轨老式卡拉OK：原唱/伴奏压在左右声道里，只放其中一路即可。
        /// <para>
        /// 谁是原唱由曲库的 <c>songs.accomp</c> 决定（docs §7.5，规则见
        /// <see cref="VocalChannelRule"/>）。accomp 为 0 表示曲库没标注，
        /// 这时按「左=原唱」猜，并在状态栏告诉用户"反了就选另一项"，
        /// 而不是让他对着一个灰掉的下拉框猜为什么不能用。
        /// </para>
        /// </summary>
        private void ApplyChannel(bool wantOriginal)
        {
            var accomp = _song == null ? 0 : _song.Accomp;
            var channel = VocalChannelRule.ResolveChannel(accomp, wantOriginal);

            if (!_player.SetAudioChannel(channel))
            {
                lblStatus.Text = "切换声道失败（这首歌的音轨可能不是立体声）";
                return;
            }

            _currentTrackIndex = wantOriginal ? 0 : 1;

            var name = wantOriginal ? "原唱" : "伴奏";
            var side = channel == VocalChannelRule.ChannelLeft ? "左声道" : "右声道";
            var hint = VocalChannelRule.IsAccompKnown(accomp)
                ? string.Empty
                : "；曲库未标注 accomp，若听起来反了请选另一项";

            lblStatus.Text = string.Format("正在播放 - {0}（单音轨，只放{1}{2}）", name, side, hint);
        }

        private void OnVocalModeChanged(object sender, EventArgs e)
        {
            var index = cboVocalMode.SelectedIndex;
            if (index < 0) return;

            // 实时切换，无需重新准备预览文件。
            ApplyVocalMode(index);
        }

        private void OnPlayPauseClick(object sender, EventArgs e)
        {
            if (_player == null) return;

            if (_player.IsPlaying()) _player.Pause();
            else _player.Play(_playablePath);
        }

        private void OnStopClick(object sender, EventArgs e)
        {
            if (_player == null) return;

            // 停止后 IsPlaying/GetLength 都失效，再探测也拿不到结论，直接停掉。
            StopAudioProbe();

            // 画面没了，盖在上面的分层窗口也必须收起来，否则会在黑屏上留一块残影。
            StopOverlay();

            _player.Stop();
            if (_progressTimer != null) _progressTimer.Stop();
            trackSeek.Value = 0;
            lblTime.Text = "00:00";
            lblStatus.Text = "已停止";
        }

        /// <summary>
        /// 启动进度刷新定时器。每 500ms 读一次播放位置更新进度条。
        /// </summary>
        private void StartProgressTimer()
        {
            if (_progressTimer == null)
            {
                _progressTimer = new System.Windows.Forms.Timer { Interval = 500 };
                _progressTimer.Tick += OnProgressTick;
            }

            _progressTimer.Stop();
            _progressTimer.Start();
        }

        private void OnProgressTick(object sender, EventArgs e)
        {
            if (_player == null || !_player.IsAvailable || IsDisposed) return;

            try
            {
                // 拖动中不刷新，避免把用户的滑块位置抢回去。
                if (_isSeeking) return;

                // 时长需要播放开始后才有效，这里持续探测。
                if (_mediaLength <= 0)
                {
                    _mediaLength = _player.GetLength();
                }

                var position = _player.GetTime();

                UpdateOverlay(position);

                if (_mediaLength > 0)
                {
                    var ratio = (double)position / _mediaLength;
                    var value = (int)Math.Round(ratio * trackSeek.Maximum);
                    trackSeek.Value = Math.Max(trackSeek.Minimum,
                        Math.Min(trackSeek.Maximum, value));

                    lblTime.Text = FormatTime(position) + " / " + FormatTime(_mediaLength);
                }
                else
                {
                    lblTime.Text = FormatTime(position);
                }
            }
            catch
            {
                // 读取失败忽略，下一拍再试。
            }
        }

        private static string FormatTime(long milliseconds)
        {
            if (milliseconds <= 0) return "00:00";

            var totalSeconds = milliseconds / 1000;
            var hours = totalSeconds / 3600;
            var minutes = (totalSeconds % 3600) / 60;
            var seconds = totalSeconds % 60;

            if (hours > 0)
                return string.Format("{0}:{1:D2}:{2:D2}", hours, minutes, seconds);

            return string.Format("{0:D2}:{1:D2}", minutes, seconds);
        }

        /// <summary>开始拖动进度条。</summary>
        private void OnSeekMouseDown(object sender, MouseEventArgs e)
        {
            _isSeeking = true;
        }

        /// <summary>结束拖动，跳转到目标位置。</summary>
        private void OnSeekMouseUp(object sender, MouseEventArgs e)
        {
            _isSeeking = false;

            if (_player == null || _mediaLength <= 0) return;

            try
            {
                var ratio = (float)trackSeek.Value / trackSeek.Maximum;
                _player.SetPosition(ratio);
                lblStatus.Text = "跳转到 " + FormatTime((long)(_mediaLength * ratio));
            }
            catch
            {
                // 定位失败忽略。
            }
        }

        /// <summary>拖动过程中实时更新时间显示。</summary>
        private void OnSeekScroll(object sender, EventArgs e)
        {
            if (_mediaLength <= 0) return;

            var ratio = (double)trackSeek.Value / trackSeek.Maximum;
            lblTime.Text = FormatTime((long)(_mediaLength * ratio)) + " / " + FormatTime(_mediaLength);
        }

        private void OnVolumeChanged(object sender, EventArgs e)
        {
            _volume = trackVolume.Value;
            if (_player != null) _player.SetVolume(_volume);
        }

        private void OnOpenFolderClick(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_playablePath) || !File.Exists(_playablePath)) return;
                Process.Start("explorer.exe", "/select,\"" + _playablePath + "\"");
            }
            catch (Exception ex)
            {
                _logger.Warn("打开目录失败: " + ex.Message);
            }
        }

        private void OnClearCacheClick(object sender, EventArgs e)
        {
            try
            {
                if (_player != null) _player.Stop();
                _previewService.ClearCache();
                lblStatus.Text = "缓存已清空";
                MessageBox.Show("预览缓存已清空。", "预览",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _logger.Error("清空缓存失败", ex);
            }
        }

        #region 叠加层（水印 / 片头卡片）

        /// <summary>
        /// 开始显示叠加层。
        /// <para>
        /// 用的是<b>分层窗口</b>而不是透明控件：视频是 libVLC 直接往原生 HWND 上画的，
        /// WinForms 的 <c>BackColor = Transparent</c> 靠"让父控件先画背景"来模拟，
        /// 放在 <c>panelVideo</c> 里露出来的是黑底、放在外面露出来的是窗体背景，
        /// 两种都盖不出画面。只有 <c>WS_EX_LAYERED</c> + <c>UpdateLayeredWindow</c>
        /// 能把 ARGB 位图真正按像素透明地合成到视频上面。
        /// </para>
        /// </summary>
        private void StartOverlay()
        {
            if (_composition == null || !_composition.Enabled || !_composition.ShowInPreview) return;
            if (_composition.GetRenderableElements().Count == 0) return;
            if (IsDisposed) return;

            try
            {
                if (_overlayWindow == null || _overlayWindow.IsDisposed)
                {
                    _overlayWindow = new OverlayLayeredWindow
                    {
                        Composition = _composition,
                        VideoWidth = 1280,
                        VideoHeight = 720
                    };

                    // Owner 让它始终浮在预览窗体之上，但不会跑到别的应用前面去。
                    _overlayWindow.Show(this);
                }

                if (!_overlayHooksAttached)
                {
                    // 分层窗口是独立的顶层窗口，不会跟着 panelVideo 自动移动，
                    // 这三个事件缺一个，用户一拖窗口或一缩放叠加层就会留在原地。
                    panelVideo.SizeChanged += OnHostLayoutChanged;
                    Move += OnHostLayoutChanged;
                    Resize += OnHostLayoutChanged;
                    _overlayHooksAttached = true;
                }

                _overlayVideoSizeKnown = false;
                RepositionOverlay();
            }
            catch (Exception ex)
            {
                // 叠加层只是锦上添花，失败不该影响播放本身。
                _logger.Warn("启动叠加层失败: " + ex.Message);
                StopOverlay();
            }
        }

        /// <summary>
        /// 每一拍进度更新叠加层：补上视频尺寸、按当前时刻过滤时间窗口、重新定位。
        /// <para>
        /// ⚠️ 视频尺寸要反复问、不能问一次就放弃：和音轨探测是同一类问题 ——
        /// libVLC 解 TS 是异步的，刚开始播放那几拍 <c>libvlc_video_get_size</c> 常常失败。
        /// 拿到一次就够了，之后不再问。
        /// </para>
        /// </summary>
        private void UpdateOverlay(long positionMilliseconds)
        {
            if (_overlayWindow == null || _overlayWindow.IsDisposed) return;

            try
            {
                if (!_overlayVideoSizeKnown)
                {
                    int width, height;
                    if (_player != null && _player.TryGetVideoSize(out width, out height))
                    {
                        _overlayWindow.VideoWidth = width;
                        _overlayWindow.VideoHeight = height;
                        _overlayVideoSizeKnown = true;
                        RepositionOverlay();
                    }
                }

                _overlayWindow.CurrentTimeSeconds = positionMilliseconds / 1000.0;

                // 尺寸变化会让画面区域跟着变（比如从 480p 切到 720p 的片源），
                // 所以拿到尺寸之后每拍仍然对齐一次，成本就是几次整数运算。
                if (_overlayVideoSizeKnown) RepositionOverlay();
            }
            catch (Exception ex)
            {
                _logger.Warn("更新叠加层失败: " + ex.Message);
            }
        }

        private void RepositionOverlay()
        {
            if (_overlayWindow == null || _overlayWindow.IsDisposed) return;

            // 最小化时视频区域是 0，留着窗口会在任务栏附近糊一块东西。
            if (WindowState == FormWindowState.Minimized || !Visible)
            {
                if (_overlayWindow.Visible) _overlayWindow.Hide();
                return;
            }

            _overlayWindow.Reposition(panelVideo);
        }

        private void OnHostLayoutChanged(object sender, EventArgs e)
        {
            RepositionOverlay();
        }

        /// <summary>
        /// 收起叠加层。
        /// <para>
        /// 必须在关窗和停止播放时都调：这是个独立的顶层窗口，
        /// 不会随预览窗体自动消失，漏掉就会在桌面上留一块残影。
        /// </para>
        /// </summary>
        private void StopOverlay()
        {
            if (_overlayHooksAttached)
            {
                try
                {
                    panelVideo.SizeChanged -= OnHostLayoutChanged;
                    Move -= OnHostLayoutChanged;
                    Resize -= OnHostLayoutChanged;
                }
                catch (Exception)
                {
                    // 控件已经在销毁中，解绑失败无所谓。
                }

                _overlayHooksAttached = false;
            }

            if (_overlayWindow != null)
            {
                try
                {
                    _overlayWindow.Shutdown();

                    // Owner 关系会让它跟预览窗体一起 Dispose，但保险起见自己先放掉引用。
                    if (!_overlayWindow.IsDisposed) _overlayWindow.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Warn("关闭叠加层失败: " + ex.Message);
                }

                _overlayWindow = null;
            }

            _overlayVideoSizeKnown = false;
        }

        /// <summary>打开叠加层编辑器。从预览窗口打开才能"试烧样片"和"标记当前播放位置"。</summary>
        private void OnOverlayButtonClick(object sender, EventArgs e)
        {
            if (_composition == null) return;

            using (var editor = new OverlayEditorForm(
                _composition,
                _overlayStore,
                _logger,
                _mediaService,
                _playablePath,
                () => _player == null ? 0 : _player.GetTime() / 1000.0))
            {
                editor.ShowDialog(this);

                if (!editor.Saved || _overlayStore == null) return;

                // 编辑器改的是副本并且已经存盘，这里把新配置读回来**原地**替换，
                // 这样主窗体持有的同一个实例也立刻看到新配置（下一次下载就生效）。
                try
                {
                    _composition.ReplaceWith(_overlayStore.Load());
                }
                catch (Exception ex)
                {
                    _logger.Warn("重新载入叠加配置失败: " + ex.Message);
                    return;
                }

                // 配置可能从"关"变成"开"、也可能新增了元素，所以整套重来一遍。
                StopOverlay();
                StartOverlay();

                lblStatus.Text = _composition.Enabled && _composition.ShowInPreview
                    ? "叠加层已更新"
                    : "叠加层已关闭";
            }
        }

        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            // 音轨探测定时器必须在释放 player **之前**停掉：
            // 它每 300ms 拍一次，若留到 player 已 Dispose 之后才触发，
            // OnAudioProbeTick 里就是 NullReferenceException。
            StopAudioProbe();

            // 叠加层是独立的顶层窗口，不会跟着预览窗体自动消失，必须显式收掉。
            StopOverlay();

            if (_progressTimer != null)
            {
                _progressTimer.Stop();
                _progressTimer.Tick -= OnProgressTick;
                _progressTimer.Dispose();
                _progressTimer = null;
            }

            if (_player != null)
            {
                _player.Stop();
                _player.Dispose();
                _player = null;
            }

            base.OnFormClosing(e);
        }
    }
}
