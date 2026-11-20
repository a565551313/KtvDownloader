using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KtvDownloader.Models;
using KtvDownloader.Services;

namespace KtvDownloader.Controls
{
    /// <summary>
    /// 盖在视频画面上的叠加层窗口。<b>只负责显示，不接收鼠标。</b>
    /// <para>
    /// <b>为什么不能用普通的透明控件：</b>
    /// WinForms 的 <c>BackColor = Color.Transparent</c> 是靠"让父控件先画背景"模拟的，
    /// 而视频是 libVLC 直接往原生 HWND 上画的，不经过 <c>panelVideo</c> 的 Paint。
    /// 把透明控件放进 panelVideo，透明区域露出来的是黑色背景而不是画面；
    /// 放在 panelVideo 外面当兄弟控件，同样只会露出窗体背景。
    /// 要真按像素透明地叠在一个原生视频窗口上，只有 <c>WS_EX_LAYERED</c> +
    /// <c>UpdateLayeredWindow</c> 这一条路 —— 它把一张 32 位 ARGB 位图直接交给 DWM 合成。
    /// </para>
    /// <para>
    /// <b>为什么加 <c>WS_EX_TRANSPARENT</c> 让它对鼠标透明：</b>
    /// 播放时用户要点视频区域（以及以后可能加的手势），叠加层不能把点击吃掉。
    /// 拖拽调整位置在编辑器的设计画布上做（<see cref="OverlayDesignSurface"/>），
    /// 那边是普通控件，交互可靠得多。
    /// </para>
    /// <para>
    /// 渲染走 <see cref="OverlayRenderer"/>，和编辑器画布、ffmpeg 成品共用同一套几何计算。
    /// </para>
    /// </summary>
    public sealed class OverlayLayeredWindow : Form
    {
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private const int ULW_ALPHA = 0x00000002;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;

        private OverlayComposition _composition;
        private int _videoWidth;
        private int _videoHeight;
        private double _currentTimeSeconds;
        private bool _renderFailed;

        /// <summary>要显示的叠加配置。</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public OverlayComposition Composition
        {
            get { return _composition; }
            set { _composition = value; Redraw(); }
        }

        /// <summary>视频宽（像素）。由播放器或探测结果提供。</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int VideoWidth
        {
            get { return _videoWidth; }
            set { _videoWidth = value; }
        }

        /// <summary>视频高（像素）。</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int VideoHeight
        {
            get { return _videoHeight; }
            set { _videoHeight = value; }
        }

        /// <summary>当前播放时刻（秒），用于时间窗口过滤。播放进度定时器每跳一次就设一次。</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double CurrentTimeSeconds
        {
            get { return _currentTimeSeconds; }
            set
            {
                if (Math.Abs(_currentTimeSeconds - value) < 0.01) return;

                _currentTimeSeconds = value;
                Redraw();
            }
        }

        public OverlayLayeredWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            Text = string.Empty;
            Size = new Size(1, 1);
        }

        /// <summary>
        /// 加上分层 + 鼠标穿透 + 不出现在任务栏/Alt-Tab 里。
        /// <para><c>WS_EX_NOACTIVATE</c> 很关键：不然点视频区域会激活这个窗口，
        /// 预览窗体反而失焦，快捷键全失灵。</para>
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return parameters;
            }
        }

        /// <summary>分层窗口自己不需要任何背景绘制，全部由 UpdateLayeredWindow 提交。</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        /// <summary>
        /// 贴到宿主控件（<c>panelVideo</c>）上，并且只覆盖画面真正显示的那块区域（去掉黑边）。
        /// <para>
        /// 必须去黑边：libVLC 默认等比缩放居中，上下或左右会有黑边。
        /// 直接把窗口铺满 panelVideo 的话，叠加层会按 panelVideo 的宽高比排版，
        /// 和视频画面差一截 —— 表现为水印"漂"在错误的位置。
        /// </para>
        /// </summary>
        /// <param name="host">承载视频的控件。</param>
        public void Reposition(Control host)
        {
            if (host == null || host.IsDisposed || !host.IsHandleCreated) return;

            try
            {
                var clientSize = host.ClientSize;
                if (clientSize.Width <= 0 || clientSize.Height <= 0 || _videoWidth <= 0 || _videoHeight <= 0)
                {
                    if (Visible) Hide();
                    return;
                }

                var fit = OverlayRenderer.FitRect(_videoWidth, _videoHeight, clientSize.Width, clientSize.Height);
                if (fit.Width <= 0 || fit.Height <= 0)
                {
                    if (Visible) Hide();
                    return;
                }

                var screenOrigin = host.PointToScreen(fit.Location);

                Bounds = new Rectangle(screenOrigin, fit.Size);

                if (!Visible) Show();
                Redraw();
            }
            catch (Exception)
            {
                // 窗口正在销毁时 PointToScreen 会抛，这不是错误，忽略即可。
            }
        }

        /// <summary>
        /// 重画并提交给 DWM。
        /// <para>
        /// 每次只画一张和显示区域等大的位图，成本很低（几个元素而已）。
        /// 进度定时器每 300ms 才跳一次，不会造成压力。
        /// </para>
        /// </summary>
        public void Redraw()
        {
            if (_renderFailed || !IsHandleCreated) return;
            if (_composition == null || !_composition.Enabled || !_composition.ShowInPreview) return;

            var width = Width;
            var height = Height;
            if (width <= 0 || height <= 0 || _videoWidth <= 0 || _videoHeight <= 0) return;

            try
            {
                // 必须是 32bpp **预乘** Alpha：UpdateLayeredWindow 的 AC_SRC_ALPHA 就按预乘解释，
                // 用 Format32bppArgb 的话半透明边缘会出现黑边或者亮边。
                using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb))
                {
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.FromArgb(0, 0, 0, 0));
                        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                        graphics.ScaleTransform(width / (float)_videoWidth, height / (float)_videoHeight);
                        graphics.SetClip(new Rectangle(0, 0, _videoWidth, _videoHeight));

                        OverlayRenderer.Render(graphics, _composition, _videoWidth, _videoHeight,
                            _currentTimeSeconds, null, false);
                    }

                    SubmitLayer(bitmap);
                }
            }
            catch (Exception)
            {
                // 反复失败就别再试了（例如远程桌面/某些显卡驱动不支持分层窗口），
                // 否则每次进度跳都会抛一遍。预览没有叠加层不影响播放本身。
                _renderFailed = true;
                if (Visible) Hide();
            }
        }

        private void SubmitLayer(Bitmap bitmap)
        {
            var position = new NativePoint { X = Left, Y = Top };
            var size = new NativeSize { CX = bitmap.Width, CY = bitmap.Height };
            var sourceOrigin = new NativePoint { X = 0, Y = 0 };

            var blend = new BlendFunction
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,     // 逐像素 alpha，不再整体调透明度
                AlphaFormat = AC_SRC_ALPHA
            };

            using (var graphics = Graphics.FromImage(bitmap))
            {
                var deviceContext = graphics.GetHdc();
                try
                {
                    UpdateLayeredWindow(Handle, IntPtr.Zero, ref position, ref size,
                        deviceContext, ref sourceOrigin, 0, ref blend, ULW_ALPHA);
                }
                finally
                {
                    graphics.ReleaseHdc(deviceContext);
                }
            }
        }

        /// <summary>彻底关掉。调用方（PreviewForm）在关窗时必须调，否则这个窗口会留在屏幕上。</summary>
        public void Shutdown()
        {
            try
            {
                if (Visible) Hide();
                if (IsHandleCreated) Close();
            }
            catch (Exception)
            {
                // 关闭阶段的异常不值得往上抛。
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int CX;
            public int CY;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hwnd,
            IntPtr hdcDst,
            ref NativePoint pptDst,
            ref NativeSize psize,
            IntPtr hdcSrc,
            ref NativePoint pprSrc,
            int crKey,
            ref BlendFunction pblend,
            int dwFlags);
    }
}
