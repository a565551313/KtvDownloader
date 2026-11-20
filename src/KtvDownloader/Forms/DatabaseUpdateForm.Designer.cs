namespace KtvDownloader.Forms
{
    partial class DatabaseUpdateForm
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
            this.lblStage = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.lblDetail = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // lblStage
            // 
            this.lblStage.AutoEllipsis = true;
            this.lblStage.Location = new System.Drawing.Point(16, 18);
            this.lblStage.Name = "lblStage";
            this.lblStage.Size = new System.Drawing.Size(452, 22);
            this.lblStage.TabIndex = 0;
            this.lblStage.Text = "准备中...";
            // 
            // progressBar
            // 
            this.progressBar.Location = new System.Drawing.Point(16, 48);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(452, 22);
            this.progressBar.TabIndex = 1;
            // 
            // lblDetail
            // 
            this.lblDetail.AutoEllipsis = true;
            this.lblDetail.Location = new System.Drawing.Point(16, 80);
            this.lblDetail.Name = "lblDetail";
            this.lblDetail.Size = new System.Drawing.Size(452, 20);
            this.lblDetail.TabIndex = 2;
            this.lblDetail.Text = "";
            // 
            // DatabaseUpdateForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(484, 116);
            this.ControlBox = false;
            this.Controls.Add(this.lblDetail);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.lblStage);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "DatabaseUpdateForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "更新曲库";
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lblStage;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label lblDetail;
    }
}
