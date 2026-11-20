using System;
using System.Drawing;
using System.Windows.Forms;

namespace KtvDownloader.Controls
{
    /// <summary>
    /// 翻页控件。
    /// <para>
    /// 布局：[首页] [上一页] [第 n / N 页] [下一页] [尾页]  跳转到 [__] 页  [跳转]  每页 [50▼] 条
    /// </para>
    /// <para>
    /// 全部用标准 WinForms 控件，可在设计器里直接拖动调整位置与顺序。
    /// </para>
    /// </summary>
    public sealed class PaginationBar : Panel
    {
        private Button _btnFirst;
        private Button _btnPrev;
        private Button _btnNext;
        private Button _btnLast;
        private Button _btnGo;
        private Label _lblPageInfo;
        private Label _lblGoTo;
        private Label _lblPageSize;
        private NumericUpDown _numGoTo;
        private ComboBox _cboPageSize;

        private int _currentPage = 1;
        private int _totalPages = 1;
        private int _pageSize = 50;
        private int _totalCount;

        /// <summary>页码变化（从 1 开始）。</summary>
        public event EventHandler<int> PageChanged;

        /// <summary>每页条数变化。</summary>
        public event EventHandler<int> PageSizeChanged;

        /// <summary>当前页（1 起）。</summary>
        public int CurrentPage => _currentPage;

        /// <summary>总页数。</summary>
        public int TotalPages => _totalPages;

        /// <summary>每页条数。</summary>
        public int PageSize => _pageSize;

        public PaginationBar()
        {
            InitializeControls();
        }

        private void InitializeControls()
        {
            Height = 36;
            BackColor = SystemColors.Control;
            Padding = new Padding(6, 4, 6, 4);

            _btnFirst = CreateButton("首页", 60);
            _btnPrev = CreateButton("上一页", 70);
            _btnNext = CreateButton("下一页", 70);
            _btnLast = CreateButton("尾页", 60);

            _lblPageInfo = new Label
            {
                AutoSize = false,
                Width = 130,
                Height = 26,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "第 1 / 1 页"
            };

            _lblGoTo = new Label
            {
                Text = "跳转到",
                AutoSize = true,
                Location = new Point(0, 7)
            };

            _numGoTo = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 1,
                Value = 1,
                Width = 60,
                Height = 26
            };

            _btnGo = CreateButton("跳转", 56);

            _lblPageSize = new Label
            {
                Text = "每页",
                AutoSize = true,
                Location = new Point(0, 7)
            };

            _cboPageSize = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 76,
                Height = 26
            };
            _cboPageSize.Items.AddRange(new object[] { "20 条", "50 条", "100 条", "200 条" });
            _cboPageSize.SelectedIndex = 1;

            // 从左到右依次排布
            var left = 6;
            _btnFirst.Location = new Point(left, 4); left += _btnFirst.Width + 4;
            _btnPrev.Location = new Point(left, 4); left += _btnPrev.Width + 4;
            _lblPageInfo.Location = new Point(left, 4); left += _lblPageInfo.Width + 8;
            _btnNext.Location = new Point(left, 4); left += _btnNext.Width + 4;
            _btnLast.Location = new Point(left, 4); left += _btnLast.Width + 16;

            _lblGoTo.Location = new Point(left, 7); left += _lblGoTo.Width + 6;
            _numGoTo.Location = new Point(left, 4); left += _numGoTo.Width + 4;
            _btnGo.Location = new Point(left, 4); left += _btnGo.Width + 16;

            _lblPageSize.Location = new Point(left, 7); left += _lblPageSize.Width + 6;
            _cboPageSize.Location = new Point(left, 4);

            Controls.AddRange(new Control[]
            {
                _btnFirst, _btnPrev, _lblPageInfo, _btnNext, _btnLast,
                _lblGoTo, _numGoTo, _btnGo, _lblPageSize, _cboPageSize
            });

            _btnFirst.Click += (s, e) => GoToPage(1);
            _btnPrev.Click += (s, e) => GoToPage(_currentPage - 1);
            _btnNext.Click += (s, e) => GoToPage(_currentPage + 1);
            _btnLast.Click += (s, e) => GoToPage(_totalPages);
            _btnGo.Click += (s, e) => GoToPage((int)_numGoTo.Value);

            _numGoTo.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                GoToPage((int)_numGoTo.Value);
            };

            _cboPageSize.SelectedIndexChanged += OnPageSizeChanged;

            UpdateButtonStates();
        }

        private static Button CreateButton(string text, int width)
        {
            return new Button
            {
                Text = text,
                Width = width,
                Height = 26,
                UseVisualStyleBackColor = true
            };
        }

        /// <summary>
        /// 设置分页信息。totalCount 为总条数，未知时传 0（此时只按已知页数显示）。
        /// </summary>
        public void SetPagination(int currentPage, int pageSize, int totalCount)
        {
            _pageSize = Math.Max(1, pageSize);
            _totalCount = Math.Max(0, totalCount);
            _totalPages = _totalCount > 0
                ? Math.Max(1, (_totalCount + _pageSize - 1) / _pageSize)
                : 1;

            _currentPage = Math.Max(1, Math.Min(currentPage, _totalPages));

            _lblPageInfo.Text = _totalCount > 0
                ? string.Format("第 {0} / {1} 页（共 {2} 条）", _currentPage, _totalPages, _totalCount)
                : string.Format("第 {0} / {1} 页", _currentPage, _totalPages);

            _numGoTo.Maximum = _totalPages;
            _numGoTo.Value = _currentPage;

            UpdateButtonStates();
        }

        /// <summary>同步每页条数下拉框（不触发事件）。</summary>
        public void SetPageSizeSilently(int size)
        {
            _pageSize = size;

            var index = 1;
            if (size == 20) index = 0;
            else if (size == 50) index = 1;
            else if (size == 100) index = 2;
            else if (size == 200) index = 3;

            _cboPageSize.SelectedIndexChanged -= OnPageSizeChanged;
            _cboPageSize.SelectedIndex = index;
            _cboPageSize.SelectedIndexChanged += OnPageSizeChanged;
        }

        private void UpdateButtonStates()
        {
            var atFirst = _currentPage <= 1;
            var atLast = _currentPage >= _totalPages;

            _btnFirst.Enabled = !atFirst;
            _btnPrev.Enabled = !atFirst;
            _btnNext.Enabled = !atLast;
            _btnLast.Enabled = !atLast;
            _btnGo.Enabled = _totalPages > 1;
        }

        private void GoToPage(int page)
        {
            if (page < 1) page = 1;
            if (page > _totalPages) page = _totalPages;
            if (page == _currentPage) return;

            _currentPage = page;
            _numGoTo.Value = page;
            UpdateButtonStates();

            var handler = PageChanged;
            if (handler != null) handler(this, page);
        }

        private void OnPageSizeChanged(object sender, EventArgs e)
        {
            var sizes = new[] { 20, 50, 100, 200 };
            var index = _cboPageSize.SelectedIndex;
            if (index < 0 || index >= sizes.Length) return;

            var size = sizes[index];
            if (size == _pageSize) return;

            _pageSize = size;

            var handler = PageSizeChanged;
            if (handler != null) handler(this, size);
        }
    }
}
