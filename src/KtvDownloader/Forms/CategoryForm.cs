using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Services.Interfaces;

namespace KtvDownloader.Forms
{
    /// <summary>
    /// 分类浏览窗体。选择语种或标签后返回给主窗体。
    /// </summary>
    public partial class CategoryForm : Form
    {
        private readonly ISongSearchService _searchService;
        private readonly ILogger _logger;

        /// <summary>用户选择的语种。未选择时为 null。</summary>
        public string SelectedLanguage { get; private set; }

        /// <summary>用户选择的标签。未选择时为 null。</summary>
        public string SelectedTag { get; private set; }

        public CategoryForm(ISongSearchService searchService, ILogger logger)
        {
            _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            InitializeComponent();
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                // 语种按钮
                var languages = await _searchService.GetLanguagesAsync();
                panelLanguages.Controls.Clear();

                foreach (var language in languages)
                {
                    var button = new Button
                    {
                        Text = language,
                        Width = 100,
                        Height = 32,
                        Margin = new Padding(4),
                        UseVisualStyleBackColor = true
                    };

                    var captured = language;
                    button.Click += (s, args) =>
                    {
                        SelectedLanguage = captured;
                        DialogResult = DialogResult.OK;
                        Close();
                    };

                    panelLanguages.Controls.Add(button);
                }

                // 标签列表
                var tags = await _searchService.GetTagsAsync();
                lstTags.Items.Clear();
                foreach (var tag in tags) lstTags.Items.Add(tag);

                _logger.Info(string.Format("分类窗体加载完成：{0} 个语种，{1} 个标签",
                    languages.Count, tags.Count));
            }
            catch (Exception ex)
            {
                _logger.Error("加载分类失败", ex);
                lblLanguageHint.Text = "加载失败：" + ex.Message;
            }
        }

        private void OnTagDoubleClick(object sender, EventArgs e)
        {
            SubmitSelectedTag();
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            SubmitSelectedTag();
        }

        private void SubmitSelectedTag()
        {
            if (lstTags.SelectedItem == null)
            {
                MessageBox.Show("请先选择一个标签。", "分类浏览",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SelectedTag = lstTags.SelectedItem.ToString();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
