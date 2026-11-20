using System.Windows.Forms;
using KtvDownloader.Services.Interfaces;

namespace KtvDownloader.Forms
{
    /// <summary>
    /// 曲库更新进度窗体。只负责显示，业务逻辑在主窗体。
    /// </summary>
    public partial class DatabaseUpdateForm : Form
    {
        public DatabaseUpdateForm()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 更新进度显示。可从任意线程调用。
        /// </summary>
        public void UpdateProgress(DatabaseUpdateProgress progress)
        {
            if (progress == null || IsDisposed) return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new System.Action<DatabaseUpdateProgress>(UpdateProgress), progress);
                }
                catch (System.InvalidOperationException)
                {
                    // 窗体已关闭。
                }
                return;
            }

            lblStage.Text = progress.Stage;
            lblDetail.Text = progress.ToDisplayText();

            if (progress.TotalBytes.HasValue && progress.TotalBytes.Value > 0)
            {
                var percent = (int)progress.Percent;
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = System.Math.Max(0, System.Math.Min(100, percent));
            }
            else
            {
                // 解压等无总大小的阶段用跑马灯。
                progressBar.Style = ProgressBarStyle.Marquee;
            }
        }
    }
}
