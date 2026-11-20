namespace KtvDownloader.Forms
{
    partial class MainForm
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.menuStrip = new System.Windows.Forms.MenuStrip();
            this.menuFile = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemSettings = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemOpenOutput = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparatorFile = new System.Windows.Forms.ToolStripSeparator();
            this.menuItemExit = new System.Windows.Forms.ToolStripMenuItem();
            this.menuLibrary = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemUpdateDatabase = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemCheckUpdate = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparatorLibrary = new System.Windows.Forms.ToolStripSeparator();
            this.menuItemPlaylists = new System.Windows.Forms.ToolStripMenuItem();
            this.menuHelp = new System.Windows.Forms.ToolStripMenuItem();
            this.menuItemAbout = new System.Windows.Forms.ToolStripMenuItem();
            this.browseToolBar = new KtvDownloader.Controls.BrowseToolBar();
            this.paginationBar = new KtvDownloader.Controls.PaginationBar();
            this.panelSearch = new System.Windows.Forms.Panel();
            this.btnSearch = new System.Windows.Forms.Button();
            this.cboLanguage = new System.Windows.Forms.ComboBox();
            this.lblLanguage = new System.Windows.Forms.Label();
            this.cboSearchType = new System.Windows.Forms.ComboBox();
            this.txtKeyword = new System.Windows.Forms.TextBox();
            this.lblKeyword = new System.Windows.Forms.Label();
            this.splitMain = new System.Windows.Forms.SplitContainer();
            this.grpResults = new System.Windows.Forms.GroupBox();
            this.lvResults = new System.Windows.Forms.ListView();
            this.colIndex = new System.Windows.Forms.ColumnHeader();
            this.colName = new System.Windows.Forms.ColumnHeader();
            this.colSinger = new System.Windows.Forms.ColumnHeader();
            this.colLanguage = new System.Windows.Forms.ColumnHeader();
            this.colMusicNo = new System.Windows.Forms.ColumnHeader();
            this.colHot = new System.Windows.Forms.ColumnHeader();
            this.lblPrefetchStatus = new System.Windows.Forms.Label();
            this.lblResultSummary = new System.Windows.Forms.Label();
            this.grpQueue = new System.Windows.Forms.GroupBox();
            this.lvQueue = new System.Windows.Forms.ListView();
            this.colQueueName = new System.Windows.Forms.ColumnHeader();
            this.colQueueState = new System.Windows.Forms.ColumnHeader();
            this.colQueueProgress = new System.Windows.Forms.ColumnHeader();
            this.panelQueueFooter = new System.Windows.Forms.Panel();
            this.btnCancelAll = new System.Windows.Forms.Button();
            this.btnClearFinished = new System.Windows.Forms.Button();
            this.panelAction = new System.Windows.Forms.Panel();
            this.cboVocal = new System.Windows.Forms.ComboBox();
            this.lblVocal = new System.Windows.Forms.Label();
            this.cboResolution = new System.Windows.Forms.ComboBox();
            this.lblResolution = new System.Windows.Forms.Label();
            this.chkConvertMp4 = new System.Windows.Forms.CheckBox();
            this.btnPreview = new System.Windows.Forms.Button();
            this.btnDownloadSelected = new System.Windows.Forms.Button();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.statusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.statusSpring = new System.Windows.Forms.ToolStripStatusLabel();
            this.statusProgress = new System.Windows.Forms.ToolStripProgressBar();
            this.menuStrip.SuspendLayout();
            this.panelSearch.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).BeginInit();
            this.splitMain.Panel1.SuspendLayout();
            this.splitMain.Panel2.SuspendLayout();
            this.splitMain.SuspendLayout();
            this.grpResults.SuspendLayout();
            this.grpQueue.SuspendLayout();
            this.panelQueueFooter.SuspendLayout();
            this.panelAction.SuspendLayout();
            this.statusStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // menuStrip
            // 
            this.menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuFile,
            this.menuLibrary,
            this.menuHelp});
            this.menuStrip.Location = new System.Drawing.Point(0, 0);
            this.menuStrip.Name = "menuStrip";
            this.menuStrip.Size = new System.Drawing.Size(1184, 25);
            this.menuStrip.TabIndex = 0;
            this.menuStrip.Text = "menuStrip";
            // 
            // menuFile
            // 
            this.menuFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuItemSettings,
            this.menuItemOpenOutput,
            this.toolStripSeparatorFile,
            this.menuItemExit});
            this.menuFile.Name = "menuFile";
            this.menuFile.Size = new System.Drawing.Size(44, 21);
            this.menuFile.Text = "文件";
            // 
            // menuLibrary
            // 
            this.menuLibrary.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuItemCheckUpdate,
            this.menuItemUpdateDatabase,
            this.toolStripSeparatorLibrary,
            this.menuItemPlaylists});
            this.menuLibrary.Name = "menuLibrary";
            this.menuLibrary.Size = new System.Drawing.Size(44, 21);
            this.menuLibrary.Text = "曲库";
            // 
            // menuItemCheckUpdate
            // 
            this.menuItemCheckUpdate.Name = "menuItemCheckUpdate";
            this.menuItemCheckUpdate.Size = new System.Drawing.Size(180, 22);
            this.menuItemCheckUpdate.Text = "检查更新";
            this.menuItemCheckUpdate.Click += new System.EventHandler(this.OnCheckUpdateClick);
            // 
            // menuItemUpdateDatabase
            // 
            this.menuItemUpdateDatabase.Name = "menuItemUpdateDatabase";
            this.menuItemUpdateDatabase.Size = new System.Drawing.Size(180, 22);
            this.menuItemUpdateDatabase.Text = "更新曲库...";
            this.menuItemUpdateDatabase.Click += new System.EventHandler(this.OnUpdateDatabaseClick);
            // 
            // toolStripSeparatorLibrary
            // 
            this.toolStripSeparatorLibrary.Name = "toolStripSeparatorLibrary";
            this.toolStripSeparatorLibrary.Size = new System.Drawing.Size(177, 6);
            // 
            // menuItemPlaylists
            // 
            this.menuItemPlaylists.Name = "menuItemPlaylists";
            this.menuItemPlaylists.Size = new System.Drawing.Size(180, 22);
            this.menuItemPlaylists.Text = "歌单与榜单...";
            this.menuItemPlaylists.Click += new System.EventHandler(this.OnPlaylistsClick);
            // 
            // menuItemSettings
            // 
            this.menuItemSettings.Name = "menuItemSettings";
            this.menuItemSettings.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O)));
            this.menuItemSettings.Size = new System.Drawing.Size(180, 22);
            this.menuItemSettings.Text = "设置...";
            this.menuItemSettings.Click += new System.EventHandler(this.OnSettingsClick);
            // 
            // menuItemOpenOutput
            // 
            this.menuItemOpenOutput.Name = "menuItemOpenOutput";
            this.menuItemOpenOutput.Size = new System.Drawing.Size(180, 22);
            this.menuItemOpenOutput.Text = "打开下载目录";
            this.menuItemOpenOutput.Click += new System.EventHandler(this.OnOpenOutputClick);
            // 
            // toolStripSeparatorFile
            // 
            this.toolStripSeparatorFile.Name = "toolStripSeparatorFile";
            this.toolStripSeparatorFile.Size = new System.Drawing.Size(177, 6);
            // 
            // menuItemExit
            // 
            this.menuItemExit.Name = "menuItemExit";
            this.menuItemExit.Size = new System.Drawing.Size(180, 22);
            this.menuItemExit.Text = "退出";
            this.menuItemExit.Click += new System.EventHandler(this.OnExitClick);
            // 
            // menuHelp
            // 
            this.menuHelp.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.menuItemAbout});
            this.menuHelp.Name = "menuHelp";
            this.menuHelp.Size = new System.Drawing.Size(44, 21);
            this.menuHelp.Text = "帮助";
            // 
            // menuItemAbout
            // 
            this.menuItemAbout.Name = "menuItemAbout";
            this.menuItemAbout.Size = new System.Drawing.Size(180, 22);
            this.menuItemAbout.Text = "关于";
            this.menuItemAbout.Click += new System.EventHandler(this.OnAboutClick);
            // 
            // browseToolBar
            // 
            this.browseToolBar.Dock = System.Windows.Forms.DockStyle.Top;
            this.browseToolBar.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.browseToolBar.Location = new System.Drawing.Point(0, 25);
            this.browseToolBar.Name = "browseToolBar";
            this.browseToolBar.Padding = new System.Windows.Forms.Padding(6, 4, 6, 4);
            this.browseToolBar.Size = new System.Drawing.Size(1184, 68);
            this.browseToolBar.TabIndex = 5;
            // 
            // panelSearch
            // 
            this.panelSearch.Controls.Add(this.btnSearch);
            this.panelSearch.Controls.Add(this.cboLanguage);
            this.panelSearch.Controls.Add(this.lblLanguage);
            this.panelSearch.Controls.Add(this.cboSearchType);
            this.panelSearch.Controls.Add(this.txtKeyword);
            this.panelSearch.Controls.Add(this.lblKeyword);
            this.panelSearch.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelSearch.Location = new System.Drawing.Point(0, 93);
            this.panelSearch.Name = "panelSearch";
            this.panelSearch.Padding = new System.Windows.Forms.Padding(8, 6, 8, 6);
            this.panelSearch.Size = new System.Drawing.Size(1184, 44);
            this.panelSearch.TabIndex = 1;
            // 
            // btnSearch
            // 
            this.btnSearch.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSearch.Location = new System.Drawing.Point(1088, 8);
            this.btnSearch.Name = "btnSearch";
            this.btnSearch.Size = new System.Drawing.Size(88, 26);
            this.btnSearch.TabIndex = 5;
            this.btnSearch.Text = "搜索";
            this.btnSearch.UseVisualStyleBackColor = true;
            this.btnSearch.Click += new System.EventHandler(this.OnSearchClick);
            // 
            // cboLanguage
            // 
            this.cboLanguage.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.cboLanguage.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboLanguage.FormattingEnabled = true;
            this.cboLanguage.Location = new System.Drawing.Point(940, 9);
            this.cboLanguage.Name = "cboLanguage";
            this.cboLanguage.Size = new System.Drawing.Size(130, 24);
            this.cboLanguage.TabIndex = 4;
            // 
            // lblLanguage
            // 
            this.lblLanguage.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblLanguage.AutoSize = true;
            this.lblLanguage.Location = new System.Drawing.Point(894, 13);
            this.lblLanguage.Name = "lblLanguage";
            this.lblLanguage.Size = new System.Drawing.Size(40, 16);
            this.lblLanguage.TabIndex = 3;
            this.lblLanguage.Text = "语种：";
            // 
            // cboSearchType
            // 
            this.cboSearchType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboSearchType.FormattingEnabled = true;
            this.cboSearchType.Location = new System.Drawing.Point(72, 9);
            this.cboSearchType.Name = "cboSearchType";
            this.cboSearchType.Size = new System.Drawing.Size(110, 24);
            this.cboSearchType.TabIndex = 2;
            // 
            // txtKeyword
            // 
            this.txtKeyword.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtKeyword.Location = new System.Drawing.Point(188, 9);
            this.txtKeyword.Name = "txtKeyword";
            this.txtKeyword.Size = new System.Drawing.Size(700, 24);
            this.txtKeyword.TabIndex = 1;
            this.txtKeyword.KeyDown += new System.Windows.Forms.KeyEventHandler(this.OnKeywordKeyDown);
            // 
            // lblKeyword
            // 
            this.lblKeyword.AutoSize = true;
            this.lblKeyword.Location = new System.Drawing.Point(11, 13);
            this.lblKeyword.Name = "lblKeyword";
            this.lblKeyword.Size = new System.Drawing.Size(56, 16);
            this.lblKeyword.TabIndex = 0;
            this.lblKeyword.Text = "搜索：";
            // 
            // splitMain
            // 
            this.splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitMain.Location = new System.Drawing.Point(0, 137);
            this.splitMain.Name = "splitMain";
            // 
            // splitMain.Panel1
            // 
            this.splitMain.Panel1.Controls.Add(this.grpResults);
            this.splitMain.Panel1MinSize = 400;
            // 
            // splitMain.Panel2
            // 
            this.splitMain.Panel2.Controls.Add(this.grpQueue);
            this.splitMain.Panel2MinSize = 280;
            this.splitMain.Size = new System.Drawing.Size(1184, 484);
            this.splitMain.SplitterDistance = 720;
            this.splitMain.TabIndex = 2;
            // 
            // grpResults
            // 
            this.grpResults.Controls.Add(this.lvResults);
            this.grpResults.Controls.Add(this.paginationBar);
            this.grpResults.Controls.Add(this.lblPrefetchStatus);
            this.grpResults.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grpResults.Location = new System.Drawing.Point(0, 0);
            this.grpResults.Name = "grpResults";
            this.grpResults.Padding = new System.Windows.Forms.Padding(6);
            this.grpResults.Size = new System.Drawing.Size(720, 552);
            this.grpResults.TabIndex = 0;
            this.grpResults.TabStop = false;
            this.grpResults.Text = "搜索结果";
            // 
            // lvResults
            // 
            this.lvResults.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.colIndex,
            this.colName,
            this.colSinger,
            this.colLanguage,
            this.colMusicNo,
            this.colHot});
            this.lvResults.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lvResults.FullRowSelect = true;
            this.lvResults.GridLines = true;
            this.lvResults.HideSelection = false;
            this.lvResults.Location = new System.Drawing.Point(6, 20);
            this.lvResults.Name = "lvResults";
            this.lvResults.Size = new System.Drawing.Size(708, 494);
            this.lvResults.TabIndex = 0;
            this.lvResults.UseCompatibleStateImageBehavior = false;
            this.lvResults.View = System.Windows.Forms.View.Details;
            this.lvResults.DoubleClick += new System.EventHandler(this.OnResultDoubleClick);
            this.lvResults.KeyDown += new System.Windows.Forms.KeyEventHandler(this.OnResultKeyDown);
            this.lvResults.MouseUp += new System.Windows.Forms.MouseEventHandler(this.OnResultMouseUp);
            this.lvResults.SelectedIndexChanged += new System.EventHandler(this.OnResultSelectionChanged);
            // 
            // colIndex
            // 
            this.colIndex.Text = "#";
            this.colIndex.Width = 40;
            // 
            // colName
            // 
            this.colName.Text = "歌名";
            this.colName.Width = 260;
            // 
            // colSinger
            // 
            this.colSinger.Text = "歌手";
            this.colSinger.Width = 160;
            // 
            // colLanguage
            // 
            this.colLanguage.Text = "语种";
            this.colLanguage.Width = 70;
            // 
            // colMusicNo
            // 
            this.colMusicNo.Text = "歌号";
            this.colMusicNo.Width = 90;
            // 
            // colHot
            // 
            this.colHot.Text = "热度";
            this.colHot.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.colHot.Width = 70;
            // 
            // lblPrefetchStatus
            // 
            this.lblPrefetchStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblPrefetchStatus.AutoSize = true;
            this.lblPrefetchStatus.ForeColor = System.Drawing.Color.Gray;
            this.lblPrefetchStatus.Location = new System.Drawing.Point(0, 0);
            this.lblPrefetchStatus.Name = "lblPrefetchStatus";
            this.lblPrefetchStatus.Size = new System.Drawing.Size(0, 16);
            this.lblPrefetchStatus.TabIndex = 3;
            this.lblPrefetchStatus.Visible = false;
            // 
            // paginationBar
            // 
            this.paginationBar.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.paginationBar.Location = new System.Drawing.Point(6, 510);
            this.paginationBar.Name = "paginationBar";
            this.paginationBar.Size = new System.Drawing.Size(708, 36);
            this.paginationBar.TabIndex = 2;
            // 
            // grpQueue
            // 
            this.grpQueue.Controls.Add(this.lvQueue);
            this.grpQueue.Controls.Add(this.panelQueueFooter);
            this.grpQueue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grpQueue.Location = new System.Drawing.Point(0, 0);
            this.grpQueue.Name = "grpQueue";
            this.grpQueue.Padding = new System.Windows.Forms.Padding(6);
            this.grpQueue.Size = new System.Drawing.Size(460, 552);
            this.grpQueue.TabIndex = 0;
            this.grpQueue.TabStop = false;
            this.grpQueue.Text = "下载队列";
            // 
            // lvQueue
            // 
            this.lvQueue.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.colQueueName,
            this.colQueueState,
            this.colQueueProgress});
            this.lvQueue.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lvQueue.FullRowSelect = true;
            this.lvQueue.GridLines = true;
            this.lvQueue.HideSelection = false;
            this.lvQueue.Location = new System.Drawing.Point(6, 20);
            this.lvQueue.Name = "lvQueue";
            this.lvQueue.Size = new System.Drawing.Size(448, 494);
            this.lvQueue.TabIndex = 0;
            this.lvQueue.UseCompatibleStateImageBehavior = false;
            this.lvQueue.View = System.Windows.Forms.View.Details;
            this.lvQueue.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.OnQueueDoubleClick);
            this.lvQueue.MouseUp += new System.Windows.Forms.MouseEventHandler(this.OnQueueMouseUp);
            // 
            // colQueueName
            // 
            this.colQueueName.Text = "歌曲";
            this.colQueueName.Width = 200;
            // 
            // colQueueState
            // 
            this.colQueueState.Text = "状态";
            this.colQueueState.Width = 130;
            // 
            // colQueueProgress
            // 
            this.colQueueProgress.Text = "进度";
            this.colQueueProgress.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.colQueueProgress.Width = 80;
            // 
            // panelQueueFooter
            // 
            this.panelQueueFooter.Controls.Add(this.btnCancelAll);
            this.panelQueueFooter.Controls.Add(this.btnClearFinished);
            this.panelQueueFooter.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelQueueFooter.Location = new System.Drawing.Point(6, 514);
            this.panelQueueFooter.Name = "panelQueueFooter";
            this.panelQueueFooter.Size = new System.Drawing.Size(448, 32);
            this.panelQueueFooter.TabIndex = 1;
            // 
            // btnCancelAll
            // 
            this.btnCancelAll.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancelAll.Location = new System.Drawing.Point(348, 4);
            this.btnCancelAll.Name = "btnCancelAll";
            this.btnCancelAll.Size = new System.Drawing.Size(88, 25);
            this.btnCancelAll.TabIndex = 1;
            this.btnCancelAll.Text = "全部取消";
            this.btnCancelAll.UseVisualStyleBackColor = true;
            this.btnCancelAll.Click += new System.EventHandler(this.OnCancelAllClick);
            // 
            // btnClearFinished
            // 
            this.btnClearFinished.Location = new System.Drawing.Point(4, 4);
            this.btnClearFinished.Name = "btnClearFinished";
            this.btnClearFinished.Size = new System.Drawing.Size(96, 25);
            this.btnClearFinished.TabIndex = 0;
            this.btnClearFinished.Text = "清除已完成";
            this.btnClearFinished.UseVisualStyleBackColor = true;
            this.btnClearFinished.Click += new System.EventHandler(this.OnClearFinishedClick);
            // 
            // panelAction
            // 
            this.panelAction.Controls.Add(this.btnPreview);
            this.panelAction.Controls.Add(this.cboVocal);
            this.panelAction.Controls.Add(this.lblVocal);
            this.panelAction.Controls.Add(this.cboResolution);
            this.panelAction.Controls.Add(this.lblResolution);
            this.panelAction.Controls.Add(this.chkConvertMp4);
            this.panelAction.Controls.Add(this.btnDownloadSelected);
            this.panelAction.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelAction.Location = new System.Drawing.Point(0, 621);
            this.panelAction.Name = "panelAction";
            this.panelAction.Padding = new System.Windows.Forms.Padding(8, 6, 8, 6);
            this.panelAction.Size = new System.Drawing.Size(1184, 44);
            this.panelAction.TabIndex = 3;
            // 
            // cboVocal
            // 
            this.cboVocal.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboVocal.FormattingEnabled = true;
            this.cboVocal.Location = new System.Drawing.Point(420, 9);
            this.cboVocal.Name = "cboVocal";
            this.cboVocal.Size = new System.Drawing.Size(150, 24);
            this.cboVocal.TabIndex = 5;
            // 
            // lblVocal
            // 
            this.lblVocal.AutoSize = true;
            this.lblVocal.Location = new System.Drawing.Point(344, 13);
            this.lblVocal.Name = "lblVocal";
            this.lblVocal.Size = new System.Drawing.Size(72, 16);
            this.lblVocal.TabIndex = 4;
            this.lblVocal.Text = "音轨处理：";
            // 
            // cboResolution
            // 
            this.cboResolution.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboResolution.FormattingEnabled = true;
            this.cboResolution.Location = new System.Drawing.Point(232, 9);
            this.cboResolution.Name = "cboResolution";
            this.cboResolution.Size = new System.Drawing.Size(100, 24);
            this.cboResolution.TabIndex = 3;
            // 
            // lblResolution
            // 
            this.lblResolution.AutoSize = true;
            this.lblResolution.Location = new System.Drawing.Point(160, 13);
            this.lblResolution.Name = "lblResolution";
            this.lblResolution.Size = new System.Drawing.Size(56, 16);
            this.lblResolution.TabIndex = 2;
            this.lblResolution.Text = "清晰度：";
            // 
            // chkConvertMp4
            // 
            this.chkConvertMp4.AutoSize = true;
            this.chkConvertMp4.Checked = true;
            this.chkConvertMp4.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkConvertMp4.Location = new System.Drawing.Point(14, 12);
            this.chkConvertMp4.Name = "chkConvertMp4";
            this.chkConvertMp4.Size = new System.Drawing.Size(130, 20);
            this.chkConvertMp4.TabIndex = 1;
            this.chkConvertMp4.Text = "转为 MP4（推荐）";
            this.chkConvertMp4.UseVisualStyleBackColor = true;
            // 
            // btnPreview
            // 
            this.btnPreview.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnPreview.Location = new System.Drawing.Point(912, 8);
            this.btnPreview.Name = "btnPreview";
            this.btnPreview.Size = new System.Drawing.Size(128, 28);
            this.btnPreview.TabIndex = 6;
            this.btnPreview.Text = "预览选中项";
            this.btnPreview.UseVisualStyleBackColor = true;
            this.btnPreview.Click += new System.EventHandler(this.OnPreviewClick);
            // 
            // btnDownloadSelected
            // 
            this.btnDownloadSelected.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnDownloadSelected.Location = new System.Drawing.Point(1048, 8);
            this.btnDownloadSelected.Name = "btnDownloadSelected";
            this.btnDownloadSelected.Size = new System.Drawing.Size(128, 28);
            this.btnDownloadSelected.TabIndex = 0;
            this.btnDownloadSelected.Text = "下载选中项";
            this.btnDownloadSelected.UseVisualStyleBackColor = true;
            this.btnDownloadSelected.Click += new System.EventHandler(this.OnDownloadSelectedClick);
            // 
            // statusStrip
            // 
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.statusLabel,
            this.statusSpring,
            this.statusProgress});
            this.statusStrip.Location = new System.Drawing.Point(0, 665);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(1184, 24);
            this.statusStrip.TabIndex = 4;
            this.statusStrip.Text = "statusStrip";
            // 
            // statusLabel
            // 
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(32, 19);
            this.statusLabel.Text = "就绪";
            // 
            // statusSpring
            // 
            this.statusSpring.Name = "statusSpring";
            this.statusSpring.Size = new System.Drawing.Size(1005, 19);
            this.statusSpring.Spring = true;
            this.statusSpring.Text = "";
            // 
            // statusProgress
            // 
            this.statusProgress.Name = "statusProgress";
            this.statusProgress.Size = new System.Drawing.Size(120, 18);
            this.statusProgress.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
            this.statusProgress.Visible = false;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1184, 689);
            this.Controls.Add(this.splitMain);
            this.Controls.Add(this.panelAction);
            this.Controls.Add(this.panelSearch);
            this.Controls.Add(this.browseToolBar);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.menuStrip);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.MainMenuStrip = this.menuStrip;
            this.MinimumSize = new System.Drawing.Size(900, 600);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "KtvDownloader - 麦动 KTV 曲库下载器";
            this.menuStrip.ResumeLayout(false);
            this.menuStrip.PerformLayout();
            this.panelSearch.ResumeLayout(false);
            this.panelSearch.PerformLayout();
            this.splitMain.Panel1.ResumeLayout(false);
            this.splitMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).EndInit();
            this.splitMain.ResumeLayout(false);
            this.grpResults.ResumeLayout(false);
            this.grpQueue.ResumeLayout(false);
            this.panelQueueFooter.ResumeLayout(false);
            this.panelAction.ResumeLayout(false);
            this.panelAction.PerformLayout();
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private KtvDownloader.Controls.BrowseToolBar browseToolBar;
        private KtvDownloader.Controls.PaginationBar paginationBar;
        private System.Windows.Forms.Label lblPrefetchStatus;
        private System.Windows.Forms.MenuStrip menuStrip;
        private System.Windows.Forms.ToolStripMenuItem menuFile;
        private System.Windows.Forms.ToolStripMenuItem menuItemSettings;
        private System.Windows.Forms.ToolStripMenuItem menuItemOpenOutput;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparatorFile;
        private System.Windows.Forms.ToolStripMenuItem menuItemExit;
        private System.Windows.Forms.ToolStripMenuItem menuLibrary;
        private System.Windows.Forms.ToolStripMenuItem menuItemCheckUpdate;
        private System.Windows.Forms.ToolStripMenuItem menuItemUpdateDatabase;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparatorLibrary;
        private System.Windows.Forms.ToolStripMenuItem menuItemPlaylists;
        private System.Windows.Forms.ToolStripMenuItem menuHelp;
        private System.Windows.Forms.ToolStripMenuItem menuItemAbout;
        private System.Windows.Forms.Panel panelSearch;
        private System.Windows.Forms.Button btnSearch;
        private System.Windows.Forms.ComboBox cboLanguage;
        private System.Windows.Forms.Label lblLanguage;
        private System.Windows.Forms.ComboBox cboSearchType;
        private System.Windows.Forms.TextBox txtKeyword;
        private System.Windows.Forms.Label lblKeyword;
        private System.Windows.Forms.SplitContainer splitMain;
        private System.Windows.Forms.GroupBox grpResults;
        private System.Windows.Forms.ListView lvResults;
        private System.Windows.Forms.ColumnHeader colIndex;
        private System.Windows.Forms.ColumnHeader colName;
        private System.Windows.Forms.ColumnHeader colSinger;
        private System.Windows.Forms.ColumnHeader colLanguage;
        private System.Windows.Forms.ColumnHeader colMusicNo;
        private System.Windows.Forms.ColumnHeader colHot;
        private System.Windows.Forms.Label lblResultSummary;
        private System.Windows.Forms.GroupBox grpQueue;
        private System.Windows.Forms.ListView lvQueue;
        private System.Windows.Forms.ColumnHeader colQueueName;
        private System.Windows.Forms.ColumnHeader colQueueState;
        private System.Windows.Forms.ColumnHeader colQueueProgress;
        private System.Windows.Forms.Panel panelQueueFooter;
        private System.Windows.Forms.Button btnCancelAll;
        private System.Windows.Forms.Button btnClearFinished;
        private System.Windows.Forms.Panel panelAction;
        private System.Windows.Forms.ComboBox cboVocal;
        private System.Windows.Forms.Label lblVocal;
        private System.Windows.Forms.ComboBox cboResolution;
        private System.Windows.Forms.Label lblResolution;
        private System.Windows.Forms.CheckBox chkConvertMp4;
        private System.Windows.Forms.Button btnPreview;
        private System.Windows.Forms.Button btnDownloadSelected;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel statusLabel;
        private System.Windows.Forms.ToolStripStatusLabel statusSpring;
        private System.Windows.Forms.ToolStripProgressBar statusProgress;
    }
}
