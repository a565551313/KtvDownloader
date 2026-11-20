using System;
using System.Drawing;
using System.Windows.Forms;

namespace KtvDownloader.Controls
{
    /// <summary>
    /// 工具栏按钮的检索模式。
    /// </summary>
    public enum BrowseMode
    {
        /// <summary>榜单：热门歌曲排行。</summary>
        Ranking = 0,

        /// <summary>歌名：按歌名浏览/搜索。</summary>
        BySongName = 1,

        /// <summary>歌星：按歌手浏览/搜索。</summary>
        BySinger = 2,

        /// <summary>分类：按语种/标签分类浏览。</summary>
        ByCategory = 3,

        /// <summary>收藏：本地收藏列表。</summary>
        Favorites = 4,

        /// <summary>新歌：最新入库歌曲。</summary>
        NewSongs = 5
    }

    /// <summary>
    /// 顶部工具栏。图标 + 文字的大按钮，对应「榜单/歌名/歌星/分类/收藏/新歌」。
    /// <para>
    /// 用 <see cref="ToolStrip"/> 而非自绘 Panel，这样在 VS 设计器里可以直接拖拽调整，
    /// 图标用 <see cref="ToolStripButton.Image"/>，文字用 <see cref="ToolStripItem.Text"/>，
    /// 并设置 <c>TextImageRelation = ImageAboveText</c> 实现上下布局。
    /// </para>
    /// </summary>
    public sealed class BrowseToolBar : ToolStrip
    {
        private ToolStripButton _btnRanking;
        private ToolStripButton _btnSongName;
        private ToolStripButton _btnSinger;
        private ToolStripButton _btnCategory;
        private ToolStripButton _btnFavorites;
        private ToolStripButton _btnNewSongs;

        /// <summary>当前选中的模式。</summary>
        public BrowseMode CurrentMode { get; private set; } = BrowseMode.BySongName;

        /// <summary>用户切换模式时触发。</summary>
        public event EventHandler<BrowseMode> ModeChanged;

        public BrowseToolBar()
        {
            InitializeButtons();
        }

        private void InitializeButtons()
        {
            GripStyle = ToolStripGripStyle.Hidden;
            ImageScalingSize = new Size(28, 28);
            Padding = new Padding(6, 4, 6, 4);
            RenderMode = ToolStripRenderMode.System;
            ShowItemToolTips = true;

            _btnRanking = CreateButton("榜单", "热门歌曲排行", CreateIcon(IconKind.Ranking));
            _btnSongName = CreateButton("歌名", "按歌名浏览与搜索", CreateIcon(IconKind.SongName));
            _btnSinger = CreateButton("歌星", "按歌手浏览与搜索", CreateIcon(IconKind.Singer));
            _btnCategory = CreateButton("分类", "按语种与标签分类浏览", CreateIcon(IconKind.Category));
            _btnFavorites = CreateButton("收藏", "我收藏的歌曲", CreateIcon(IconKind.Favorites));
            _btnNewSongs = CreateButton("新歌", "最新入库的歌曲", CreateIcon(IconKind.NewSongs));

            Items.AddRange(new ToolStripItem[]
            {
                _btnRanking, _btnSongName, _btnSinger, _btnCategory,
                new ToolStripSeparator(),
                _btnFavorites, _btnNewSongs
            });

            _btnSongName.Checked = true;

            UpdateCheckedState();
        }

        private ToolStripButton CreateButton(string text, string tooltip, Image image)
        {
            var button = new ToolStripButton
            {
                Text = text,
                ToolTipText = tooltip,
                Image = image,
                ImageAlign = ContentAlignment.TopCenter,
                TextAlign = ContentAlignment.BottomCenter,
                TextImageRelation = TextImageRelation.ImageAboveText,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                AutoSize = false,
                Size = new Size(72, 58),
                Padding = new Padding(2)
            };

            button.Click += OnButtonClick;
            return button;
        }

        private void OnButtonClick(object sender, EventArgs e)
        {
            var button = sender as ToolStripButton;
            if (button == null) return;

            BrowseMode mode;
            if (button == _btnRanking) mode = BrowseMode.Ranking;
            else if (button == _btnSongName) mode = BrowseMode.BySongName;
            else if (button == _btnSinger) mode = BrowseMode.BySinger;
            else if (button == _btnCategory) mode = BrowseMode.ByCategory;
            else if (button == _btnFavorites) mode = BrowseMode.Favorites;
            else if (button == _btnNewSongs) mode = BrowseMode.NewSongs;
            else return;

            if (mode == CurrentMode) return;

            CurrentMode = mode;
            UpdateCheckedState();

            var handler = ModeChanged;
            if (handler != null) handler(this, mode);
        }

        private void UpdateCheckedState()
        {
            _btnRanking.Checked = CurrentMode == BrowseMode.Ranking;
            _btnSongName.Checked = CurrentMode == BrowseMode.BySongName;
            _btnSinger.Checked = CurrentMode == BrowseMode.BySinger;
            _btnCategory.Checked = CurrentMode == BrowseMode.ByCategory;
            _btnFavorites.Checked = CurrentMode == BrowseMode.Favorites;
            _btnNewSongs.Checked = CurrentMode == BrowseMode.NewSongs;
        }

        /// <summary>
        /// 以代码绘制图标，避免依赖外部图片资源。
        /// 每个图标是 28x28 的简单矢量图形，风格统一。
        /// </summary>
        internal enum IconKind
        {
            Ranking, SongName, Singer, Category, Favorites, NewSongs
        }

        internal static Image CreateIcon(IconKind kind)
        {
            var bitmap = new Bitmap(28, 28);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var pen = new Pen(Color.FromArgb(70, 130, 200), 2f))
                using (var brush = new SolidBrush(Color.FromArgb(70, 130, 200)))
                using (var lightBrush = new SolidBrush(Color.FromArgb(140, 190, 235)))
                {
                    switch (kind)
                    {
                        case IconKind.Ranking:
                            // 柱状图 + 奖杯感
                            g.FillRectangle(lightBrush, 5, 18, 4, 7);
                            g.FillRectangle(brush, 11, 12, 4, 13);
                            g.FillRectangle(lightBrush, 17, 15, 4, 10);
                            g.DrawLine(pen, 3, 26, 25, 26);
                            break;

                        case IconKind.SongName:
                            // 音符
                            g.FillEllipse(brush, 6, 18, 7, 6);
                            g.DrawLine(pen, 12, 21, 12, 7);
                            g.DrawLine(pen, 12, 7, 22, 5);
                            g.DrawLine(pen, 22, 5, 22, 17);
                            g.FillEllipse(lightBrush, 18, 15, 6, 5);
                            break;

                        case IconKind.Singer:
                            // 人头像
                            g.FillEllipse(brush, 10, 4, 8, 8);
                            g.FillPie(lightBrush, 4, 14, 20, 20, 180, 180);
                            break;

                        case IconKind.Category:
                            // 四宫格
                            g.FillRectangle(lightBrush, 4, 4, 8, 8);
                            g.FillRectangle(brush, 16, 4, 8, 8);
                            g.FillRectangle(brush, 4, 16, 8, 8);
                            g.FillRectangle(lightBrush, 16, 16, 8, 8);
                            break;

                        case IconKind.Favorites:
                            // 五角星
                            var points = new PointF[10];
                            for (var i = 0; i < 10; i++)
                            {
                                var angle = -Math.PI / 2 + i * Math.PI / 5;
                                var radius = (i % 2 == 0) ? 11f : 4.5f;
                                points[i] = new PointF(
                                    (float)(14 + radius * Math.Cos(angle)),
                                    (float)(14 + radius * Math.Sin(angle)));
                            }
                            g.FillPolygon(brush, points);
                            break;

                        case IconKind.NewSongs:
                            // 时钟 + 向上箭头（新）
                            g.DrawEllipse(pen, 4, 4, 20, 20);
                            g.DrawLine(pen, 14, 8, 14, 15);
                            g.DrawLine(pen, 14, 15, 19, 18);
                            break;
                    }
                }
            }

            return bitmap;
        }
    }
}
