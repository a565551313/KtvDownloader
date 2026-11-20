namespace KtvDownloader.Forms
{
    partial class CategoryForm
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
            this.grpLanguage = new System.Windows.Forms.GroupBox();
            this.lblLanguageHint = new System.Windows.Forms.Label();
            this.panelLanguages = new System.Windows.Forms.FlowLayoutPanel();
            this.grpTags = new System.Windows.Forms.GroupBox();
            this.lblTagHint = new System.Windows.Forms.Label();
            this.lstTags = new System.Windows.Forms.ListBox();
            this.panelButtons = new System.Windows.Forms.Panel();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnOk = new System.Windows.Forms.Button();
            this.grpLanguage.SuspendLayout();
            this.grpTags.SuspendLayout();
            this.panelButtons.SuspendLayout();
            this.SuspendLayout();
            // 
            // grpLanguage
            // 
            this.grpLanguage.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpLanguage.Controls.Add(this.lblLanguageHint);
            this.grpLanguage.Controls.Add(this.panelLanguages);
            this.grpLanguage.Location = new System.Drawing.Point(12, 12);
            this.grpLanguage.Name = "grpLanguage";
            this.grpLanguage.Padding = new System.Windows.Forms.Padding(8);
            this.grpLanguage.Size = new System.Drawing.Size(560, 130);
            this.grpLanguage.TabIndex = 0;
            this.grpLanguage.TabStop = false;
            this.grpLanguage.Text = "按语种";
            // 
            // lblLanguageHint
            // 
            this.lblLanguageHint.AutoSize = true;
            this.lblLanguageHint.ForeColor = System.Drawing.Color.Gray;
            this.lblLanguageHint.Location = new System.Drawing.Point(12, 106);
            this.lblLanguageHint.Name = "lblLanguageHint";
            this.lblLanguageHint.Size = new System.Drawing.Size(120, 16);
            this.lblLanguageHint.TabIndex = 1;
            this.lblLanguageHint.Text = "点击语种直接浏览";
            // 
            // panelLanguages
            // 
            this.panelLanguages.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panelLanguages.AutoScroll = true;
            this.panelLanguages.Location = new System.Drawing.Point(12, 26);
            this.panelLanguages.Name = "panelLanguages";
            this.panelLanguages.Size = new System.Drawing.Size(536, 74);
            this.panelLanguages.TabIndex = 0;
            // 
            // grpTags
            // 
            this.grpTags.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpTags.Controls.Add(this.lblTagHint);
            this.grpTags.Controls.Add(this.lstTags);
            this.grpTags.Location = new System.Drawing.Point(12, 148);
            this.grpTags.Name = "grpTags";
            this.grpTags.Padding = new System.Windows.Forms.Padding(8);
            this.grpTags.Size = new System.Drawing.Size(560, 226);
            this.grpTags.TabIndex = 1;
            this.grpTags.TabStop = false;
            this.grpTags.Text = "按标签";
            // 
            // lblTagHint
            // 
            this.lblTagHint.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblTagHint.AutoSize = true;
            this.lblTagHint.ForeColor = System.Drawing.Color.Gray;
            this.lblTagHint.Location = new System.Drawing.Point(12, 198);
            this.lblTagHint.Name = "lblTagHint";
            this.lblTagHint.Size = new System.Drawing.Size(136, 16);
            this.lblTagHint.TabIndex = 1;
            this.lblTagHint.Text = "双击标签直接浏览";
            // 
            // lstTags
            // 
            this.lstTags.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lstTags.FormattingEnabled = true;
            this.lstTags.ItemHeight = 16;
            this.lstTags.Location = new System.Drawing.Point(12, 26);
            this.lstTags.MultiColumn = true;
            this.lstTags.Name = "lstTags";
            this.lstTags.Size = new System.Drawing.Size(536, 164);
            this.lstTags.TabIndex = 0;
            this.lstTags.DoubleClick += new System.EventHandler(this.OnTagDoubleClick);
            // 
            // panelButtons
            // 
            this.panelButtons.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panelButtons.Controls.Add(this.btnCancel);
            this.panelButtons.Controls.Add(this.btnOk);
            this.panelButtons.Location = new System.Drawing.Point(12, 380);
            this.panelButtons.Name = "panelButtons";
            this.panelButtons.Size = new System.Drawing.Size(560, 44);
            this.panelButtons.TabIndex = 2;
            // 
            // btnCancel
            // 
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(466, 8);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(88, 28);
            this.btnCancel.TabIndex = 1;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // btnOk
            // 
            this.btnOk.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOk.Location = new System.Drawing.Point(370, 8);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(88, 28);
            this.btnOk.TabIndex = 0;
            this.btnOk.Text = "浏览选中标签";
            this.btnOk.UseVisualStyleBackColor = true;
            this.btnOk.Click += new System.EventHandler(this.OnOkClick);
            // 
            // CategoryForm
            // 
            this.AcceptButton = this.btnOk;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(584, 433);
            this.Controls.Add(this.panelButtons);
            this.Controls.Add(this.grpTags);
            this.Controls.Add(this.grpLanguage);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(500, 400);
            this.Name = "CategoryForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "分类浏览";
            this.grpLanguage.ResumeLayout(false);
            this.grpLanguage.PerformLayout();
            this.grpTags.ResumeLayout(false);
            this.grpTags.PerformLayout();
            this.panelButtons.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox grpLanguage;
        private System.Windows.Forms.Label lblLanguageHint;
        private System.Windows.Forms.FlowLayoutPanel panelLanguages;
        private System.Windows.Forms.GroupBox grpTags;
        private System.Windows.Forms.Label lblTagHint;
        private System.Windows.Forms.ListBox lstTags;
        private System.Windows.Forms.Panel panelButtons;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Button btnOk;
    }
}
