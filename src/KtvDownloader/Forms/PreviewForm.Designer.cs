namespace KtvDownloader.Forms
{
    partial class PreviewForm
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
            this.panelTop = new System.Windows.Forms.Panel();
            this.lblTitle = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.lblStatus = new System.Windows.Forms.Label();
            this.panelBottom = new System.Windows.Forms.Panel();
            this.btnOpenFolder = new System.Windows.Forms.Button();
            this.btnClearCache = new System.Windows.Forms.Button();
            this.cboVocalMode = new System.Windows.Forms.ComboBox();
            this.lblVocalMode = new System.Windows.Forms.Label();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnPlay = new System.Windows.Forms.Button();
            this.lblVolume = new System.Windows.Forms.Label();
            this.trackVolume = new System.Windows.Forms.TrackBar();
            this.trackSeek = new System.Windows.Forms.TrackBar();
            this.lblTime = new System.Windows.Forms.Label();
            this.panelVideo = new System.Windows.Forms.Panel();
            this.panelTop.SuspendLayout();
            this.panelBottom.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.trackVolume)).BeginInit();
            this.SuspendLayout();
            // 
            // panelTop
            // 
            this.panelTop.Controls.Add(this.lblStatus);
            this.panelTop.Controls.Add(this.progressBar);
            this.panelTop.Controls.Add(this.lblTitle);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Padding = new System.Windows.Forms.Padding(10, 6, 10, 6);
            this.panelTop.Size = new System.Drawing.Size(884, 76);
            this.panelTop.TabIndex = 0;
            // 
            // lblTitle
            // 
            this.lblTitle.AutoEllipsis = true;
            this.lblTitle.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblTitle.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F, System.Drawing.FontStyle.Bold);
            this.lblTitle.Location = new System.Drawing.Point(10, 6);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(864, 24);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "预览";
            // 
            // progressBar
            // 
            this.progressBar.Dock = System.Windows.Forms.DockStyle.Top;
            this.progressBar.Location = new System.Drawing.Point(10, 30);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(864, 18);
            this.progressBar.TabIndex = 2;
            this.progressBar.Visible = false;
            // 
            // lblStatus
            // 
            this.lblStatus.AutoEllipsis = true;
            this.lblStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblStatus.ForeColor = System.Drawing.Color.DimGray;
            this.lblStatus.Location = new System.Drawing.Point(10, 48);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(864, 22);
            this.lblStatus.TabIndex = 1;
            this.lblStatus.Text = "准备中...";
            // 
            // panelBottom
            // 
            this.panelBottom.Controls.Add(this.lblTime);
            this.panelBottom.Controls.Add(this.trackSeek);
            this.panelBottom.Controls.Add(this.trackVolume);
            this.panelBottom.Controls.Add(this.lblVolume);
            this.panelBottom.Controls.Add(this.btnOpenFolder);
            this.panelBottom.Controls.Add(this.btnClearCache);
            this.panelBottom.Controls.Add(this.cboVocalMode);
            this.panelBottom.Controls.Add(this.lblVocalMode);
            this.panelBottom.Controls.Add(this.btnStop);
            this.panelBottom.Controls.Add(this.btnPlay);
            this.panelBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelBottom.Location = new System.Drawing.Point(0, 517);
            this.panelBottom.Name = "panelBottom";
            this.panelBottom.Padding = new System.Windows.Forms.Padding(10, 6, 10, 6);
            this.panelBottom.Size = new System.Drawing.Size(884, 76);
            this.panelBottom.TabIndex = 2;
            // 
            // trackSeek
            // 
            this.trackSeek.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.trackSeek.Location = new System.Drawing.Point(10, 4);
            this.trackSeek.Maximum = 1000;
            this.trackSeek.Minimum = 0;
            this.trackSeek.Name = "trackSeek";
            this.trackSeek.Size = new System.Drawing.Size(800, 45);
            this.trackSeek.TabIndex = 8;
            this.trackSeek.TickStyle = System.Windows.Forms.TickStyle.None;
            this.trackSeek.Scroll += new System.EventHandler(this.OnSeekScroll);
            this.trackSeek.MouseDown += new System.Windows.Forms.MouseEventHandler(this.OnSeekMouseDown);
            this.trackSeek.MouseUp += new System.Windows.Forms.MouseEventHandler(this.OnSeekMouseUp);
            // 
            // lblTime
            // 
            this.lblTime.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblTime.AutoSize = false;
            this.lblTime.Location = new System.Drawing.Point(816, 12);
            this.lblTime.Name = "lblTime";
            this.lblTime.Size = new System.Drawing.Size(58, 20);
            this.lblTime.TabIndex = 9;
            this.lblTime.Text = "00:00";
            this.lblTime.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // 
            // btnPlay
            // 
            this.btnPlay.Location = new System.Drawing.Point(10, 40);
            this.btnPlay.Name = "btnPlay";
            this.btnPlay.Size = new System.Drawing.Size(88, 28);
            this.btnPlay.TabIndex = 0;
            this.btnPlay.Text = "播放/暂停";
            this.btnPlay.UseVisualStyleBackColor = true;
            this.btnPlay.Click += new System.EventHandler(this.OnPlayPauseClick);
            // 
            // btnStop
            // 
            this.btnStop.Location = new System.Drawing.Point(104, 40);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(72, 28);
            this.btnStop.TabIndex = 1;
            this.btnStop.Text = "停止";
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new System.EventHandler(this.OnStopClick);
            // 
            // lblVocalMode
            // 
            this.lblVocalMode.AutoSize = true;
            this.lblVocalMode.Location = new System.Drawing.Point(190, 46);
            this.lblVocalMode.Name = "lblVocalMode";
            this.lblVocalMode.Size = new System.Drawing.Size(56, 16);
            this.lblVocalMode.TabIndex = 2;
            this.lblVocalMode.Text = "音轨：";
            // 
            // cboVocalMode
            // 
            this.cboVocalMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboVocalMode.FormattingEnabled = true;
            this.cboVocalMode.Location = new System.Drawing.Point(248, 42);
            this.cboVocalMode.Name = "cboVocalMode";
            this.cboVocalMode.Size = new System.Drawing.Size(130, 24);
            this.cboVocalMode.TabIndex = 3;
            this.cboVocalMode.SelectedIndexChanged += new System.EventHandler(this.OnVocalModeChanged);
            // 
            // lblVolume
            // 
            this.lblVolume.AutoSize = true;
            this.lblVolume.Location = new System.Drawing.Point(392, 46);
            this.lblVolume.Name = "lblVolume";
            this.lblVolume.Size = new System.Drawing.Size(40, 16);
            this.lblVolume.TabIndex = 4;
            this.lblVolume.Text = "音量：";
            // 
            // trackVolume
            // 
            this.trackVolume.Location = new System.Drawing.Point(434, 40);
            this.trackVolume.Maximum = 100;
            this.trackVolume.Minimum = 0;
            this.trackVolume.Name = "trackVolume";
            this.trackVolume.Size = new System.Drawing.Size(120, 45);
            this.trackVolume.TabIndex = 5;
            this.trackVolume.TickStyle = System.Windows.Forms.TickStyle.None;
            this.trackVolume.Value = 80;
            this.trackVolume.Scroll += new System.EventHandler(this.OnVolumeChanged);
            // 
            // btnOpenFolder
            // 
            this.btnOpenFolder.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOpenFolder.Location = new System.Drawing.Point(660, 40);
            this.btnOpenFolder.Name = "btnOpenFolder";
            this.btnOpenFolder.Size = new System.Drawing.Size(104, 28);
            this.btnOpenFolder.TabIndex = 6;
            this.btnOpenFolder.Text = "打开目录";
            this.btnOpenFolder.UseVisualStyleBackColor = true;
            this.btnOpenFolder.Click += new System.EventHandler(this.OnOpenFolderClick);
            // 
            // btnClearCache
            // 
            this.btnClearCache.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClearCache.Location = new System.Drawing.Point(770, 40);
            this.btnClearCache.Name = "btnClearCache";
            this.btnClearCache.Size = new System.Drawing.Size(104, 28);
            this.btnClearCache.TabIndex = 7;
            this.btnClearCache.Text = "清空缓存";
            this.btnClearCache.UseVisualStyleBackColor = true;
            this.btnClearCache.Click += new System.EventHandler(this.OnClearCacheClick);
            // 
            // panelVideo
            // 
            this.panelVideo.BackColor = System.Drawing.Color.Black;
            this.panelVideo.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelVideo.Location = new System.Drawing.Point(0, 76);
            this.panelVideo.Name = "panelVideo";
            this.panelVideo.Size = new System.Drawing.Size(884, 441);
            this.panelVideo.TabIndex = 1;
            // 
            // PreviewForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(884, 593);
            this.Controls.Add(this.panelVideo);
            this.Controls.Add(this.panelBottom);
            this.Controls.Add(this.panelTop);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(640, 480);
            this.Name = "PreviewForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "预览";
            this.panelTop.ResumeLayout(false);
            this.panelBottom.ResumeLayout(false);
            this.panelBottom.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.trackVolume)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel panelTop;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Panel panelBottom;
        private System.Windows.Forms.Button btnPlay;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Label lblVocalMode;
        private System.Windows.Forms.ComboBox cboVocalMode;
        private System.Windows.Forms.Label lblVolume;
        private System.Windows.Forms.TrackBar trackVolume;
        private System.Windows.Forms.TrackBar trackSeek;
        private System.Windows.Forms.Label lblTime;
        private System.Windows.Forms.Button btnOpenFolder;
        private System.Windows.Forms.Button btnClearCache;
        private System.Windows.Forms.Panel panelVideo;
    }
}
