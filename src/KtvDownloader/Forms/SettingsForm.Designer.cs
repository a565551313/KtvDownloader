namespace KtvDownloader.Forms
{
    partial class SettingsForm
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
            this.grpPaths = new System.Windows.Forms.GroupBox();
            this.btnBrowseFfmpeg = new System.Windows.Forms.Button();
            this.txtFfmpegPath = new System.Windows.Forms.TextBox();
            this.lblFfmpeg = new System.Windows.Forms.Label();
            this.btnBrowseOutput = new System.Windows.Forms.Button();
            this.txtOutputDirectory = new System.Windows.Forms.TextBox();
            this.lblOutput = new System.Windows.Forms.Label();
            this.btnBrowseDatabase = new System.Windows.Forms.Button();
            this.txtDatabasePath = new System.Windows.Forms.TextBox();
            this.lblDatabase = new System.Windows.Forms.Label();
            this.grpDefaults = new System.Windows.Forms.GroupBox();
            this.numPageSize = new System.Windows.Forms.NumericUpDown();
            this.lblPageSize = new System.Windows.Forms.Label();
            this.numConcurrency = new System.Windows.Forms.NumericUpDown();
            this.lblConcurrency = new System.Windows.Forms.Label();
            this.chkDefaultConvertMp4 = new System.Windows.Forms.CheckBox();
            this.cboDefaultVocal = new System.Windows.Forms.ComboBox();
            this.lblDefaultVocal = new System.Windows.Forms.Label();
            this.cboDefaultResolution = new System.Windows.Forms.ComboBox();
            this.lblDefaultResolution = new System.Windows.Forms.Label();
            this.cboPreviewDuration = new System.Windows.Forms.ComboBox();
            this.lblPreviewDuration = new System.Windows.Forms.Label();
            this.panelButtons = new System.Windows.Forms.Panel();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnOk = new System.Windows.Forms.Button();
            this.grpPaths.SuspendLayout();
            this.grpDefaults.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numPageSize)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numConcurrency)).BeginInit();
            this.panelButtons.SuspendLayout();
            this.SuspendLayout();
            // 
            // grpPaths
            // 
            this.grpPaths.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpPaths.Controls.Add(this.btnBrowseFfmpeg);
            this.grpPaths.Controls.Add(this.txtFfmpegPath);
            this.grpPaths.Controls.Add(this.lblFfmpeg);
            this.grpPaths.Controls.Add(this.btnBrowseOutput);
            this.grpPaths.Controls.Add(this.txtOutputDirectory);
            this.grpPaths.Controls.Add(this.lblOutput);
            this.grpPaths.Controls.Add(this.btnBrowseDatabase);
            this.grpPaths.Controls.Add(this.txtDatabasePath);
            this.grpPaths.Controls.Add(this.lblDatabase);
            this.grpPaths.Location = new System.Drawing.Point(12, 12);
            this.grpPaths.Name = "grpPaths";
            this.grpPaths.Size = new System.Drawing.Size(660, 130);
            this.grpPaths.TabIndex = 0;
            this.grpPaths.TabStop = false;
            this.grpPaths.Text = "路径";
            // 
            // btnBrowseFfmpeg
            // 
            this.btnBrowseFfmpeg.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseFfmpeg.Location = new System.Drawing.Point(566, 92);
            this.btnBrowseFfmpeg.Name = "btnBrowseFfmpeg";
            this.btnBrowseFfmpeg.Size = new System.Drawing.Size(80, 25);
            this.btnBrowseFfmpeg.TabIndex = 8;
            this.btnBrowseFfmpeg.Text = "浏览...";
            this.btnBrowseFfmpeg.UseVisualStyleBackColor = true;
            this.btnBrowseFfmpeg.Click += new System.EventHandler(this.OnBrowseFfmpegClick);
            // 
            // txtFfmpegPath
            // 
            this.txtFfmpegPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtFfmpegPath.Location = new System.Drawing.Point(96, 92);
            this.txtFfmpegPath.Name = "txtFfmpegPath";
            this.txtFfmpegPath.Size = new System.Drawing.Size(460, 24);
            this.txtFfmpegPath.TabIndex = 7;
            // 
            // lblFfmpeg
            // 
            this.lblFfmpeg.AutoSize = true;
            this.lblFfmpeg.Location = new System.Drawing.Point(16, 96);
            this.lblFfmpeg.Name = "lblFfmpeg";
            this.lblFfmpeg.Size = new System.Drawing.Size(56, 16);
            this.lblFfmpeg.TabIndex = 6;
            this.lblFfmpeg.Text = "ffmpeg：";
            // 
            // btnBrowseOutput
            // 
            this.btnBrowseOutput.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseOutput.Location = new System.Drawing.Point(566, 58);
            this.btnBrowseOutput.Name = "btnBrowseOutput";
            this.btnBrowseOutput.Size = new System.Drawing.Size(80, 25);
            this.btnBrowseOutput.TabIndex = 5;
            this.btnBrowseOutput.Text = "浏览...";
            this.btnBrowseOutput.UseVisualStyleBackColor = true;
            this.btnBrowseOutput.Click += new System.EventHandler(this.OnBrowseOutputClick);
            // 
            // txtOutputDirectory
            // 
            this.txtOutputDirectory.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtOutputDirectory.Location = new System.Drawing.Point(96, 58);
            this.txtOutputDirectory.Name = "txtOutputDirectory";
            this.txtOutputDirectory.Size = new System.Drawing.Size(460, 24);
            this.txtOutputDirectory.TabIndex = 4;
            // 
            // lblOutput
            // 
            this.lblOutput.AutoSize = true;
            this.lblOutput.Location = new System.Drawing.Point(16, 62);
            this.lblOutput.Name = "lblOutput";
            this.lblOutput.Size = new System.Drawing.Size(72, 16);
            this.lblOutput.TabIndex = 3;
            this.lblOutput.Text = "下载目录：";
            // 
            // btnBrowseDatabase
            // 
            this.btnBrowseDatabase.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseDatabase.Location = new System.Drawing.Point(566, 24);
            this.btnBrowseDatabase.Name = "btnBrowseDatabase";
            this.btnBrowseDatabase.Size = new System.Drawing.Size(80, 25);
            this.btnBrowseDatabase.TabIndex = 2;
            this.btnBrowseDatabase.Text = "浏览...";
            this.btnBrowseDatabase.UseVisualStyleBackColor = true;
            this.btnBrowseDatabase.Click += new System.EventHandler(this.OnBrowseDatabaseClick);
            // 
            // txtDatabasePath
            // 
            this.txtDatabasePath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtDatabasePath.Location = new System.Drawing.Point(96, 24);
            this.txtDatabasePath.Name = "txtDatabasePath";
            this.txtDatabasePath.Size = new System.Drawing.Size(460, 24);
            this.txtDatabasePath.TabIndex = 1;
            // 
            // lblDatabase
            // 
            this.lblDatabase.AutoSize = true;
            this.lblDatabase.Location = new System.Drawing.Point(16, 28);
            this.lblDatabase.Name = "lblDatabase";
            this.lblDatabase.Size = new System.Drawing.Size(56, 16);
            this.lblDatabase.TabIndex = 0;
            this.lblDatabase.Text = "曲库：";
            // 
            // grpDefaults
            // 
            this.grpDefaults.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpDefaults.Controls.Add(this.cboPreviewDuration);
            this.grpDefaults.Controls.Add(this.lblPreviewDuration);
            this.grpDefaults.Controls.Add(this.numPageSize);
            this.grpDefaults.Controls.Add(this.lblPageSize);
            this.grpDefaults.Controls.Add(this.numConcurrency);
            this.grpDefaults.Controls.Add(this.lblConcurrency);
            this.grpDefaults.Controls.Add(this.chkDefaultConvertMp4);
            this.grpDefaults.Controls.Add(this.cboDefaultVocal);
            this.grpDefaults.Controls.Add(this.lblDefaultVocal);
            this.grpDefaults.Controls.Add(this.cboDefaultResolution);
            this.grpDefaults.Controls.Add(this.lblDefaultResolution);
            this.grpDefaults.Location = new System.Drawing.Point(12, 150);
            this.grpDefaults.Name = "grpDefaults";
            this.grpDefaults.Size = new System.Drawing.Size(660, 190);
            this.grpDefaults.TabIndex = 1;
            this.grpDefaults.TabStop = false;
            this.grpDefaults.Text = "默认选项";
            // 
            // numPageSize
            // 
            this.numPageSize.Location = new System.Drawing.Point(374, 66);
            this.numPageSize.Maximum = new decimal(new int[] { 500, 0, 0, 0});
            this.numPageSize.Minimum = new decimal(new int[] { 10, 0, 0, 0});
            this.numPageSize.Name = "numPageSize";
            this.numPageSize.Size = new System.Drawing.Size(90, 24);
            this.numPageSize.TabIndex = 8;
            this.numPageSize.Value = new decimal(new int[] { 50, 0, 0, 0});
            // 
            // lblPageSize
            // 
            this.lblPageSize.AutoSize = true;
            this.lblPageSize.Location = new System.Drawing.Point(274, 70);
            this.lblPageSize.Name = "lblPageSize";
            this.lblPageSize.Size = new System.Drawing.Size(72, 16);
            this.lblPageSize.TabIndex = 7;
            this.lblPageSize.Text = "每页条数：";
            // 
            // numConcurrency
            // 
            this.numConcurrency.Location = new System.Drawing.Point(132, 66);
            this.numConcurrency.Maximum = new decimal(new int[] { 8, 0, 0, 0});
            this.numConcurrency.Minimum = new decimal(new int[] { 1, 0, 0, 0});
            this.numConcurrency.Name = "numConcurrency";
            this.numConcurrency.Size = new System.Drawing.Size(90, 24);
            this.numConcurrency.TabIndex = 6;
            this.numConcurrency.Value = new decimal(new int[] { 2, 0, 0, 0});
            // 
            // lblConcurrency
            // 
            this.lblConcurrency.AutoSize = true;
            this.lblConcurrency.Location = new System.Drawing.Point(16, 70);
            this.lblConcurrency.Name = "lblConcurrency";
            this.lblConcurrency.Size = new System.Drawing.Size(96, 16);
            this.lblConcurrency.TabIndex = 5;
            this.lblConcurrency.Text = "同时下载数：";
            // 
            // cboPreviewDuration
            // 
            this.cboPreviewDuration.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboPreviewDuration.FormattingEnabled = true;
            this.cboPreviewDuration.Location = new System.Drawing.Point(374, 106);
            this.cboPreviewDuration.Name = "cboPreviewDuration";
            this.cboPreviewDuration.Size = new System.Drawing.Size(180, 24);
            this.cboPreviewDuration.TabIndex = 10;
            // 
            // lblPreviewDuration
            // 
            this.lblPreviewDuration.AutoSize = true;
            this.lblPreviewDuration.Location = new System.Drawing.Point(274, 110);
            this.lblPreviewDuration.Name = "lblPreviewDuration";
            this.lblPreviewDuration.Size = new System.Drawing.Size(72, 16);
            this.lblPreviewDuration.TabIndex = 9;
            this.lblPreviewDuration.Text = "预览时长：";
            // 
            // chkDefaultConvertMp4
            // 
            this.chkDefaultConvertMp4.AutoSize = true;
            this.chkDefaultConvertMp4.Checked = true;
            this.chkDefaultConvertMp4.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkDefaultConvertMp4.Location = new System.Drawing.Point(132, 108);
            this.chkDefaultConvertMp4.Name = "chkDefaultConvertMp4";
            this.chkDefaultConvertMp4.Size = new System.Drawing.Size(130, 20);
            this.chkDefaultConvertMp4.TabIndex = 4;
            this.chkDefaultConvertMp4.Text = "下载后转为 MP4";
            this.chkDefaultConvertMp4.UseVisualStyleBackColor = true;
            // 
            // cboDefaultVocal
            // 
            this.cboDefaultVocal.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboDefaultVocal.FormattingEnabled = true;
            this.cboDefaultVocal.Location = new System.Drawing.Point(374, 26);
            this.cboDefaultVocal.Name = "cboDefaultVocal";
            this.cboDefaultVocal.Size = new System.Drawing.Size(180, 24);
            this.cboDefaultVocal.TabIndex = 3;
            // 
            // lblDefaultVocal
            // 
            this.lblDefaultVocal.AutoSize = true;
            this.lblDefaultVocal.Location = new System.Drawing.Point(274, 30);
            this.lblDefaultVocal.Name = "lblDefaultVocal";
            this.lblDefaultVocal.Size = new System.Drawing.Size(72, 16);
            this.lblDefaultVocal.TabIndex = 2;
            this.lblDefaultVocal.Text = "音轨处理：";
            // 
            // cboDefaultResolution
            // 
            this.cboDefaultResolution.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboDefaultResolution.FormattingEnabled = true;
            this.cboDefaultResolution.Location = new System.Drawing.Point(132, 26);
            this.cboDefaultResolution.Name = "cboDefaultResolution";
            this.cboDefaultResolution.Size = new System.Drawing.Size(100, 24);
            this.cboDefaultResolution.TabIndex = 1;
            // 
            // lblDefaultResolution
            // 
            this.lblDefaultResolution.AutoSize = true;
            this.lblDefaultResolution.Location = new System.Drawing.Point(16, 30);
            this.lblDefaultResolution.Name = "lblDefaultResolution";
            this.lblDefaultResolution.Size = new System.Drawing.Size(72, 16);
            this.lblDefaultResolution.TabIndex = 0;
            this.lblDefaultResolution.Text = "默认清晰度：";
            // 
            // panelButtons
            // 
            this.panelButtons.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panelButtons.Controls.Add(this.btnCancel);
            this.panelButtons.Controls.Add(this.btnOk);
            this.panelButtons.Location = new System.Drawing.Point(12, 348);
            this.panelButtons.Name = "panelButtons";
            this.panelButtons.Size = new System.Drawing.Size(660, 44);
            this.panelButtons.TabIndex = 2;
            // 
            // btnCancel
            // 
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(566, 8);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(88, 28);
            this.btnCancel.TabIndex = 1;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // btnOk
            // 
            this.btnOk.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOk.Location = new System.Drawing.Point(470, 8);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(88, 28);
            this.btnOk.TabIndex = 0;
            this.btnOk.Text = "确定";
            this.btnOk.UseVisualStyleBackColor = true;
            this.btnOk.Click += new System.EventHandler(this.OnOkClick);
            // 
            // SettingsForm
            // 
            this.AcceptButton = this.btnOk;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(684, 401);
            this.Controls.Add(this.panelButtons);
            this.Controls.Add(this.grpDefaults);
            this.Controls.Add(this.grpPaths);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SettingsForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "设置";
            this.grpPaths.ResumeLayout(false);
            this.grpPaths.PerformLayout();
            this.grpDefaults.ResumeLayout(false);
            this.grpDefaults.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numPageSize)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numConcurrency)).EndInit();
            this.panelButtons.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox grpPaths;
        private System.Windows.Forms.Button btnBrowseFfmpeg;
        private System.Windows.Forms.TextBox txtFfmpegPath;
        private System.Windows.Forms.Label lblFfmpeg;
        private System.Windows.Forms.Button btnBrowseOutput;
        private System.Windows.Forms.TextBox txtOutputDirectory;
        private System.Windows.Forms.Label lblOutput;
        private System.Windows.Forms.Button btnBrowseDatabase;
        private System.Windows.Forms.TextBox txtDatabasePath;
        private System.Windows.Forms.Label lblDatabase;
        private System.Windows.Forms.GroupBox grpDefaults;
        private System.Windows.Forms.NumericUpDown numPageSize;
        private System.Windows.Forms.Label lblPageSize;
        private System.Windows.Forms.NumericUpDown numConcurrency;
        private System.Windows.Forms.Label lblConcurrency;
        private System.Windows.Forms.CheckBox chkDefaultConvertMp4;
        private System.Windows.Forms.ComboBox cboDefaultVocal;
        private System.Windows.Forms.Label lblDefaultVocal;
        private System.Windows.Forms.ComboBox cboDefaultResolution;
        private System.Windows.Forms.Label lblDefaultResolution;
        private System.Windows.Forms.ComboBox cboPreviewDuration;
        private System.Windows.Forms.Label lblPreviewDuration;
        private System.Windows.Forms.Panel panelButtons;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Button btnOk;
    }
}
