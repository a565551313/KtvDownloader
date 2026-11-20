using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using KtvDownloader.Models;
using KtvDownloader.Services;

namespace KtvDownloader.Controls
{
    /// <summary>
    /// 叠加层的设计画布：黑底代表视频画面，上面按真实比例画出各元素，可以直接拖动。
    /// <para>
    /// <b>为什么编辑放在这里、而不是盖在正在播放的视频上：</b>
    /// WinForms 的 <c>BackColor = Color.Transparent</c> 是靠"让父控件先画背景"模拟的，
    /// 而视频是 libVLC 直接往原生 HWND 上画的、根本不经过 <c>panelVideo</c> 的 Paint ——
    /// 把透明控件塞进 panelVideo，透明区域露出来的会是黑底而不是画面。
    /// 真要按像素透明地叠在视频上只能用 <c>UpdateLayeredWindow</c>（见
    /// <see cref="OverlayLayeredWindow"/>），而那玩意儿吃不了鼠标拖拽的交互。
    /// 所以分工是：<b>这里负责编辑（可拖、可点、绝对可靠），视频上那层只负责显示效果。</b>
    /// </para>
    /// <para>
    /// 两边共用 <see cref="OverlayRenderer"/>，所以画出来是一样的。
    /// </para>
    /// </summary>
    public sealed class OverlayDesignSurface : Control
    {
        private OverlayComposition _composition;
        private OverlayElement _selected;

        private int _videoWidth = 1280;
        private int _videoHeight = 720;

        private bool _dragging;
        private bool _hovering;
        private Point _dragMouseOffset;      // 按下时鼠标相对元素左上角的偏移（视频像素）

        /// <summary>拖动结束后触发，调用方在这里保存配置 / 同步属性面板。</summary>
        public event EventHandler ElementMoved;

        /// <summary>选中元素变化时触发。</summary>
        public event EventHandler SelectedElementChanged;

        public OverlayDesignSurface()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);

            BackColor = Color.FromArgb(30, 30, 34);
            DoubleBuffered = true;
            TabStop = true;
        }

        /// <summary>要显示与编辑的配置。设 null 时画布清空。</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public OverlayComposition Composition
        {
            get { return _composition; }
            set
            {
                _composition = value;
                Invalidate();
            }
        }

        /// <summary>当前选中的元素。必须是 <see cref="Composition"/> 里的同一个实例。</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public OverlayElement SelectedElement
        {
            get { return _selected; }
            set
            {
                if (ReferenceEquals(_selected, value)) return;

                _selected = value;
                Invalidate();

                var handler = SelectedElementChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        /// <summary>视频宽（像素）。只影响画布的纵横比和坐标换算。</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int VideoWidth
        {
            get { return _videoWidth; }
            set { _videoWidth = value > 0 ? value : 1280; Invalidate(); }
        }

        /// <summary>视频高（像素）。</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int VideoHeight
        {
            get { return _videoHeight; }
            set { _videoHeight = value > 0 ? value : 720; Invalidate(); }
        }

        /// <summary>
        /// 用于时间窗口过滤的当前时刻（秒）。
        /// <para>
        /// 编辑时建议配合 <see cref="ShowAllWindows"/> 使用：光按时间过滤的话，
        /// 用户刚打开编辑器、播放头还在 0 秒，设了"10–15 秒"的片头就完全看不见，
        /// 会以为功能坏了。
        /// </para>
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double PreviewTimeSeconds { get; set; }

        /// <summary>true 时忽略时间窗口，所有元素都画出来（编辑态）。</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowAllWindows { get; set; }

        /// <summary>画布上代表视频画面的那块区域（客户区坐标）。</summary>
        public Rectangle VideoArea => OverlayRenderer.FitRect(_videoWidth, _videoHeight, ClientSize.Width, ClientSize.Height);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            var area = VideoArea;

            using (var background = new SolidBrush(Color.Black))
            {
                g.FillRectangle(background, area);
            }

            if (_composition == null || area.Width <= 0 || area.Height <= 0) return;

            // 把"视频像素坐标系"映射到画布上那块区域，之后的绘制全部用视频像素，
            // 于是渲染器不需要知道画布有多大 —— 和 ffmpeg 那边用的是同一套数字。
            var state = g.Save();
            g.TranslateTransform(area.X, area.Y);
            g.ScaleTransform(area.Width / (float)_videoWidth, area.Height / (float)_videoHeight);
            g.SetClip(new Rectangle(0, 0, _videoWidth, _videoHeight));

            OverlayRenderer.Render(g, _composition, _videoWidth, _videoHeight,
                PreviewTimeSeconds, _selected, ShowAllWindows);

            g.Restore(state);

            // 画面边框：让用户看清"视频到哪儿为止"，叠加层出界时一眼就能发现。
            using (var border = new Pen(Color.FromArgb(90, 90, 100)))
            {
                g.DrawRectangle(border, area.X, area.Y, area.Width - 1, area.Height - 1);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            if (e.Button != MouseButtons.Left || _composition == null) return;

            int vx, vy;
            if (!ToVideoCoordinates(e.Location, out vx, out vy)) return;

            var hit = HitTest(vx, vy);
            SelectedElement = hit;

            if (hit == null)
            {
                _dragging = false;
                return;
            }

            var bounds = GetBounds(hit);
            _dragMouseOffset = new Point(vx - bounds.X, vy - bounds.Y);
            _dragging = true;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int vx, vy;
            if (!ToVideoCoordinates(e.Location, out vx, out vy))
            {
                UpdateHoverCursor(false);
                return;
            }

            if (_dragging && _selected != null)
            {
                // 拖拽反解回锚点 + 边距（模型里没有"绝对坐标"这个字段，
                // 存锚点是为了换分辨率/换片源时不用重调）。
                var bounds = GetBounds(_selected);
                OverlayGeometry.ApplyDrag(_selected, _videoWidth, _videoHeight,
                    bounds.Width, bounds.Height,
                    vx - _dragMouseOffset.X, vy - _dragMouseOffset.Y);

                Invalidate();
                return;
            }

            UpdateHoverCursor(HitTest(vx, vy) != null);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (!_dragging) return;

            _dragging = false;
            Capture = false;

            var handler = ElementMoved;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>客户区坐标 → 视频像素坐标。落在画面外时返回 false。</summary>
        public bool ToVideoCoordinates(Point clientPoint, out int videoX, out int videoY)
        {
            videoX = 0;
            videoY = 0;

            var area = VideoArea;
            if (area.Width <= 0 || area.Height <= 0) return false;
            if (!area.Contains(clientPoint)) return false;

            videoX = (int)Math.Round((clientPoint.X - area.X) * (double)_videoWidth / area.Width);
            videoY = (int)Math.Round((clientPoint.Y - area.Y) * (double)_videoHeight / area.Height);
            return true;
        }

        /// <summary>命中测试。倒序遍历：列表后面的元素画在上面，也该先被点中。</summary>
        public OverlayElement HitTest(int videoX, int videoY)
        {
            if (_composition == null) return null;

            var elements = _composition.Elements;
            for (var i = elements.Count - 1; i >= 0; i--)
            {
                var element = elements[i];
                if (element == null || !element.Visible) continue;

                var bounds = GetBounds(element);
                if (bounds.Contains(videoX, videoY)) return element;
            }

            return null;
        }

        private Rectangle GetBounds(OverlayElement element)
        {
            // 命中测试需要一个 Graphics 来量文字。CreateGraphics 拿的是屏幕 DC，
            // 量出来的尺寸和 OnPaint 里的一致（同一个 DPI），够用了。
            using (var g = CreateGraphics())
            {
                return OverlayRenderer.GetElementBounds(g, element, _videoWidth, _videoHeight);
            }
        }

        private void UpdateHoverCursor(bool overElement)
        {
            if (overElement == _hovering) return;

            _hovering = overElement;
            Cursor = overElement ? Cursors.SizeAll : Cursors.Default;
        }
    }
}
