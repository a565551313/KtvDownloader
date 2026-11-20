using System;
using System.IO;
using System.Windows.Forms;
using KtvDownloader.Models;

namespace KtvDownloader.Forms
{
    /// <summary>
    /// 设置窗体。修改曲库路径、下载目录、ffmpeg 路径与默认选项。
    /// </summary>
    public partial class SettingsForm : Form
    {
        /// <summary>编辑中的配置副本，点「确定」后才写回。</summary>
        private readonly AppConfig _draft;

        public SettingsForm(AppConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            InitializeComponent();

            // 深拷贝，避免用户点「取消」时污染原配置。
            _draft = new AppConfig
            {
                DatabasePath = config.DatabasePath,
                OutputDirectory = config.OutputDirectory,
                FfmpegPath = config.FfmpegPath,
                MaxConcurrency = config.MaxConcurrency,
                DefaultResolution = config.DefaultResolution,
                DefaultVocal = config.DefaultVocal,
                ConvertToMp4 = config.ConvertToMp4,
                PageSize = config.PageSize,
                PreviewDurationSeconds = config.PreviewDurationSeconds
            };

            LoadValues();
        }

        /// <summary>把编辑后的值写回调用方传入的配置对象。</summary>
        public void ApplyTo(AppConfig target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            target.DatabasePath = _draft.DatabasePath;
            target.OutputDirectory = _draft.OutputDirectory;
            target.FfmpegPath = _draft.FfmpegPath;
            target.MaxConcurrency = _draft.MaxConcurrency;
            target.DefaultResolution = _draft.DefaultResolution;
            target.DefaultVocal = _draft.DefaultVocal;
            target.ConvertToMp4 = _draft.ConvertToMp4;
            target.PageSize = _draft.PageSize;
            target.PreviewDurationSeconds = _draft.PreviewDurationSeconds;
        }

        private void LoadValues()
        {
            txtDatabasePath.Text = _draft.DatabasePath ?? string.Empty;
            txtOutputDirectory.Text = _draft.OutputDirectory ?? string.Empty;
            txtFfmpegPath.Text = _draft.FfmpegPath ?? string.Empty;

            cboDefaultResolution.Items.Clear();
            cboDefaultResolution.Items.AddRange(new object[] { "480", "720", "1080" });
            cboDefaultResolution.SelectedItem = _draft.DefaultResolution;
            if (cboDefaultResolution.SelectedIndex < 0) cboDefaultResolution.SelectedIndex = 1;

            cboDefaultVocal.Items.Clear();
            cboDefaultVocal.Items.Add("保留原唱+伴奏");
            cboDefaultVocal.Items.Add("仅原唱");
            cboDefaultVocal.Items.Add("仅伴奏");
            cboDefaultVocal.Items.Add("原唱和伴奏各一份");
            cboDefaultVocal.SelectedIndex = (int)_draft.DefaultVocal;
            if (cboDefaultVocal.SelectedIndex < 0) cboDefaultVocal.SelectedIndex = 0;

            numConcurrency.Value = Clamp(_draft.MaxConcurrency, 1, 8);
            numPageSize.Value = Clamp(_draft.PageSize, 10, 500);
            chkDefaultConvertMp4.Checked = _draft.ConvertToMp4;

            cboPreviewDuration.Items.Clear();
            cboPreviewDuration.Items.Add("30 秒（最快）");
            cboPreviewDuration.Items.Add("60 秒（推荐）");
            cboPreviewDuration.Items.Add("120 秒");
            cboPreviewDuration.Items.Add("180 秒");
            cboPreviewDuration.Items.Add("完整播放（需下载整首）");

            var index = 1;
            switch (_draft.PreviewDurationSeconds)
            {
                case 30: index = 0; break;
                case 60: index = 1; break;
                case 120: index = 2; break;
                case 180: index = 3; break;
                case 0: index = 4; break;
                default: index = 1; break;
            }
            cboPreviewDuration.SelectedIndex = index;
        }

        private static decimal Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void OnBrowseDatabaseClick(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择曲库文件";
                dialog.Filter = "SQLite 数据库 (*.db)|*.db|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;

                var current = txtDatabasePath.Text;
                if (!string.IsNullOrWhiteSpace(current))
                {
                    var dir = Path.GetDirectoryName(current);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dialog.InitialDirectory = dir;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK) txtDatabasePath.Text = dialog.FileName;
            }
        }

        private void OnBrowseOutputClick(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择下载目录";
                dialog.ShowNewFolderButton = true;

                var current = txtOutputDirectory.Text;
                if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
                    dialog.SelectedPath = current;

                if (dialog.ShowDialog(this) == DialogResult.OK) txtOutputDirectory.Text = dialog.SelectedPath;
            }
        }

        private void OnBrowseFfmpegClick(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择 ffmpeg.exe";
                dialog.Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;

                var current = txtFfmpegPath.Text;
                if (!string.IsNullOrWhiteSpace(current))
                {
                    var dir = Path.GetDirectoryName(current);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dialog.InitialDirectory = dir;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK) txtFfmpegPath.Text = dialog.FileName;
            }
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            var databasePath = txtDatabasePath.Text.Trim();
            if (databasePath.Length > 0 && !File.Exists(databasePath))
            {
                MessageBox.Show("曲库文件不存在：" + Environment.NewLine + databasePath,
                    "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtDatabasePath.Focus();
                return;
            }

            var ffmpegPath = txtFfmpegPath.Text.Trim();
            if (ffmpegPath.Length > 0 && !File.Exists(ffmpegPath))
            {
                MessageBox.Show("ffmpeg 文件不存在：" + Environment.NewLine + ffmpegPath,
                    "设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtFfmpegPath.Focus();
                return;
            }

            _draft.DatabasePath = databasePath;
            _draft.OutputDirectory = txtOutputDirectory.Text.Trim();
            _draft.FfmpegPath = ffmpegPath;
            _draft.DefaultResolution = cboDefaultResolution.SelectedItem as string ?? "720";
            _draft.DefaultVocal = (VocalOption)Math.Max(0, cboDefaultVocal.SelectedIndex);
            _draft.MaxConcurrency = (int)numConcurrency.Value;
            _draft.PageSize = (int)numPageSize.Value;
            _draft.ConvertToMp4 = chkDefaultConvertMp4.Checked;

            switch (cboPreviewDuration.SelectedIndex)
            {
                case 0: _draft.PreviewDurationSeconds = 30; break;
                case 2: _draft.PreviewDurationSeconds = 120; break;
                case 3: _draft.PreviewDurationSeconds = 180; break;
                case 4: _draft.PreviewDurationSeconds = 0; break;
                default: _draft.PreviewDurationSeconds = 60; break;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
