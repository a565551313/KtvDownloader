namespace KtvDownloader.Forms
{
    partial class PlaylistForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        private void InitializeComponent()
        {
            this.splitPlaylist = new System.Windows.Forms.SplitContainer();
            this.grpPlaylists = new System.Windows.Forms.GroupBox();
            this.lvPlaylists = new System.Windows.Forms.ListView();
            this.colPlaylistName = new System.Windows.Forms.ColumnHeader();
            this.colPlaylistType = new System.Windows.Forms.ColumnHeader();
            this.colPlaylistCount = new System.Windows.Forms.ColumnHeader();
            this.panelPlaylistFilter = new System.Windows.Forms.Panel();
            this.cboPlaylistType = new System.Windows.Forms.ComboBox();
            this.lblPlaylistType = new System.Windows.Forms.Label();
            this.grpSongs = new System.Windows.Forms.GroupBox();
            this.lvPlaylistSongs = new System.Windows.Forms.ListView();
            this.colSongIndex = new System.Windows.Forms.ColumnHeader();
            this.colSongName = new System.Windows.Forms.ColumnHeader();
            this.colSongSinger = new System.Windows.Forms.ColumnHeader();
            this.colSongNo = new System.Windows.Forms.ColumnHeader();
            this.panelButtons = new System.Windows.Forms.Panel();
            this.lblSongSummary = new System.Windows.Forms.Label();
            this.btnAddAll = new System.Windows.Forms.Button();
            this.btnAddSelected = new System.Windows.Forms.Button();
            this.btnClose = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.splitPlaylist)).BeginInit();
            this.splitPlaylist.Panel1.SuspendLayout();
            this.splitPlaylist.Panel2.SuspendLayout();
            this.splitPlaylist.SuspendLayout();
            this.grpPlaylists.SuspendLayout();
            this.panelPlaylistFilter.SuspendLayout();
            this.grpSongs.SuspendLayout();
            this.panelButtons.SuspendLayout();
            this.SuspendLayout();
            // 
            // splitPlaylist
            // 
            this.splitPlaylist.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitPlaylist.Location = new System.Drawing.Point(0, 0);
            this.splitPlaylist.Name = "splitPlaylist";
            // 
            // splitPlaylist.Panel1
            // 
            this.splitPlaylist.Panel1.Controls.Add(this.grpPlaylists);
            this.splitPlaylist.Panel1MinSize = 260;
            // 
            // splitPlaylist.Panel2
            // 
            this.splitPlaylist.Panel2.Controls.Add(this.grpSongs);
            this.splitPlaylist.Size = new System.Drawing.Size(1004, 521);
            this.splitPlaylist.SplitterDistance = 360;
            this.splitPlaylist.TabIndex = 0;
            // 
            // grpPlaylists
            // 
            this.grpPlaylists.Controls.Add(this.lvPlaylists);
            this.grpPlaylists.Controls.Add(this.panelPlaylistFilter);
            this.grpPlaylists.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grpPlaylists.Location = new System.Drawing.Point(0, 0);
            this.grpPlaylists.Name = "grpPlaylists";
            this.grpPlaylists.Padding = new System.Windows.Forms.Padding(6);
            this.grpPlaylists.Size = new System.Drawing.Size(360, 521);
            this.grpPlaylists.TabIndex = 0;
            this.grpPlaylists.TabStop = false;
            this.grpPlaylists.Text = "歌单与榜单";
            // 
            // lvPlaylists
            // 
            this.lvPlaylists.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.colPlaylistName,
            this.colPlaylistType,
            this.colPlaylistCount});
            this.lvPlaylists.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lvPlaylists.FullRowSelect = true;
            this.lvPlaylists.GridLines = true;
            this.lvPlaylists.HideSelection = false;
            this.lvPlaylists.Location = new System.Drawing.Point(6, 56);
            this.lvPlaylists.MultiSelect = false;
            this.lvPlaylists.Name = "lvPlaylists";
            this.lvPlaylists.Size = new System.Drawing.Size(348, 459);
            this.lvPlaylists.TabIndex = 1;
            this.lvPlaylists.UseCompatibleStateImageBehavior = false;
            this.lvPlaylists.View = System.Windows.Forms.View.Details;
            this.lvPlaylists.SelectedIndexChanged += new System.EventHandler(this.OnPlaylistSelected);
            // 
            // colPlaylistName
            // 
            this.colPlaylistName.Text = "名称";
            this.colPlaylistName.Width = 200;
            // 
            // colPlaylistType
            // 
            this.colPlaylistType.Text = "类型";
            this.colPlaylistType.Width = 70;
            // 
            // colPlaylistCount
            // 
            this.colPlaylistCount.Text = "曲数";
            this.colPlaylistCount.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.colPlaylistCount.Width = 60;
            // 
            // panelPlaylistFilter
            // 
            this.panelPlaylistFilter.Controls.Add(this.cboPlaylistType);
            this.panelPlaylistFilter.Controls.Add(this.lblPlaylistType);
            this.panelPlaylistFilter.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelPlaylistFilter.Location = new System.Drawing.Point(6, 20);
            this.panelPlaylistFilter.Name = "panelPlaylistFilter";
            this.panelPlaylistFilter.Size = new System.Drawing.Size(348, 36);
            this.panelPlaylistFilter.TabIndex = 0;
            // 
            // cboPlaylistType
            // 
            this.cboPlaylistType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboPlaylistType.FormattingEnabled = true;
            this.cboPlaylistType.Location = new System.Drawing.Point(76, 6);
            this.cboPlaylistType.Name = "cboPlaylistType";
            this.cboPlaylistType.Size = new System.Drawing.Size(160, 24);
            this.cboPlaylistType.TabIndex = 1;
            this.cboPlaylistType.SelectedIndexChanged += new System.EventHandler(this.OnPlaylistTypeChanged);
            // 
            // lblPlaylistType
            // 
            this.lblPlaylistType.AutoSize = true;
            this.lblPlaylistType.Location = new System.Drawing.Point(4, 10);
            this.lblPlaylistType.Name = "lblPlaylistType";
            this.lblPlaylistType.Size = new System.Drawing.Size(56, 16);
            this.lblPlaylistType.TabIndex = 0;
            this.lblPlaylistType.Text = "类型：";
            // 
            // grpSongs
            // 
            this.grpSongs.Controls.Add(this.lvPlaylistSongs);
            this.grpSongs.Controls.Add(this.panelButtons);
            this.grpSongs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grpSongs.Location = new System.Drawing.Point(0, 0);
            this.grpSongs.Name = "grpSongs";
            this.grpSongs.Padding = new System.Windows.Forms.Padding(6);
            this.grpSongs.Size = new System.Drawing.Size(640, 521);
            this.grpSongs.TabIndex = 0;
            this.grpSongs.TabStop = false;
            this.grpSongs.Text = "曲目";
            // 
            // lvPlaylistSongs
            // 
            this.lvPlaylistSongs.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.colSongIndex,
            this.colSongName,
            this.colSongSinger,
            this.colSongNo});
            this.lvPlaylistSongs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lvPlaylistSongs.FullRowSelect = true;
            this.lvPlaylistSongs.GridLines = true;
            this.lvPlaylistSongs.HideSelection = false;
            this.lvPlaylistSongs.Location = new System.Drawing.Point(6, 20);
            this.lvPlaylistSongs.Name = "lvPlaylistSongs";
            this.lvPlaylistSongs.Size = new System.Drawing.Size(628, 455);
            this.lvPlaylistSongs.TabIndex = 0;
            this.lvPlaylistSongs.UseCompatibleStateImageBehavior = false;
            this.lvPlaylistSongs.View = System.Windows.Forms.View.Details;
            // 
            // colSongIndex
            // 
            this.colSongIndex.Text = "#";
            this.colSongIndex.Width = 50;
            // 
            // colSongName
            // 
            this.colSongName.Text = "歌名";
            this.colSongName.Width = 300;
            // 
            // colSongSinger
            // 
            this.colSongSinger.Text = "歌手";
            this.colSongSinger.Width = 180;
            // 
            // colSongNo
            // 
            this.colSongNo.Text = "歌号";
            this.colSongNo.Width = 90;
            // 
            // panelButtons
            // 
            this.panelButtons.Controls.Add(this.lblSongSummary);
            this.panelButtons.Controls.Add(this.btnAddAll);
            this.panelButtons.Controls.Add(this.btnAddSelected);
            this.panelButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelButtons.Location = new System.Drawing.Point(6, 475);
            this.panelButtons.Name = "panelButtons";
            this.panelButtons.Size = new System.Drawing.Size(628, 40);
            this.panelButtons.TabIndex = 1;
            // 
            // lblSongSummary
            // 
            this.lblSongSummary.AutoSize = true;
            this.lblSongSummary.Location = new System.Drawing.Point(4, 12);
            this.lblSongSummary.Name = "lblSongSummary";
            this.lblSongSummary.Size = new System.Drawing.Size(88, 16);
            this.lblSongSummary.TabIndex = 0;
            this.lblSongSummary.Text = "请选择歌单";
            // 
            // btnAddAll
            // 
            this.btnAddAll.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnAddAll.Enabled = false;
            this.btnAddAll.Location = new System.Drawing.Point(404, 6);
            this.btnAddAll.Name = "btnAddAll";
            this.btnAddAll.Size = new System.Drawing.Size(120, 28);
            this.btnAddAll.TabIndex = 2;
            this.btnAddAll.Text = "全部加入队列";
            this.btnAddAll.UseVisualStyleBackColor = true;
            this.btnAddAll.Click += new System.EventHandler(this.OnAddAllClick);
            // 
            // btnAddSelected
            // 
            this.btnAddSelected.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnAddSelected.Enabled = false;
            this.btnAddSelected.Location = new System.Drawing.Point(532, 6);
            this.btnAddSelected.Name = "btnAddSelected";
            this.btnAddSelected.Size = new System.Drawing.Size(92, 28);
            this.btnAddSelected.TabIndex = 1;
            this.btnAddSelected.Text = "加入队列";
            this.btnAddSelected.UseVisualStyleBackColor = true;
            this.btnAddSelected.Click += new System.EventHandler(this.OnAddSelectedClick);
            // 
            // btnClose
            // 
            this.btnClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClose.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnClose.Location = new System.Drawing.Point(904, 531);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(88, 28);
            this.btnClose.TabIndex = 1;
            this.btnClose.Text = "关闭";
            this.btnClose.UseVisualStyleBackColor = true;
            this.btnClose.Click += new System.EventHandler(this.OnCloseClick);
            // 
            // PlaylistForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnClose;
            this.ClientSize = new System.Drawing.Size(1004, 571);
            this.Controls.Add(this.btnClose);
            this.Controls.Add(this.splitPlaylist);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(800, 500);
            this.Name = "PlaylistForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "歌单与榜单";
            this.splitPlaylist.Panel1.ResumeLayout(false);
            this.splitPlaylist.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitPlaylist)).EndInit();
            this.splitPlaylist.ResumeLayout(false);
            this.grpPlaylists.ResumeLayout(false);
            this.panelPlaylistFilter.ResumeLayout(false);
            this.panelPlaylistFilter.PerformLayout();
            this.grpSongs.ResumeLayout(false);
            this.panelButtons.ResumeLayout(false);
            this.panelButtons.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer splitPlaylist;
        private System.Windows.Forms.GroupBox grpPlaylists;
        private System.Windows.Forms.ListView lvPlaylists;
        private System.Windows.Forms.ColumnHeader colPlaylistName;
        private System.Windows.Forms.ColumnHeader colPlaylistType;
        private System.Windows.Forms.ColumnHeader colPlaylistCount;
        private System.Windows.Forms.Panel panelPlaylistFilter;
        private System.Windows.Forms.ComboBox cboPlaylistType;
        private System.Windows.Forms.Label lblPlaylistType;
        private System.Windows.Forms.GroupBox grpSongs;
        private System.Windows.Forms.ListView lvPlaylistSongs;
        private System.Windows.Forms.ColumnHeader colSongIndex;
        private System.Windows.Forms.ColumnHeader colSongName;
        private System.Windows.Forms.ColumnHeader colSongSinger;
        private System.Windows.Forms.ColumnHeader colSongNo;
        private System.Windows.Forms.Panel panelButtons;
        private System.Windows.Forms.Label lblSongSummary;
        private System.Windows.Forms.Button btnAddAll;
        private System.Windows.Forms.Button btnAddSelected;
        private System.Windows.Forms.Button btnClose;
    }
}
