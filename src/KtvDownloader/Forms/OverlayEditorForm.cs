using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using KtvDownloader.Controls;
using KtvDownloader.Infrastructure.Config;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Models;
using KtvDownloader.Services;
using KtvDownloader.Services.Interfaces;

namespace KtvDownloader.Forms
{
    /// <summary>
    /// 叠加层编辑器：管理水印图片与文字卡片，可直接在画布上拖动定位、设定显示时间段。
    /// <para>
    /// 界面全部用代码构建，没有 Designer 文件 —— 控件多、布局是绝对定位，
    /// 手写 Designer 分部类更容易出错，而且没法在沙箱里验证。
    /// </para>
    /// <para>
    /// 编辑的是配置的<b>副本</b>（<see cref="OverlayComposition.Clone"/>）：
    /// 用户改到一半点"取消"，全局配置不该跟着变，正在跑的下载任务更不该受影响。
    /// </para>
    /// </summary>
    public sealed class OverlayEditorForm : Form
    {
        private readonly OverlayComposition _working;
        private readonly OverlayStore _store;
        private readonly ILogger _logger;
        private readonly IMediaService _mediaService;
        private readonly string _sampleSource;
        private readonly Func<double> _currentTimeProvider;

        private CancellationTokenSource _sampleCts;

        // ── 控件 ──
        private OverlayDesignSurface _surface;
        private CheckBox _chkEnabled;
        private CheckBox _chkShowInPreview;
        private CheckBox _chkBurnOnDownload;
        private CheckBox _chkShowAllWindows;
        private ComboBox _cboEncoder;
        private ComboBox _cboPreset;
        private NumericUpDown _numCrf;
        private Button _btnSample;
        private Label _lblSampleResult;

        private ListBox _lstElements;
        private Button _btnAddImage;
        private Button _btnAddText;
        private Button _btnDelete;
        private Button _btnUp;
        private Button _btnDown;

        private Label _lblElementSummary;
        private CheckBox _chkVisible;
        private ComboBox _cboAnchor;
        private NumericUpDown _numMarginX;
        private NumericUpDown _numMarginY;
        private CheckBox _chkTimeWindow;
        private NumericUpDown _numStart;
        private NumericUpDown _numEnd;
        private Button _btnMarkStart;
        private Button _btnMarkEnd;

        private Panel _pnlImage;
        private TextBox _txtImagePath;
        private Button _btnBrowseImage;
        private NumericUpDown _numRelativeWidth;
        private NumericUpDown _numOpacity;

        private Panel _pnlText;
        private ComboBox _cboTextPreset;
        private TextBox _txtText;
        private TextBox _txtFontFamily;
        private NumericUpDown _numFontSize;
        private Button _btnFillColor;
        private Button _btnBorderColor;
        private NumericUpDown _numBorderWidth;
        private CheckBox _chkShadow;

        private Button _btnOk;
        private Button _btnCancel;

        private bool _syncing;   // 防止"代码改控件"又触发"控件改代码"的死循环

        /// <summary>保存成功后置 true，调用方据此决定要不要刷新预览。</summary>
        public bool Saved { get; private set; }

        /// <param name="composition">当前配置（会被克隆，不直接改）。</param>
        /// <param name="store">保存目标。</param>
        /// <param name="mediaService">用于「试烧样片」；传 null 则该按钮不可用。</param>
        /// <param name="sampleSource">试烧用的视频文件；从预览窗口打开时就是正在播的那个。</param>
        /// <param name="currentTimeProvider">
        /// 取当前播放时刻（秒），供「标记为当前播放位置」用；传 null 则该按钮不可用。
        /// </param>
        public OverlayEditorForm(
            OverlayComposition composition,
            OverlayStore store,
            ILogger logger,
            IMediaService mediaService = null,
            string sampleSource = null,
            Func<double> currentTimeProvider = null)
        {
            _working = (composition ?? new OverlayComposition()).Clone();
            _store = store;
            _logger = logger;
            _mediaService = mediaService;
            _sampleSource = sampleSource;
            _currentTimeProvider = currentTimeProvider;

            BuildUi();
            RefreshElementList();
            RefreshGlobalControls();

            // 从预览窗口打开时，用真实视频的分辨率当画布比例；否则按 720p。
            BeginProbeVideoSize();
        }

        #region 界面构建

        private void BuildUi()
        {
            Text = "叠加层设置（水印 / 片头卡片）";
            ClientSize = new Size(1010, 700);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9f);

            // ── 左：设计画布 ──
            _surface = new OverlayDesignSurface
            {
                Location = new Point(12, 12),
                Size = new Size(620, 380),
                Composition = _working,
                VideoWidth = 1280,
                VideoHeight = 720,
                ShowAllWindows = true
            };
            _surface.ElementMoved += OnSurfaceElementMoved;
            _surface.SelectedElementChanged += OnSurfaceSelectionChanged;
            Controls.Add(_surface);

            var lblSurfaceHint = NewLabel(
                "拖动画面上的元素即可调整位置，会自动吸附到九宫格锚点。位置与大小以 720p 为基准，渲染时按实际分辨率等比缩放。",
                12, 396, 620);
            lblSurfaceHint.ForeColor = Color.Gray;
            Controls.Add(lblSurfaceHint);

            // ── 左下：全局设置 ──
            _chkEnabled = NewCheck("启用叠加层（总开关）", 12, 424, 190);
            _chkEnabled.CheckedChanged += OnGlobalChanged;
            Controls.Add(_chkEnabled);

            _chkShowInPreview = NewCheck("预览时显示", 210, 424, 110);
            _chkShowInPreview.CheckedChanged += OnGlobalChanged;
            Controls.Add(_chkShowInPreview);

            _chkBurnOnDownload = NewCheck("下载时烧进视频", 330, 424, 140);
            _chkBurnOnDownload.CheckedChanged += OnGlobalChanged;
            Controls.Add(_chkBurnOnDownload);

            _chkShowAllWindows = NewCheck("画布显示全部时间段", 480, 424, 152);
            _chkShowAllWindows.Checked = true;
            _chkShowAllWindows.CheckedChanged += delegate
            {
                _surface.ShowAllWindows = _chkShowAllWindows.Checked;
                _surface.Invalidate();
            };
            Controls.Add(_chkShowAllWindows);

            Controls.Add(NewLabel("视频编码器", 12, 458, 76));
            _cboEncoder = NewCombo(88, 454, 130);
            _cboEncoder.Items.AddRange(new object[] { "libx264", "h264_nvenc", "h264_qsv", "h264_amf" });
            _cboEncoder.SelectedIndexChanged += OnGlobalChanged;
            Controls.Add(_cboEncoder);

            Controls.Add(NewLabel("速度档", 226, 458, 50));
            _cboPreset = NewCombo(276, 454, 100);
            _cboPreset.Items.AddRange(new object[]
            {
                "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow"
            });
            _cboPreset.SelectedIndexChanged += OnGlobalChanged;
            Controls.Add(_cboPreset);

            Controls.Add(NewLabel("质量 CRF", 384, 458, 62));
            _numCrf = NewNumeric(446, 454, 60, 0, 51, 0, 1);
            _numCrf.ValueChanged += OnGlobalChanged;
            Controls.Add(_numCrf);

            Controls.Add(NewLabel("（CRF 越小画质越好、文件越大；20 在 KTV 场景肉眼无损）", 512, 458, 120));

            _btnSample = NewButton("试烧样片…", 12, 488, 110);
            _btnSample.Click += OnSampleClick;
            Controls.Add(_btnSample);

            _lblSampleResult = NewLabel(string.Empty, 130, 492, 502);
            _lblSampleResult.ForeColor = Color.Gray;
            Controls.Add(_lblSampleResult);

            var lblCost = NewLabel(
                "⚠ 烧录 = 整首重编码。视频从「流拷贝几秒」变成「解码+编码一两分钟」，哪怕叠加只出现 5 秒也一样。\r\n" +
                "批量下载时建议把并发降到 1（两个 libx264 会吃满 CPU，反而更慢）。烧录失败不会丢掉歌曲，会保留无水印版本。",
                12, 520, 620);
            lblCost.Height = 46;
            lblCost.ForeColor = Color.FromArgb(180, 90, 40);
            Controls.Add(lblCost);

            var lblFidelity = NewLabel(
                "预览用 GDI+ 排版，成品用 ffmpeg 的 libfreetype，字距会有几像素差异，描边已按 borderw 语义对齐。\r\n" +
                "要确认最终效果请点「试烧样片」——那才是真成品。字重由字体文件决定，预览刻意不加粗。",
                12, 572, 620);
            lblFidelity.Height = 46;
            lblFidelity.ForeColor = Color.Gray;
            Controls.Add(lblFidelity);

            // ── 右：元素列表 ──
            Controls.Add(NewLabel("元素（后面的盖在前面的上面）", 648, 12, 350));

            _lstElements = new ListBox
            {
                Location = new Point(648, 34),
                Size = new Size(350, 132),
                IntegralHeight = false,
                HorizontalScrollbar = true
            };
            _lstElements.SelectedIndexChanged += OnListSelectionChanged;
            Controls.Add(_lstElements);

            _btnAddImage = NewButton("加图片", 648, 172, 66);
            _btnAddImage.Click += OnAddImage;
            Controls.Add(_btnAddImage);

            _btnAddText = NewButton("加文字", 719, 172, 66);
            _btnAddText.Click += OnAddText;
            Controls.Add(_btnAddText);

            _btnDelete = NewButton("删除", 790, 172, 60);
            _btnDelete.Click += OnDeleteElement;
            Controls.Add(_btnDelete);

            _btnUp = NewButton("上移", 855, 172, 66);
            _btnUp.Click += OnMoveUp;
            Controls.Add(_btnUp);

            _btnDown = NewButton("下移", 926, 172, 66);
            _btnDown.Click += OnMoveDown;
            Controls.Add(_btnDown);

            // ── 右：属性 ──
            _lblElementSummary = NewLabel(string.Empty, 648, 206, 350);
            _lblElementSummary.ForeColor = Color.DimGray;
            Controls.Add(_lblElementSummary);

            _chkVisible = NewCheck("显示这个元素", 648, 228, 130);
            _chkVisible.CheckedChanged += OnElementPropertyChanged;
            Controls.Add(_chkVisible);

            Controls.Add(NewLabel("锚点", 790, 230, 34));
            _cboAnchor = NewCombo(824, 226, 90);
            foreach (OverlayAnchor anchor in Enum.GetValues(typeof(OverlayAnchor)))
            {
                _cboAnchor.Items.Add(OverlayElement.AnchorText(anchor));
            }
            _cboAnchor.SelectedIndexChanged += OnElementPropertyChanged;
            Controls.Add(_cboAnchor);

            Controls.Add(NewLabel("边距", 922, 230, 34));
            _numMarginX = NewNumeric(956, 226, 46, 0, 2000, 0, 1);
            _numMarginX.ValueChanged += OnElementPropertyChanged;
            Controls.Add(_numMarginX);

            Controls.Add(NewLabel("位置", 648, 262, 34));
            _numMarginY = NewNumeric(682, 258, 46, 0, 2000, 0, 1);
            _numMarginY.ValueChanged += OnElementPropertyChanged;
            Controls.Add(_numMarginY);
            Controls.Add(NewLabel("X / Y（720p 基准像素，靠中/居中的那一轴不生效）", 734, 262, 254));

            _chkTimeWindow = NewCheck("只在指定时间段显示", 648, 288, 160);
            _chkTimeWindow.CheckedChanged += OnElementPropertyChanged;
            Controls.Add(_chkTimeWindow);

            Controls.Add(NewLabel("从", 812, 290, 22));
            _numStart = NewNumeric(834, 286, 62, 0, 36000, 1, (decimal)0.5);
            _numStart.ValueChanged += OnElementPropertyChanged;
            Controls.Add(_numStart);

            Controls.Add(NewLabel("到", 902, 290, 22));
            _numEnd = NewNumeric(924, 286, 62, 0, 36000, 1, (decimal)0.5);
            _numEnd.ValueChanged += OnElementPropertyChanged;
            Controls.Add(_numEnd);

            _btnMarkStart = NewButton("起点=当前播放位置", 648, 316, 160);
            _btnMarkStart.Click += delegate { MarkCurrentTime(true); };
            Controls.Add(_btnMarkStart);

            _btnMarkEnd = NewButton("终点=当前播放位置", 814, 316, 160);
            _btnMarkEnd.Click += delegate { MarkCurrentTime(false); };
            Controls.Add(_btnMarkEnd);

            // ── 图片属性 ──
            _pnlImage = new Panel { Location = new Point(648, 348), Size = new Size(350, 96) };

            var lblImagePath = NewLabel("图片", 0, 6, 34);
            _txtImagePath = new TextBox { Location = new Point(36, 2), Size = new Size(230, 23), ReadOnly = true };
            _btnBrowseImage = NewButton("浏览…", 272, 1, 76);
            _btnBrowseImage.Click += OnBrowseImage;

            var lblRelWidth = NewLabel("宽度占画面", 0, 40, 76);
            _numRelativeWidth = NewNumeric(80, 36, 56, 2, 80, 0, 1);
            _numRelativeWidth.ValueChanged += OnElementPropertyChanged;
            var lblRelWidthUnit = NewLabel("%　不透明度", 140, 40, 84);
            _numOpacity = NewNumeric(226, 36, 56, 5, 100, 0, 5);
            _numOpacity.ValueChanged += OnElementPropertyChanged;
            var lblOpacityUnit = NewLabel("%", 286, 40, 20);

            var lblImageHint = NewLabel("建议用透明 PNG。相对宽度按画面宽算，换分辨率不用重调。", 0, 70, 348);
            lblImageHint.ForeColor = Color.Gray;

            _pnlImage.Controls.AddRange(new Control[]
            {
                lblImagePath, _txtImagePath, _btnBrowseImage,
                lblRelWidth, _numRelativeWidth, lblRelWidthUnit, _numOpacity, lblOpacityUnit,
                lblImageHint
            });
            Controls.Add(_pnlImage);

            // ── 文本属性 ──
            _pnlText = new Panel { Location = new Point(648, 348), Size = new Size(350, 232) };

            var lblPreset = NewLabel("样式预设", 0, 6, 62);
            _cboTextPreset = NewCombo(64, 2, 200);
            foreach (var preset in OverlayTextPresets.All) _cboTextPreset.Items.Add(preset);
            _cboTextPreset.SelectedIndexChanged += OnTextPresetChanged;

            var lblText = NewLabel("文字", 0, 36, 34);
            _txtText = new TextBox
            {
                Location = new Point(36, 32),
                Size = new Size(312, 66),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true
            };
            _txtText.TextChanged += OnElementPropertyChanged;

            var lblFont = NewLabel("字体", 0, 108, 34);
            _txtFontFamily = new TextBox { Location = new Point(36, 104), Size = new Size(120, 23) };
            _txtFontFamily.TextChanged += OnElementPropertyChanged;

            var lblFontSize = NewLabel("字号", 164, 108, 34);
            _numFontSize = NewNumeric(200, 104, 56, 8, 400, 0, 1);
            _numFontSize.ValueChanged += OnElementPropertyChanged;

            var lblFontHint = NewLabel("（像素，720p 基准）", 260, 108, 90);
            lblFontHint.ForeColor = Color.Gray;

            _btnFillColor = NewButton("填充色", 0, 136, 76);
            _btnFillColor.Click += delegate { PickColor(true); };
            _btnBorderColor = NewButton("描边色", 82, 136, 76);
            _btnBorderColor.Click += delegate { PickColor(false); };

            var lblBorderWidth = NewLabel("描边宽", 168, 140, 50);
            _numBorderWidth = NewNumeric(220, 136, 50, 0, 40, 0, 1);
            _numBorderWidth.ValueChanged += OnElementPropertyChanged;

            _chkShadow = NewCheck("投影", 280, 138, 60);
            _chkShadow.CheckedChanged += OnElementPropertyChanged;

            var lblTextHint = NewLabel(
                "字体要能在 C:\\Windows\\Fonts 下找到。想要粗体请填粗体字体文件名\r\n" +
                "（drawtext 没有加粗选项，预览这边也刻意不加粗，两边才一致）。",
                0, 168, 348);
            lblTextHint.Height = 34;
            lblTextHint.ForeColor = Color.Gray;

            var lblMultiLine = NewLabel("换行直接按回车即可；烧录时会自动统一成 LF（避免行尾出现方框）。", 0, 204, 348);
            lblMultiLine.ForeColor = Color.Gray;

            _pnlText.Controls.AddRange(new Control[]
            {
                lblPreset, _cboTextPreset, lblText, _txtText,
                lblFont, _txtFontFamily, lblFontSize, _numFontSize, lblFontHint,
                _btnFillColor, _btnBorderColor, lblBorderWidth, _numBorderWidth, _chkShadow,
                lblTextHint, lblMultiLine
            });
            Controls.Add(_pnlText);

            // ── 底部按钮 ──
            _btnOk = NewButton("保存", 806, 656, 90);
            _btnOk.Click += OnOkClick;
            Controls.Add(_btnOk);

            _btnCancel = NewButton("取消", 906, 656, 90);
            _btnCancel.DialogResult = DialogResult.Cancel;
            Controls.Add(_btnCancel);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private static Label NewLabel(string text, int x, int y, int width)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 20),
                AutoSize = false
            };
        }

        private static CheckBox NewCheck(string text, int x, int y, int width)
        {
            return new CheckBox
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 22),
                AutoSize = false
            };
        }

        private static Button NewButton(string text, int x, int y, int width)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 26),
                AutoSize = false,
                TabStop = true
            };
        }

        private static ComboBox NewCombo(int x, int y, int width)
        {
            return new ComboBox
            {
                Location = new Point(x, y),
                Size = new Size(width, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
        }

        /// <summary>
        /// 建一个 NumericUpDown。
        /// <para>
        /// ⚠️ 必须先设 <c>Minimum</c>/<c>Maximum</c> 再设 <c>Value</c>：
        /// 反过来写的话，Value 落在旧区间外会直接抛 <c>ArgumentOutOfRangeException</c>，
        /// 而且是构造窗口时就炸，整个编辑器打不开。
        /// </para>
        /// </summary>
        private static NumericUpDown NewNumeric(
            int x, int y, int width, decimal minimum, decimal maximum, int decimalPlaces, decimal increment)
        {
            return new NumericUpDown
            {
                Location = new Point(x, y),
                Size = new Size(width, 23),
                Minimum = minimum,
                Maximum = maximum,
                DecimalPlaces = decimalPlaces,
                Increment = increment,
                Value = minimum
            };
        }

        #endregion

        #region 数据 ↔ 控件同步

        private OverlayElement CurrentElement
        {
            get
            {
                var index = _lstElements.SelectedIndex;
                return index >= 0 && index < _working.Elements.Count ? _working.Elements[index] : null;
            }
        }

        private void RefreshElementList()
        {
            var keepIndex = _lstElements.SelectedIndex;

            _syncing = true;
            try
            {
                _lstElements.BeginUpdate();
                _lstElements.Items.Clear();

                foreach (var element in _working.Elements)
                {
                    var text = element == null ? "(空)" : element.Describe();
                    if (element != null && !element.Visible) text = "［已隐藏］" + text;
                    _lstElements.Items.Add(text);
                }

                _lstElements.EndUpdate();

                if (_working.Elements.Count > 0)
                {
                    _lstElements.SelectedIndex = keepIndex >= 0 && keepIndex < _working.Elements.Count
                        ? keepIndex
                        : 0;
                }
            }
            finally
            {
                _syncing = false;
            }

            RefreshPropertyPanel();
        }

        private void RefreshGlobalControls()
        {
            _syncing = true;
            try
            {
                _chkEnabled.Checked = _working.Enabled;
                _chkShowInPreview.Checked = _working.ShowInPreview;
                _chkBurnOnDownload.Checked = _working.BurnOnDownload;

                SelectOrAdd(_cboEncoder, _working.VideoEncoder);
                SelectOrAdd(_cboPreset, _working.VideoPreset);
                SetNumeric(_numCrf, _working.VideoCrf);

                var usableForSample = _mediaService != null &&
                                      !string.IsNullOrEmpty(_sampleSource) &&
                                      File.Exists(_sampleSource);

                _btnSample.Enabled = usableForSample;
                _btnMarkStart.Enabled = _currentTimeProvider != null;
                _btnMarkEnd.Enabled = _currentTimeProvider != null;

                if (!usableForSample)
                {
                    _lblSampleResult.Text = _mediaService == null || string.IsNullOrEmpty(_sampleSource)
                        ? "（从预览窗口打开才能试烧样片）"
                        : "（样片源文件不存在）";
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private void RefreshPropertyPanel()
        {
            var element = CurrentElement;

            _syncing = true;
            try
            {
                var has = element != null;

                _chkVisible.Enabled = has;
                _cboAnchor.Enabled = has;
                _numMarginX.Enabled = has;
                _numMarginY.Enabled = has;
                _chkTimeWindow.Enabled = has;
                _numStart.Enabled = has;
                _numEnd.Enabled = has;
                _btnDelete.Enabled = has;
                _btnUp.Enabled = has;
                _btnDown.Enabled = has;
                _btnMarkStart.Enabled = has && _currentTimeProvider != null;
                _btnMarkEnd.Enabled = has && _currentTimeProvider != null;

                _pnlImage.Visible = has && element.Kind == OverlayElementKind.Image;
                _pnlText.Visible = has && element.Kind == OverlayElementKind.Text;

                if (!has)
                {
                    _lblElementSummary.Text = "还没有元素。点「加图片」放台标水印，或「加文字」做片头卡片。";
                    return;
                }

                _lblElementSummary.Text = string.Format("{0}　·　{1}",
                    element.Kind == OverlayElementKind.Image ? "图片元素" : "文字元素",
                    OverlayElement.AnchorText(element.Anchor));

                _chkVisible.Checked = element.Visible;
                _cboAnchor.SelectedIndex = (int)element.Anchor;
                SetNumeric(_numMarginX, element.MarginX);
                SetNumeric(_numMarginY, element.MarginY);

                _chkTimeWindow.Checked = element.HasTimeWindow;
                SetNumericDecimal(_numStart, (decimal)element.StartSeconds);
                SetNumericDecimal(_numEnd, (decimal)element.EndSeconds);

                if (element.Kind == OverlayElementKind.Image)
                {
                    _txtImagePath.Text = element.ImagePath;
                    SetNumericDecimal(_numRelativeWidth, (decimal)(element.RelativeWidth * 100));
                    SetNumericDecimal(_numOpacity, (decimal)(element.Opacity * 100));
                }
                else
                {
                    _txtText.Text = element.Text;
                    _txtFontFamily.Text = element.FontFamily;
                    SetNumeric(_numFontSize, element.FontSizePx);
                    SetNumeric(_numBorderWidth, element.BorderWidthPx);
                    _chkShadow.Checked = element.Shadow;

                    _btnFillColor.BackColor = OverlayRenderer.ParseColor(element.FillColor, Color.DodgerBlue);
                    _btnFillColor.Text = element.FillColor;
                    _btnFillColor.ForeColor = ContrastText(_btnFillColor.BackColor);

                    _btnBorderColor.BackColor = OverlayRenderer.ParseColor(element.BorderColor, Color.White);
                    _btnBorderColor.Text = element.BorderColor;
                    _btnBorderColor.ForeColor = ContrastText(_btnBorderColor.BackColor);

                    _cboTextPreset.SelectedIndex = MatchPreset(element);
                }
            }
            finally
            {
                _syncing = false;
            }

            _surface.SelectedElement = element;
            _surface.Invalidate();
        }

        /// <summary>深/浅背景上自动选黑字或白字，否则蓝底黑字根本看不清。</summary>
        private static Color ContrastText(Color background)
        {
            var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
            return luminance > 0.55 ? Color.Black : Color.White;
        }

        private static void SelectOrAdd(ComboBox combo, string value)
        {
            var index = combo.Items.IndexOf(value);
            if (index < 0 && !string.IsNullOrEmpty(value))
            {
                combo.Items.Add(value);
                index = combo.Items.Count - 1;
            }

            combo.SelectedIndex = index < 0 ? 0 : index;
        }

        /// <summary>
        /// 设 NumericUpDown 的值。
        /// <para>夹到区间内再赋：控件在区间外赋值会抛异常，而值可能来自用户手改的配置文件。</para>
        /// </summary>
        private static void SetNumeric(NumericUpDown numeric, int value)
        {
            SetNumericDecimal(numeric, value);
        }

        private static void SetNumericDecimal(NumericUpDown numeric, decimal value)
        {
            if (value < numeric.Minimum) value = numeric.Minimum;
            if (value > numeric.Maximum) value = numeric.Maximum;
            numeric.Value = value;
        }

        /// <summary>找出和当前元素样式一致的预设，找不到返回 -1。</summary>
        private static int MatchPreset(OverlayElement element)
        {
            var presets = OverlayTextPresets.All;
            for (var i = 0; i < presets.Count; i++)
            {
                var preset = presets[i];
                if (string.Equals(preset.FontFamily, element.FontFamily, StringComparison.Ordinal) &&
                    preset.FontSizePx == element.FontSizePx &&
                    string.Equals(preset.FillColor, element.FillColor, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(preset.BorderColor, element.BorderColor, StringComparison.OrdinalIgnoreCase) &&
                    preset.BorderWidthPx == element.BorderWidthPx &&
                    preset.Shadow == element.Shadow)
                {
                    return i;
                }
            }

            return -1;
        }

        #endregion

        #region 事件

        private void OnGlobalChanged(object sender, EventArgs e)
        {
            if (_syncing) return;

            _working.Enabled = _chkEnabled.Checked;
            _working.ShowInPreview = _chkShowInPreview.Checked;
            _working.BurnOnDownload = _chkBurnOnDownload.Checked;
            _working.VideoEncoder = _cboEncoder.SelectedItem as string ?? "libx264";
            _working.VideoPreset = _cboPreset.SelectedItem as string ?? "veryfast";
            _working.VideoCrf = (int)_numCrf.Value;

            _surface.Invalidate();
        }

        private void OnListSelectionChanged(object sender, EventArgs e)
        {
            if (_syncing) return;
            RefreshPropertyPanel();
        }

        private void OnSurfaceSelectionChanged(object sender, EventArgs e)
        {
            if (_syncing) return;

            var element = _surface.SelectedElement;
            if (element == null) return;

            var index = _working.Elements.IndexOf(element);
            if (index >= 0 && index != _lstElements.SelectedIndex)
            {
                _syncing = true;
                try { _lstElements.SelectedIndex = index; }
                finally { _syncing = false; }
            }

            RefreshPropertyPanel();
        }

        /// <summary>画布上拖动结束：把新的锚点/边距同步回属性面板，并刷新列表描述。</summary>
        private void OnSurfaceElementMoved(object sender, EventArgs e)
        {
            RefreshElementListText();
            RefreshPropertyPanel();
        }

        private void OnElementPropertyChanged(object sender, EventArgs e)
        {
            if (_syncing) return;

            var element = CurrentElement;
            if (element == null) return;

            element.Visible = _chkVisible.Checked;

            var anchorIndex = _cboAnchor.SelectedIndex;
            if (anchorIndex >= 0 && Enum.IsDefined(typeof(OverlayAnchor), anchorIndex))
                element.Anchor = (OverlayAnchor)anchorIndex;

            element.MarginX = (int)_numMarginX.Value;
            element.MarginY = (int)_numMarginY.Value;

            element.HasTimeWindow = _chkTimeWindow.Checked;
            element.StartSeconds = (double)_numStart.Value;
            element.EndSeconds = (double)_numEnd.Value;

            if (element.Kind == OverlayElementKind.Image)
            {
                element.RelativeWidth = (double)_numRelativeWidth.Value / 100.0;
                element.Opacity = (double)_numOpacity.Value / 100.0;
            }
            else
            {
                element.Text = _txtText.Text;
                element.FontFamily = _txtFontFamily.Text;
                element.FontSizePx = (int)_numFontSize.Value;
                element.BorderWidthPx = (int)_numBorderWidth.Value;
                element.Shadow = _chkShadow.Checked;
            }

            element.Normalize();

            RefreshElementListText();
            _surface.Invalidate();
        }

        /// <summary>只刷新列表文字，不动选中项（刷新整个列表会把光标位置弄丢）。</summary>
        private void RefreshElementListText()
        {
            _syncing = true;
            try
            {
                for (var i = 0; i < _working.Elements.Count && i < _lstElements.Items.Count; i++)
                {
                    var element = _working.Elements[i];
                    if (element == null) continue;

                    var text = element.Describe();
                    if (!element.Visible) text = "［已隐藏］" + text;

                    // 图片文件不见了要当场告诉用户，别等下载完才发现水印没烧上。
                    // 这个磁盘检查放在 UI 层：Models 里的 IsRenderable 刻意不碰磁盘，
                    // 理由见 OverlayElement.IsRenderable 的注释。
                    if (element.Visible &&
                        element.Kind == OverlayElementKind.Image &&
                        !string.IsNullOrWhiteSpace(element.ImagePath) &&
                        !File.Exists(element.ImagePath))
                    {
                        text += "［图片缺失］";
                    }

                    _lstElements.Items[i] = text;
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private void OnTextPresetChanged(object sender, EventArgs e)
        {
            if (_syncing) return;

            var preset = _cboTextPreset.SelectedItem as OverlayTextPreset;
            var element = CurrentElement;
            if (preset == null || element == null) return;

            preset.ApplyTo(element);
            RefreshPropertyPanel();
            _surface.Invalidate();
        }

        private void OnAddImage(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择水印图片";
                dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*";

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var element = OverlayTextPresets.CreateImageElement(dialog.FileName);
                AddElement(element);

                // 换了图必须让渲染器的缓存失效，否则预览一直显示上一张。
                OverlayRenderer.InvalidateImage(dialog.FileName);
            }
        }

        private void OnAddText(object sender, EventArgs e)
        {
            AddElement(OverlayTextPresets.CreateTextElement("欢迎光临"));
        }

        private void AddElement(OverlayElement element)
        {
            _working.Elements.Add(element);
            RefreshElementList();
            _lstElements.SelectedIndex = _working.Elements.Count - 1;
            _surface.SelectedElement = element;
            _surface.Invalidate();
        }

        private void OnDeleteElement(object sender, EventArgs e)
        {
            var index = _lstElements.SelectedIndex;
            if (index < 0 || index >= _working.Elements.Count) return;

            _working.Elements.RemoveAt(index);

            if (_working.Elements.Count == 0) _surface.SelectedElement = null;
            RefreshElementList();
        }

        private void OnMoveUp(object sender, EventArgs e)
        {
            MoveElement(-1);
        }

        private void OnMoveDown(object sender, EventArgs e)
        {
            MoveElement(1);
        }

        private void MoveElement(int delta)
        {
            var index = _lstElements.SelectedIndex;
            var target = index + delta;

            if (index < 0 || target < 0 || target >= _working.Elements.Count) return;

            var element = _working.Elements[index];
            _working.Elements[index] = _working.Elements[target];
            _working.Elements[target] = element;

            RefreshElementList();
            _lstElements.SelectedIndex = target;
            _surface.SelectedElement = element;
            _surface.Invalidate();
        }

        private void OnBrowseImage(object sender, EventArgs e)
        {
            var element = CurrentElement;
            if (element == null || element.Kind != OverlayElementKind.Image) return;

            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择水印图片";
                dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*";

                if (!string.IsNullOrEmpty(element.ImagePath) && File.Exists(element.ImagePath))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(element.ImagePath);
                    dialog.FileName = Path.GetFileName(element.ImagePath);
                }

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                element.ImagePath = dialog.FileName;
                element.Normalize();

                OverlayRenderer.InvalidateImage(dialog.FileName);

                RefreshPropertyPanel();
                RefreshElementListText();
                _surface.Invalidate();
            }
        }

        private void PickColor(bool fill)
        {
            var element = CurrentElement;
            if (element == null || element.Kind != OverlayElementKind.Text) return;

            using (var dialog = new ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.Color = OverlayRenderer.ParseColor(
                    fill ? element.FillColor : element.BorderColor, Color.White);

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var html = string.Format("#{0:X2}{1:X2}{2:X2}",
                    dialog.Color.R, dialog.Color.G, dialog.Color.B);

                if (fill) element.FillColor = html;
                else element.BorderColor = html;

                element.Normalize();
                RefreshPropertyPanel();
                _surface.Invalidate();
            }
        }

        /// <summary>
        /// 把当前播放时刻填进起点/终点。
        /// <para>
        /// 比让用户手输秒数好用得多：播到片头该出现的地方点一下、该消失的地方再点一下就完事。
        /// </para>
        /// </summary>
        private void MarkCurrentTime(bool start)
        {
            var element = CurrentElement;
            if (element == null || _currentTimeProvider == null) return;

            double seconds;
            try
            {
                seconds = _currentTimeProvider();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "取当前播放位置失败：" + ex.Message, "叠加层设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (seconds < 0) seconds = 0;

            // 自动打开时间窗口：用户点了"标记"，意图很明确就是要限定时间段，
            // 不该再要求他先去勾一个复选框。
            element.HasTimeWindow = true;

            if (start) element.StartSeconds = seconds;
            else element.EndSeconds = seconds;

            // 起止颠倒会自动纠正（Normalize 里做），但这里顺手把另一头推开一点，
            // 否则标完起点后终点还停在起点，窗口被强制拉成 0.5 秒，看着像没生效。
            if (start && element.EndSeconds <= element.StartSeconds)
                element.EndSeconds = element.StartSeconds + 5;

            element.Normalize();
            RefreshPropertyPanel();
            RefreshElementListText();
            _surface.Invalidate();
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            _working.Normalize();

            if (_working.Enabled && _working.BurnOnDownload && _working.Elements.Count == 0)
            {
                var answer = MessageBox.Show(this,
                    "已开启「下载时烧进视频」，但一个元素都没有，这样等于没开。" + Environment.NewLine +
                    "要继续保存吗？",
                    "叠加层设置", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (answer != DialogResult.Yes) return;
            }

            foreach (var element in _working.Elements)
            {
                if (element == null || !element.Visible) continue;

                var reason = element.GetSkipReason();

                // 配置本身是完整的，但图片文件读不到 —— 这条 IsRenderable/GetSkipReason
                // 刻意不管（Models 不碰磁盘，理由见 OverlayElement.IsRenderable），
                // 得在 UI 层补上。不提醒的话用户会拿到一个干干净净没水印的文件，
                // 完全不知道是图片路径的问题。
                if (reason == null &&
                    element.Kind == OverlayElementKind.Image &&
                    !string.IsNullOrWhiteSpace(element.ImagePath) &&
                    !File.Exists(element.ImagePath))
                {
                    reason = "的图片文件读不到: " + element.ImagePath;
                }

                if (reason == null) continue;

                MessageBox.Show(this,
                    string.Format("元素「{0}」{1}，下载时会被跳过。", element.Describe(), reason),
                    "叠加层设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (_store != null && !_store.Save(_working))
            {
                MessageBox.Show(this,
                    "保存失败：" + _store.LastError + Environment.NewLine +
                    "配置只在本次运行内生效。",
                    "叠加层设置", MessageBoxButtons.OK, MessageBoxIcon.Error);

                if (_logger != null) _logger.Warn("保存叠加配置失败: " + _store.LastError);
            }

            Saved = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        #endregion

        #region 试烧样片

        /// <summary>
        /// 建议的样片时长：要能覆盖到最后一个时间窗口的结束点，否则用户设了"10–15 秒"
        /// 的片头、样片只烧前 5 秒，什么都看不到，会以为功能坏了。
        /// </summary>
        internal static int SuggestSampleSeconds(OverlayComposition composition)
        {
            if (composition == null) return 5;

            var maxEnd = 0.0;
            foreach (var element in composition.Elements)
            {
                if (element == null || !element.Visible) continue;
                if (element.HasTimeWindow && element.EndSeconds > maxEnd) maxEnd = element.EndSeconds;
            }

            var seconds = (int)Math.Ceiling(maxEnd + 1.0);
            return Math.Max(5, Math.Min(30, seconds));
        }

        private async void OnSampleClick(object sender, EventArgs e)
        {
            if (_mediaService == null || string.IsNullOrEmpty(_sampleSource) || !File.Exists(_sampleSource))
            {
                MessageBox.Show(this, "没有可用的样片源。请从预览窗口打开本对话框。",
                    "试烧样片", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!_mediaService.IsFfmpegAvailable)
            {
                MessageBox.Show(this, "ffmpeg 不可用，无法试烧。请在设置里指定 ffmpeg.exe 路径。",
                    "试烧样片", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_working.Enabled || _working.GetRenderableElements().Count == 0)
            {
                MessageBox.Show(this, "没有可渲染的元素（总开关关着，或者图片/文字都还没填）。",
                    "试烧样片", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var seconds = SuggestSampleSeconds(_working);
            var output = Path.Combine(Path.GetTempPath(), "KtvDownloader-overlay-sample.mp4");

            _btnSample.Enabled = false;
            _lblSampleResult.Text = "正在烧录样片…";
            _lblSampleResult.ForeColor = Color.Gray;

            _sampleCts = new CancellationTokenSource();

            var progress = new Progress<double>(percent =>
            {
                _lblSampleResult.Text = string.Format("正在烧录样片… {0:0}%", percent);
            });

            try
            {
                var result = await _mediaService.BurnInAsync(
                    _sampleSource, output, _working, VocalOption.KeepBoth, 192,
                    (IProgress<double>)progress, seconds, _sampleCts.Token).ConfigureAwait(true);

                if (result.NothingToBurn)
                {
                    ShowSampleMessage("没有可渲染的元素：" + result.Error, Color.DarkOrange);
                    return;
                }

                if (!result.Success)
                {
                    ShowSampleMessage("烧录失败：" + result.Error, Color.Firebrick);
                    return;
                }

                foreach (var warning in result.Warnings)
                {
                    if (_logger != null) _logger.Warn("试烧样片: " + warning);
                }

                ShowSampleMessage(string.Format("样片已生成（前 {0} 秒），正在打开…{1}",
                    seconds,
                    result.Warnings.Count > 0 ? "　注意：" + result.Warnings[0] : string.Empty),
                    Color.ForestGreen);

                try
                {
                    Process.Start(output);
                }
                catch (Exception ex)
                {
                    ShowSampleMessage("样片已生成但打不开：" + output + "（" + ex.Message + "）", Color.DarkOrange);
                }
            }
            catch (OperationCanceledException)
            {
                ShowSampleMessage("已取消。", Color.Gray);
            }
            catch (Exception ex)
            {
                ShowSampleMessage("试烧异常：" + ex.Message, Color.Firebrick);
                if (_logger != null) _logger.Warn("试烧样片异常: " + ex.Message);
            }
            finally
            {
                _btnSample.Enabled = true;

                if (_sampleCts != null)
                {
                    _sampleCts.Dispose();
                    _sampleCts = null;
                }
            }
        }

        private void ShowSampleMessage(string message, Color color)
        {
            if (IsDisposed) return;

            _lblSampleResult.ForeColor = color;
            _lblSampleResult.Text = message;

            if (_logger != null) _logger.Info("试烧样片: " + message);
        }

        /// <summary>
        /// 探测样片源的真实分辨率，让画布比例和实际视频一致。
        /// <para>
        /// 拿不到就用 720p。画布比例只影响观感，坐标换算始终按元素里存的 720p 基准走，
        /// 所以探不到也不会把水印算歪。
        /// </para>
        /// </summary>
        private async void BeginProbeVideoSize()
        {
            if (_mediaService == null || string.IsNullOrEmpty(_sampleSource) || !File.Exists(_sampleSource))
                return;

            try
            {
                var probe = await _mediaService.ProbeAsync(_sampleSource).ConfigureAwait(true);
                if (IsDisposed || !probe.Usable || !probe.HasVideoSize) return;

                _surface.VideoWidth = probe.VideoWidth;
                _surface.VideoHeight = probe.VideoHeight;
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Warn("探测样片分辨率失败: " + ex.Message);
            }
        }

        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_sampleCts != null)
            {
                // 关窗时还在烧样片：先把 ffmpeg 停掉，否则它会继续跑并占着输出文件。
                try { _sampleCts.Cancel(); } catch (ObjectDisposedException) { }
                _sampleCts.Dispose();
                _sampleCts = null;
            }

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_surface != null) _surface.Composition = null;
            }

            base.Dispose(disposing);
        }
    }
}
