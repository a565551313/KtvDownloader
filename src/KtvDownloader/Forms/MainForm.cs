using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using KtvDownloader.Forms;
using KtvDownloader.Controls;
using KtvDownloader.Infrastructure.Config;
using KtvDownloader.Infrastructure.Http;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Models;
using KtvDownloader.Services;
using KtvDownloader.Services.Interfaces;

namespace KtvDownloader.Forms
{
    /// <summary>
    /// 主窗体。搜索、下载队列、曲库更新的操作入口。
    /// </summary>
    public partial class MainForm : Form
    {
        private readonly ILogger _logger;
        private readonly ConfigStore _configStore;
        private AppConfig _config;

        private SongSearchService _searchService;
        private PlaylistService _playlistService;
        private SongUrlService _urlService;
        private TsDecryptService _decryptService;
        private MediaService _mediaService;
        private DownloadService _downloadService;
        private DownloadQueue _downloadQueue;
        private DatabaseUpdateService _databaseUpdateService;
        private PreviewService _previewService;

        /// <summary>当前搜索结果。</summary>
        private readonly List<Song> _searchResults = new List<Song>();

        /// <summary>最近一次搜索的关键词与类型。</summary>
        private string _lastKeyword = string.Empty;
        private bool _lastSearchBySinger;
        private string _lastLanguage = string.Empty;

        /// <summary>最近一次解析结果（支持「歌名 歌手」格式）。</summary>
        private SearchQuery _lastQuery;

        /// <summary>是否命中了「歌名 + 歌手」组合模式。</summary>
        private bool _lastIsCombined;

        /// <summary>当前浏览模式（工具栏切换）。</summary>
        private BrowseMode _browseMode = BrowseMode.BySongName;

        /// <summary>后台预加载的完整结果缓存。翻页时直接从这里取。</summary>
        private List<Song> _prefetchCache;

        /// <summary>收藏存储。</summary>
        private FavoriteStore _favoriteStore;

        /// <summary>队列中的任务项与列表行的对应关系。</summary>
        private readonly Dictionary<Guid, ListViewItem> _queueItems = new Dictionary<Guid, ListViewItem>();

        private CancellationTokenSource _searchCts;
        private CancellationTokenSource _updateCts;

        public MainForm(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            InitializeComponent();

            _configStore = new ConfigStore(null, _logger);
            _config = _configStore.Load();

            // 配置文件里可以直接调日志级别。
            ApplyLogLevel();

            _favoriteStore = new FavoriteStore();

            InitializeServices();
        }

        /// <summary>
        /// 创建所有服务实例（**只在启动时调用一次**）。曲库路径在这里自动探测。
        /// <para>
        /// ⚠️ 保存设置、更新曲库都不要再调这个方法：它会 Dispose 掉
        /// <see cref="DownloadQueue"/>，而队列的 Dispose 是 CancelAll ——
        /// 用户只是改了个下载目录，正在跑的十几首歌就全被取消了。
        /// 那两个场景分别用 <see cref="ApplyConfigurationChanges"/> 与
        /// <see cref="RebuildDatabaseServices"/>。
        /// </para>
        /// </summary>
        private void InitializeServices()
        {
            var databasePath = ResolveAndPersistDatabasePath();

            var urlClient = new CleanHttpClient(_logger);
            _urlService = new SongUrlService(_logger, urlClient);
            _decryptService = new TsDecryptService(_logger);
            _mediaService = new MediaService(_logger, _config.FfmpegPath);
            _downloadService = new DownloadService(_logger, _urlService, _decryptService, _mediaService);
            _downloadQueue = new DownloadQueue(_logger, _downloadService, _config.MaxConcurrency);
            _databaseUpdateService = new DatabaseUpdateService(_logger);

            _searchService = new SongSearchService(_logger, databasePath);
            _playlistService = new PlaylistService(_logger, databasePath);

            // 预览走 libVLC 直接播解密后的 TS，不再需要 MediaService。
            _previewService = new PreviewService(_logger, _urlService, _decryptService);

            _downloadQueue.TaskUpdated += OnDownloadTaskUpdated;
        }

        /// <summary>
        /// 探测曲库文件，探到了就写回配置（下次启动不用再找）。
        /// </summary>
        private string ResolveAndPersistDatabasePath()
        {
            var databasePath = DatabasePathResolver.Resolve(_config.DatabasePath);
            if (databasePath != null && databasePath != _config.DatabasePath)
            {
                _config.DatabasePath = databasePath;
                _configStore.Save(_config);
                _logger.Info("自动探测到曲库: " + databasePath);
            }
            return databasePath;
        }

        /// <summary>
        /// 只重建「曲库相关」的服务：搜索与歌单。
        /// <para>
        /// 这两个服务不参与下载管线，重建它们不会碰到正在跑的任务 ——
        /// 曲库更新完成后、或者用户在设置里换了曲库路径，都用这条路径。
        /// </para>
        /// </summary>
        private void RebuildDatabaseServices()
        {
            var databasePath = ResolveAndPersistDatabasePath();

            if (_searchService != null)
            {
                _searchService.Dispose();
                _searchService = null;
            }

            _searchService = new SongSearchService(_logger, databasePath);
            _playlistService = new PlaylistService(_logger, databasePath);

            // 换了曲库，旧结果与新曲库对不上，直接清掉，别让用户翻到一半发现歌号不存在。
            _prefetchCache = null;
        }

        /// <summary>
        /// 设置保存后**原地**应用变更，不重建下载管线。
        /// </summary>
        /// <param name="oldDatabasePath">保存前的曲库路径，用于判断是否真的变了。</param>
        private void ApplyConfigurationChanges(string oldDatabasePath)
        {
            // 1) 并发数：队列支持热调整（内部换一个信号量，在跑的任务不受影响）。
            if (_downloadQueue != null) _downloadQueue.SetMaxConcurrency(_config.MaxConcurrency);

            // 2) ffmpeg 路径：原地更新，下一次调用重新解析。
            if (_mediaService != null) _mediaService.UpdateFfmpegPath(_config.FfmpegPath);

            // 3) 日志级别。
            ApplyLogLevel();

            // 4) 曲库路径变了才重建搜索/歌单服务。
            if (!string.Equals(oldDatabasePath, _config.DatabasePath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("曲库路径变更，重建搜索服务: " + _config.DatabasePath);
                RebuildDatabaseServices();
            }
        }

        private void ApplyLogLevel()
        {
            var fileLogger = _logger as FileLogger;
            if (fileLogger == null) return;
            fileLogger.MinimumLevel = LogLevelParser.Parse(_config.LogLevel, LogLevel.Debug);
        }

        /// <summary>
        /// 释放全部服务。**只在窗体关闭时调用**。
        /// </summary>
        private void DisposeServices()
        {
            if (_downloadQueue != null)
            {
                _downloadQueue.TaskUpdated -= OnDownloadTaskUpdated;
                _downloadQueue.Dispose();
                _downloadQueue = null;
            }

            // 下载服务与预览服务各自持有 HttpClient，不释放会累积连接池与 socket。
            DisposeQuietly(ref _downloadService);
            DisposeQuietly(ref _previewService);
            DisposeQuietly(ref _databaseUpdateService);
            DisposeQuietly(ref _searchService);

            _playlistService = null;
            _urlService = null;
            _decryptService = null;
            _mediaService = null;
        }

        /// <summary>
        /// 释放并置空。关闭阶段的异常没有意义，吞掉即可，
        /// 但一定要继续释放后面的对象 —— 所以每个都单独 try。
        /// </summary>
        private void DisposeQuietly<T>(ref T service) where T : class, IDisposable
        {
            if (service == null) return;
            try { service.Dispose(); }
            catch (Exception ex) { _logger.Warn("释放 " + typeof(T).Name + " 失败: " + ex.Message); }
            service = null;
        }

        /// <summary>
        /// 窗体加载：初始化下拉框、检查曲库。
        /// </summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            InitializeControls();

            // 曲库检查必须等窗体真正显示后再做，
            // 在 OnLoad 里弹模态框会卡住窗体的首次显示。
            BeginInvoke(new Action(CheckDatabase));
        }

        /// <summary>初始化界面控件的初始值。</summary>
        private void InitializeControls()
        {
            cboSearchType.Items.Clear();
            cboSearchType.Items.Add("按歌名");
            cboSearchType.Items.Add("按歌手");
            cboSearchType.SelectedIndex = 0;

            cboResolution.Items.Clear();
            cboResolution.Items.AddRange(new object[] { "480", "720", "1080" });
            cboResolution.SelectedItem = _config.DefaultResolution;
            if (cboResolution.SelectedIndex < 0) cboResolution.SelectedIndex = 1;

            cboVocal.Items.Clear();
            cboVocal.Items.Add("保留原唱+伴奏");
            cboVocal.Items.Add("仅原唱");
            cboVocal.Items.Add("仅伴奏");
            cboVocal.Items.Add("原唱和伴奏各一份");
            cboVocal.SelectedIndex = (int)_config.DefaultVocal;
            if (cboVocal.SelectedIndex < 0) cboVocal.SelectedIndex = 0;

            chkConvertMp4.Checked = _config.ConvertToMp4;

            cboLanguage.Items.Clear();
            cboLanguage.Items.Add("全部");
            cboLanguage.SelectedIndex = 0;

            // 分页栏初始值与配置同步。
            paginationBar.SetPageSizeSilently(_config.PageSize);
            paginationBar.SetPagination(1, _config.PageSize, 0);

            // 事件绑定。
            browseToolBar.ModeChanged += OnBrowseModeChanged;
            paginationBar.PageChanged += OnPageChanged;
            paginationBar.PageSizeChanged += OnPageSizeChanged;

            UpdateStatus("就绪");
        }

        /// <summary>检查曲库是否可用，并载入语种列表。</summary>
        private async void CheckDatabase()
        {
            var path = _config.DatabasePath;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                statusSpring.Text = "曲库未配置";

                var result = MessageBox.Show(
                    "未找到曲库文件 muse.db。" + Environment.NewLine + Environment.NewLine +
                    "点击「确定」可以立即从官方服务下载曲库（约 418MB），" + Environment.NewLine +
                    "也可以点「取消」后到「文件 → 设置」手动指定。" + Environment.NewLine + Environment.NewLine +
                    "如果机器上已有 muse.db，程序会自动探测到它。",
                    "KtvDownloader", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);

                if (result == DialogResult.OK) await UpdateDatabaseAsync();
                return;
            }

            statusSpring.Text = "正在打开曲库...";
            UpdateStatus("正在加载曲库");

            try
            {
                var available = await Task.Run(() => _searchService.IsAvailable);
                if (!available)
                {
                    statusSpring.Text = "曲库打开失败";
                    UpdateStatus("曲库打开失败");
                    MessageBox.Show("曲库文件存在但无法打开，可能已损坏。" + Environment.NewLine + path,
                        "KtvDownloader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var total = await Task.Run(() => _searchService.TotalSongCount);
                var languages = await _searchService.GetLanguagesAsync();

                foreach (var language in languages)
                {
                    if (!cboLanguage.Items.Contains(language)) cboLanguage.Items.Add(language);
                }

                var version = _databaseUpdateService.ReadLocalVersion(_config.DatabasePath);
                statusSpring.Text = string.Format("曲库：{0} 首  版本 {1}  {2}",
                    total, version ?? "未知", Path.GetFileName(_config.DatabasePath));

                UpdateStatus("曲库已就绪");
                _logger.Info("曲库加载完成，共 " + total + " 首");
            }
            catch (Exception ex)
            {
                _logger.Error("加载曲库失败", ex);
                statusSpring.Text = "曲库加载失败";
                UpdateStatus("曲库加载失败：" + ex.Message);
            }
        }

        /// <summary>更新状态栏文案。</summary>
        private void UpdateStatus(string text)
        {
            statusLabel.Text = text;
        }

        #region 搜索与浏览

        /// <summary>
        /// 工具栏切换浏览模式。
        /// </summary>
        private async void OnBrowseModeChanged(object sender, BrowseMode mode)
        {
            _browseMode = mode;

            // 不同模式的界面元素状态不同。
            var isSearchMode = mode == BrowseMode.BySongName || mode == BrowseMode.BySinger;
            txtKeyword.Enabled = isSearchMode;
            cboSearchType.Enabled = isSearchMode;
            cboLanguage.Enabled = mode == BrowseMode.ByCategory || isSearchMode;

            switch (mode)
            {
                case BrowseMode.Ranking:
                    txtKeyword.Text = string.Empty;
                    cboLanguage.SelectedIndex = 0;
                    await LoadRankingAsync(1);
                    break;

                case BrowseMode.BySongName:
                    cboSearchType.SelectedIndex = 0;
                    UpdateStatus("按歌名搜索");
                    txtKeyword.Focus();
                    break;

                case BrowseMode.BySinger:
                    cboSearchType.SelectedIndex = 1;
                    UpdateStatus("按歌手搜索");
                    txtKeyword.Focus();
                    break;

                case BrowseMode.ByCategory:
                    await LoadCategoriesAsync();
                    break;

                case BrowseMode.Favorites:
                    await LoadFavoritesAsync();
                    break;

                case BrowseMode.NewSongs:
                    await LoadNewSongsAsync(1);
                    break;
            }
        }

        /// <summary>榜单：按热度倒序。</summary>
        private async Task LoadRankingAsync(int page)
        {
            await LoadPageAsync(
                "榜单", page,
                (offset, limit, ct) => _searchService.GetHotSongsAsync(offset, limit, ct),
                null);
        }

        /// <summary>新歌：按歌号倒序近似最新入库。</summary>
        private async Task LoadNewSongsAsync(int page)
        {
            await LoadPageAsync(
                "新歌", page,
                (offset, limit, ct) => _searchService.GetNewSongsAsync(offset, limit, ct),
                null);
        }

        /// <summary>分类：先弹语种选择，再按语种加载。</summary>
        private async Task LoadCategoriesAsync()
        {
            var languages = cboLanguage.Items.Cast<object>()
                .Select(o => o.ToString())
                .Where(s => s != "全部")
                .ToList();

            if (languages.Count == 0)
            {
                UpdateStatus("没有可用的分类");
                return;
            }

            using (var dialog = new CategoryForm(_searchService, _logger))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                if (dialog.SelectedLanguage != null)
                {
                    var lang = dialog.SelectedLanguage;
                    await LoadPageAsync(
                        "分类：" + lang, 1,
                        (offset, limit, ct) => _searchService.GetSongsByLanguageAsync(lang, offset, limit, ct),
                        null);
                }
                else if (dialog.SelectedTag != null)
                {
                    var tag = dialog.SelectedTag;
                    await LoadPageAsync(
                        "分类：" + tag, 1,
                        (offset, limit, ct) => _searchService.GetSongsByTagAsync(tag, offset, limit, ct),
                        null);
                }
            }
        }

        /// <summary>收藏：从本地收藏文件读取。</summary>
        private async Task LoadFavoritesAsync()
        {
            try
            {
                var favorites = _favoriteStore.Load();
                if (favorites.Count == 0)
                {
                    _searchResults.Clear();
                    lvResults.Items.Clear();
                    lblResultSummary.Text = "尚无收藏";
                    paginationBar.SetPagination(1, _config.PageSize, 0);
                    UpdateStatus("收藏为空，可在搜索结果右键添加收藏");
                    return;
                }

                // 按歌号取回完整信息。
                var songs = new List<Song>();
                foreach (var musicNo in favorites)
                {
                    var song = await _searchService.GetByMusicNoAsync(musicNo);
                    if (song != null) songs.Add(song);
                }

                _searchResults.Clear();
                _searchResults.AddRange(songs);
                AppendSearchResults(songs, true);
                paginationBar.SetPagination(1, _config.PageSize, songs.Count);
                lblResultSummary.Text = string.Format("收藏 {0} 首", songs.Count);
                UpdateStatus("已加载收藏");
            }
            catch (Exception ex)
            {
                _logger.Error("加载收藏失败", ex);
                UpdateStatus("加载收藏失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 通用分页加载。负责取数、渲染、更新分页栏。
        /// </summary>
        private async Task LoadPageAsync(
            string title,
            int page,
            Func<int, int, CancellationToken, Task<IReadOnlyList<Song>>> fetch,
            Func<CancellationToken, Task<int>> countFetcher)
        {
            if (!await IsDatabaseReadyAsync())
            {
                MessageBox.Show("曲库未就绪，无法浏览。", "KtvDownloader",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            CancelBackgroundLoad();
            SetSearchBusy(true);
            UpdateStatus("正在加载" + title + "...");

            try
            {
                var pageSize = paginationBar.PageSize;
                var offset = (page - 1) * pageSize;

                var songs = await fetch(offset, pageSize, CancellationToken.None);

                if (IsDisposed) return;

                _searchResults.Clear();
                _searchResults.AddRange(songs);
                AppendSearchResults(songs, true);

                // 总条数：能快速统计的才统计，否则按已知页数估算。
                int totalCount;
                if (countFetcher != null)
                {
                    totalCount = await countFetcher(CancellationToken.None);
                }
                else
                {
                    // 无法精确计数时，用「已加载 + 是否满页」估算。
                    totalCount = songs.Count < pageSize
                        ? offset + songs.Count
                        : (page + 1) * pageSize;
                }

                paginationBar.SetPagination(page, pageSize, totalCount);

                lblResultSummary.Text = string.Format("{0}　第 {1} 页，共 {2} 条",
                    title, page, totalCount);
                UpdateStatus(string.Format("已加载 {0} 条", songs.Count));
            }
            catch (Exception ex)
            {
                _logger.Error("加载失败: " + title, ex);
                UpdateStatus("加载失败：" + ex.Message);
            }
            finally
            {
                SetSearchBusy(false);
            }
        }

        /// <summary>
        /// 执行搜索。先按设定条数加载第一页，随后后台继续把全部结果拉回来缓存，
        /// 这样用户翻页/看后续时无需等待。
        /// </summary>
        private async Task PerformSearchAsync()
        {
            var keyword = txtKeyword.Text.Trim();
            if (keyword.Length == 0)
            {
                MessageBox.Show("请输入搜索关键词。", "KtvDownloader",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtKeyword.Focus();
                return;
            }

            if (!await IsDatabaseReadyAsync())
            {
                MessageBox.Show("曲库未就绪，无法搜索。", "KtvDownloader",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _lastKeyword = keyword;
            _lastSearchBySinger = cboSearchType.SelectedIndex == 1;
            _lastLanguage = cboLanguage.SelectedItem as string ?? "全部";

            // 解析「歌名 歌手」格式。例如「发如雪 周杰伦」。
            // 用曲库校验歌手名，避免把普通带空格的歌名误判为组合搜索。
            if (!_lastSearchBySinger)
            {
                var searchService = _searchService;
                var singerCheck = new Func<string, bool>(name =>
                {
                    try
                    {
                        // 这里已经是后台线程，同步等待是安全的（见下面的 Task.Run）。
                        return searchService.SingerExistsAsync(name)
                            .GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn("校验歌手名失败: " + ex.Message);
                        return false;
                    }
                });

                // ⚠️ Resolve 内部会对每个候选切法调一次 singerCheck，
                // 而每次 singerCheck 都是一趟 SQLite 查询。三段以上的输入要查好几轮，
                // 直接在 UI 线程上跑就是肉眼可见的卡顿（按下回车后要等一下才出结果）。
                _lastQuery = await Task.Run(() => SearchQuery.Resolve(keyword, singerCheck));
                _lastIsCombined = _lastQuery != null && _lastQuery.IsCombined;

                if (_lastIsCombined)
                {
                    _logger.Info(string.Format("识别为组合搜索：歌名={0} 歌手={1}",
                        _lastQuery.SongName, _lastQuery.SingerName));
                }
            }
            else
            {
                _lastQuery = null;
                _lastIsCombined = false;
            }

            CancelBackgroundLoad();
            var cts = new CancellationTokenSource();
            _searchCts = cts;

            SetSearchBusy(true);
            UpdateStatus("正在搜索...");

            try
            {
                var pageSize = paginationBar.PageSize;

                // ── 第一步：前缀秒出（走索引，通常 1-10ms）──
                IReadOnlyList<Song> firstPage;
                if (_lastIsCombined)
                {
                    // 组合搜索：歌名 + 歌手同时匹配。
                    firstPage = await _searchService.SearchCombinedAsync(
                        _lastQuery.SongName, _lastQuery.SingerName,
                        0, pageSize, fastOnly: true, cancellationToken: cts.Token);

                    if (cts.Token.IsCancellationRequested || IsDisposed) return;

                    // 前缀没命中（歌名在中间），退回完整搜索。
                    if (firstPage.Count == 0)
                    {
                        UpdateStatus("正在完整搜索...");
                        firstPage = await _searchService.SearchCombinedAsync(
                            _lastQuery.SongName, _lastQuery.SingerName,
                            0, pageSize, fastOnly: false, cancellationToken: cts.Token);
                    }
                }
                else if (_lastSearchBySinger)
                {
                    // 歌手搜索没有前缀快路径，直接查。
                    firstPage = await _searchService.SearchBySingerAsync(
                        _lastKeyword, 0, pageSize, cts.Token);
                }
                else
                {
                    firstPage = await _searchService.SearchSongsFastAsync(
                        _lastKeyword, _lastLanguage, 0, pageSize, cts.Token);

                    if (cts.Token.IsCancellationRequested || IsDisposed) return;

                    // 前缀结果太少时，换成完整搜索（稍慢但结果全）。
                    if (firstPage.Count < 5)
                    {
                        UpdateStatus("前缀结果较少，正在扩大搜索...");
                        firstPage = await _searchService.SearchSongsAsync(
                            _lastKeyword, _lastLanguage, 0, pageSize, cts.Token);
                    }
                }

                if (cts.Token.IsCancellationRequested || IsDisposed) return;

                _searchResults.Clear();
                _searchResults.AddRange(firstPage);
                AppendSearchResults(firstPage, true);

                // 先给出即时反馈，用户马上能看到结果。
                paginationBar.SetPagination(1, pageSize,
                    firstPage.Count < pageSize ? firstPage.Count : pageSize * 10);
                lblResultSummary.Text = _lastIsCombined
                    ? BuildResultSummary(
                        string.Format("「{0}」+「{1}」", _lastQuery.SongName, _lastQuery.SingerName),
                        "正在统计...", _lastQuery.SingerName, firstPage)
                    : BuildResultSummary(
                        string.Format("搜索「{0}」", _lastKeyword), "正在统计...",
                        _lastSearchBySinger ? _lastKeyword : null, firstPage);
                UpdateStatus(string.Format("已显示 {0} 条，后台统计中", firstPage.Count));

                SetSearchBusy(false);

                // ── 第二步：后台统计总数 + 预加载全部 ──
                StartBackgroundCountAndPrefetch(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 被新搜索取消，正常。
            }
            catch (Exception ex)
            {
                _logger.Error("搜索失败", ex);
                UpdateStatus("搜索失败：" + ex.Message);
                MessageBox.Show("搜索失败：" + ex.Message, "KtvDownloader",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed) SetSearchBusy(false);
            }
        }

        /// <summary>
        /// 生成搜索结果汇总文案。
        /// <para>
        /// 第三个参数非空时会按**已加载的结果**判断要不要补一句说明：
        /// 如果有些歌是靠歌名匹配进来的、歌手字段里却没有这个词，
        /// 说明命中的是「发如雪（Cover：周杰伦）」这类翻唱版 ——
        /// 曲库把歌手标成了「佚名」，真名只写在歌名里。
        /// 不解释的话，用户看着满屏「佚名」会以为搜错了。
        /// </para>
        /// </summary>
        /// <param name="title">标题部分，例如「搜索「周杰伦」」。</param>
        /// <param name="totalText">条数文案，例如「共 372 条」。</param>
        /// <param name="singerKeyword">按歌手搜索时的关键词；按歌名搜索传 null（不补说明）。</param>
        /// <param name="loaded">已加载到界面上的结果。</param>
        private static string BuildResultSummary(
            string title, string totalText, string singerKeyword, IReadOnlyList<Song> loaded)
        {
            var text = title + "　" + totalText;
            return HasNameOnlyHits(singerKeyword, loaded)
                ? text + "　（含歌名里的「Cover：」版本，歌手列可能显示「佚名」）"
                : text;
        }

        /// <summary>
        /// 是否存在「歌名里有这个词、但歌手字段里没有」的结果。
        /// </summary>
        private static bool HasNameOnlyHits(string keyword, IReadOnlyList<Song> songs)
        {
            if (string.IsNullOrEmpty(keyword) || songs == null) return false;

            foreach (var song in songs)
            {
                if (song == null) continue;

                var inName = (song.Name ?? string.Empty)
                    .IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!inName) continue;

                var inSinger = (song.Singer ?? string.Empty)
                    .IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!inSinger) return true;
            }

            return false;
        }

        /// <summary>
        /// 后台统计总数并预加载全部结果。
        /// 界面已先显示前缀结果，这一步补齐剩余数据，用户几乎无感。
        /// </summary>
        private void StartBackgroundCountAndPrefetch(CancellationToken token)
        {
            var keyword = _lastKeyword;
            var language = _lastLanguage;
            var bySinger = _lastSearchBySinger;

            // ⚠️ 组合搜索的歌名/歌手也必须在这里快照下来。
            // 后台任务跑几秒到几十秒，期间用户完全可以再搜一次；
            // 直接读 _lastQuery 字段就会拿着**新**的查询条件去补**旧**的结果，
            // 最后把两份不相干的结果混进同一个缓存。
            var isCombined = _lastIsCombined && _lastQuery != null;
            var songName = isCombined ? _lastQuery.SongName : string.Empty;
            var singerName = isCombined ? _lastQuery.SingerName : string.Empty;

            Task.Run(async () =>
            {
                try
                {
                    // 统计总数（组合搜索用专门的计数方法）
                    int total;
                    if (isCombined)
                    {
                        total = await _searchService.CountCombinedAsync(songName, singerName, token);
                    }
                    else
                    {
                        total = await _searchService.CountAsync(
                            keyword, language, bySinger, token);
                    }

                    if (token.IsCancellationRequested || IsDisposed) return;

                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed) return;
                        paginationBar.SetPagination(1, paginationBar.PageSize, total);
                        // bySinger / isCombined / singerName 都是进后台前快照的局部变量，
                        // 不能改读字段：后台可能跑几十秒，期间用户已经搜了别的。
                        lblResultSummary.Text = BuildResultSummary(
                            string.Format("搜索「{0}」", keyword),
                            string.Format("共 {0} 条", total),
                            (isCombined || bySinger)
                                ? (isCombined ? singerName : keyword)
                                : null,
                            _searchResults);
                    }));

                    // 预加载全部
                    var progress = new Progress<int>(count =>
                    {
                        if (IsDisposed || !IsHandleCreated) return;
                        try
                        {
                            BeginInvoke(new Action(() =>
                            {
                                if (IsDisposed) return;
                                lblPrefetchStatus.Text = string.Format("后台加载 {0}/{1}", count, total);
                                lblPrefetchStatus.Visible = true;
                            }));
                        }
                        catch (InvalidOperationException) { }
                    });

                    IReadOnlyList<Song> all;
                    if (isCombined)
                    {
                        // 组合搜索：分页循环拉取。
                        var buffer = new List<Song>();
                        const int batch = 500;
                        while (buffer.Count < 5000)
                        {
                            if (token.IsCancellationRequested) return;

                            var page = await _searchService.SearchCombinedAsync(
                                songName, singerName,
                                buffer.Count, batch, fastOnly: false, cancellationToken: token);

                            if (page.Count == 0) break;
                            buffer.AddRange(page);
                            if (progress != null) ((IProgress<int>)progress).Report(buffer.Count);
                            if (page.Count < batch) break;
                        }
                        all = buffer;
                    }
                    else
                    {
                        all = await _searchService.GetAllMatchesAsync(
                            keyword, language, bySinger, 5000, progress, token);
                    }

                    if (token.IsCancellationRequested || IsDisposed) return;

                    _prefetchCache = new List<Song>(all);

                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed) return;
                        // token 已经作废（用户又搜了一次）就别再动界面，
                        // 否则新搜索刚显示的第一页会被旧缓存覆盖掉。
                        if (token.IsCancellationRequested) return;

                        lblPrefetchStatus.Text = string.Format("已缓存 {0} 条", all.Count);
                        lblPrefetchStatus.Visible = true;

                        // 刷新当前页：如果缓存里第一页比现在显示的多，更新显示。
                        if (all.Count > _searchResults.Count)
                        {
                            ShowPageFromCache(1);
                        }
                    }));
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.Warn("后台统计/预加载失败: " + ex.Message);
                }
            }, token);
        }

        /// <summary>
        /// 取消后台预加载并丢弃缓存。
        /// <para>
        /// ⚠️ 早期版本取消的是一个叫 <c>_prefetchCts</c> 的字段，而它**从来没有被赋过值**
        /// （真正在用的是 <see cref="_searchCts"/>），所以这个"取消"一直是空操作：
        /// 用户连着搜两次，第一次的后台预取还在往 <c>_prefetchCache</c> / <c>_lastQuery</c> 里写，
        /// 翻页时就会看到上一次搜索的结果混进来。
        /// </para>
        /// </summary>
        private void CancelBackgroundLoad()
        {
            var cts = _searchCts;
            _searchCts = null;

            if (cts != null)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                catch (Exception ex) { _logger.Warn("取消后台加载失败: " + ex.Message); }

                // Cancel 之后后台任务可能还在收尾，Dispose 交给它结束后的竞态处理：
                // 这里立刻 Dispose 会让仍在 await 的分支抛 ObjectDisposedException。
                // 用一个延迟释放，既不漏句柄也不打断收尾。
                DisposeLater(cts);
            }

            _prefetchCache = null;

            if (!IsDisposed) lblPrefetchStatus.Visible = false;
        }

        /// <summary>
        /// 延迟释放一个 CancellationTokenSource。
        /// <para>
        /// <c>Cancel()</c> 之后，注册在 token 上的回调与正在 await 的分支还需要一点时间收尾，
        /// 立刻 <c>Dispose()</c> 会让它们撞上 <see cref="ObjectDisposedException"/>。
        /// 丢到线程池上等一会儿再释放，既不泄漏句柄也不打断收尾。
        /// </para>
        /// </summary>
        private void DisposeLater(CancellationTokenSource cts)
        {
            Task.Run(async () =>
            {
                try { await Task.Delay(2000).ConfigureAwait(false); } catch { }
                try { cts.Dispose(); } catch { }
            });
        }

        /// <summary>翻页。</summary>
        private async void OnPageChanged(object sender, int page)
        {
            if (_browseMode == BrowseMode.Ranking)
            {
                await LoadRankingAsync(page);
                return;
            }

            if (_browseMode == BrowseMode.NewSongs)
            {
                await LoadNewSongsAsync(page);
                return;
            }

            if (_browseMode == BrowseMode.Favorites)
            {
                // 收藏不分页，忽略。
                return;
            }

            // 搜索结果翻页：优先用预加载缓存。
            if (_prefetchCache != null && _prefetchCache.Count > 0)
            {
                ShowPageFromCache(page);
                return;
            }

            await LoadSearchPageAsync(page);
        }

        /// <summary>从预加载缓存里取某页显示，无需查库。</summary>
        private void ShowPageFromCache(int page)
        {
            try
            {
                var pageSize = paginationBar.PageSize;
                var offset = (page - 1) * pageSize;

                var pageSongs = _prefetchCache
                    .Skip(offset)
                    .Take(pageSize)
                    .ToList();

                _searchResults.Clear();
                _searchResults.AddRange(pageSongs);
                AppendSearchResults(pageSongs, true);

                paginationBar.SetPagination(page, pageSize, _prefetchCache.Count);
                lblResultSummary.Text = BuildResultSummary(
                    string.Format("搜索「{0}」", _lastKeyword),
                    string.Format("第 {0} 页，共 {1} 条", page, _prefetchCache.Count),
                    (_lastSearchBySinger || _lastIsCombined)
                        ? (_lastIsCombined && _lastQuery != null ? _lastQuery.SingerName : _lastKeyword)
                        : null,
                    pageSongs);
                UpdateStatus(string.Format("第 {0} 页（来自缓存）", page));
            }
            catch (Exception ex)
            {
                _logger.Error("从缓存翻页失败", ex);
            }
        }

        /// <summary>缓存未就绪时直接查库取某页。</summary>
        private async Task LoadSearchPageAsync(int page)
        {
            SetSearchBusy(true);
            UpdateStatus("正在加载第 " + page + " 页...");

            try
            {
                var pageSize = paginationBar.PageSize;
                var offset = (page - 1) * pageSize;

                IReadOnlyList<Song> songs;
                if (_lastIsCombined && _lastQuery != null)
                {
                    songs = await _searchService.SearchCombinedAsync(
                        _lastQuery.SongName, _lastQuery.SingerName,
                        offset, pageSize, fastOnly: false, cancellationToken: CancellationToken.None);
                }
                else if (_lastSearchBySinger)
                {
                    songs = await _searchService.SearchBySingerAsync(
                        _lastKeyword, offset, pageSize, CancellationToken.None);
                }
                else
                {
                    songs = await _searchService.SearchSongsAsync(
                        _lastKeyword, _lastLanguage, offset, pageSize, CancellationToken.None);
                }

                if (IsDisposed) return;

                _searchResults.Clear();
                _searchResults.AddRange(songs);
                AppendSearchResults(songs, true);

                int totalCount;
                if (_lastIsCombined && _lastQuery != null)
                {
                    totalCount = await _searchService.CountCombinedAsync(
                        _lastQuery.SongName, _lastQuery.SingerName, CancellationToken.None);
                }
                else
                {
                    totalCount = await _searchService.CountAsync(
                        _lastKeyword, _lastLanguage, _lastSearchBySinger, CancellationToken.None);
                }

                paginationBar.SetPagination(page, pageSize, totalCount);
                lblResultSummary.Text = string.Format("搜索「{0}」　第 {1} 页，共 {2} 条",
                    _lastKeyword, page, totalCount);
                UpdateStatus(string.Format("第 {0} 页，共 {1} 条", page, totalCount));
            }
            catch (Exception ex)
            {
                _logger.Error("翻页失败", ex);
                UpdateStatus("翻页失败：" + ex.Message);
            }
            finally
            {
                SetSearchBusy(false);
            }
        }

        /// <summary>每页条数变化：回到第一页重新加载。</summary>
        private async void OnPageSizeChanged(object sender, int pageSize)
        {
            _config.PageSize = pageSize;
            _configStore.Save(_config);

            if (_browseMode == BrowseMode.Ranking)
            {
                await LoadRankingAsync(1);
                return;
            }

            if (_browseMode == BrowseMode.NewSongs)
            {
                await LoadNewSongsAsync(1);
                return;
            }

            if (_lastKeyword.Length > 0 && _prefetchCache != null)
            {
                // 缓存里已全部数据，直接重新分页。
                ShowPageFromCache(1);
                paginationBar.SetPagination(1, pageSize, _prefetchCache.Count);
                return;
            }

            if (_lastKeyword.Length > 0)
            {
                await LoadSearchPageAsync(1);
            }
        }

        private void OnSearchClick(object sender, EventArgs e)
        {
            // fire-and-forget：PerformSearchAsync 内部已完整处理异常，
            // 这里刻意不 await，避免阻塞 UI 线程。
#pragma warning disable CS4014
            PerformSearchAsync();
#pragma warning restore CS4014
        }

        /// <summary>搜索结果选中项变化时，同步预览与下载按钮的可用状态。</summary>
        private void OnResultSelectionChanged(object sender, EventArgs e)
        {
            var hasSelection = lvResults.SelectedItems.Count > 0;
            btnDownloadSelected.Enabled = hasSelection;
            btnPreview.Enabled = hasSelection;
        }

        private void OnKeywordKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;

            e.SuppressKeyPress = true;
#pragma warning disable CS4014
            PerformSearchAsync();
#pragma warning restore CS4014
        }

        private void AppendSearchResults(IReadOnlyList<Song> songs, bool reset)
        {
            if (reset) lvResults.Items.Clear();

            var startIndex = _searchResults.Count;
            if (reset) startIndex = 0;

            lvResults.BeginUpdate();
            try
            {
                for (var i = 0; i < songs.Count; i++)
                {
                    var song = songs[i];
                    var item = new ListViewItem((startIndex + i + 1).ToString());
                    item.SubItems.Add(song.Name);
                    item.SubItems.Add(song.Singer);
                    item.SubItems.Add(song.Language);
                    item.SubItems.Add(song.MusicNo);
                    item.SubItems.Add(song.HotScore.ToString());
                    item.Tag = song;
                    lvResults.Items.Add(item);
                }
            }
            finally
            {
                lvResults.EndUpdate();
            }
        }

        /// <summary>
        /// 统一管理搜索期间各按钮的可用状态。
        /// </summary>
        private void SetSearchBusy(bool busy)
        {
            btnSearch.Enabled = !busy;

            if (busy)
            {
                txtKeyword.Enabled = false;
                cboSearchType.Enabled = false;
                cboLanguage.Enabled = false;
                btnDownloadSelected.Enabled = false;
                btnPreview.Enabled = false;
                paginationBar.Enabled = false;
                Cursor = Cursors.WaitCursor;
            }
            else
            {
                Cursor = Cursors.Default;

                var isSearchMode = _browseMode == BrowseMode.BySongName || _browseMode == BrowseMode.BySinger;
                txtKeyword.Enabled = isSearchMode;
                cboSearchType.Enabled = isSearchMode;
                cboLanguage.Enabled = isSearchMode || _browseMode == BrowseMode.ByCategory;

                var hasSelection = lvResults.SelectedItems.Count > 0;
                btnDownloadSelected.Enabled = hasSelection;
                btnPreview.Enabled = hasSelection;
                paginationBar.Enabled = true;
            }
        }

        #endregion

        #region 下载

        private void OnResultDoubleClick(object sender, EventArgs e)
        {
            // 双击直接预览，这是用户最常做的事。
            OnPreviewClick(sender, EventArgs.Empty);
        }

        private void OnResultKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                OnPreviewClick(sender, EventArgs.Empty);
            }
            else if (e.Control && e.KeyCode == Keys.D)
            {
                // Ctrl+D 下载，避免和回车预览冲突。
                e.SuppressKeyPress = true;
                OnDownloadSelectedClick(sender, EventArgs.Empty);
            }
        }

        /// <summary>右键菜单：预览 / 下载 / 收藏 / 复制歌号。</summary>
        private void OnResultMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            if (lvResults.SelectedItems.Count == 0) return;

            // 菜单项回调里**不要**再读 lvResults.SelectedItems：
            // 菜单是异步弹出的，用户点之前选中项可能已经变了。这里先把歌取出来。
            var song = lvResults.SelectedItems[0].Tag as Song;
            if (song == null) return;

            ShowContextMenu(lvResults, e.Location, menu =>
            {
                menu.Items.Add("预览", null, (s, args) => OnPreviewClick(this, EventArgs.Empty));
                menu.Items.Add("下载", null, (s, args) => OnDownloadSelectedClick(this, EventArgs.Empty));
                menu.Items.Add(new ToolStripSeparator());

                // 收藏：FavoriteStore 早就写好了，但一直没有入口，
                // 「收藏」浏览模式永远是空的，状态栏还写着"可在搜索结果右键添加收藏"。
                var isFavorite = IsFavorite(song);
                var favoriteItem = new ToolStripMenuItem(isFavorite ? "取消收藏" : "添加收藏");
                favoriteItem.Click += (s, args) => ToggleFavorite(song);
                menu.Items.Add(favoriteItem);

                var copyItem = new ToolStripMenuItem("复制歌号");
                copyItem.Enabled = !string.IsNullOrEmpty(song.MusicNo);
                copyItem.Click += (s, args) =>
                {
                    try { Clipboard.SetText(song.MusicNo); }
                    catch (Exception ex)
                    {
                        // 剪贴板可能被别的进程占着，失败就只在状态栏提一句。
                        _logger.Warn("复制歌号失败: " + ex.Message);
                        UpdateStatus("复制歌号失败：" + ex.Message);
                    }
                };
                menu.Items.Add(copyItem);
            });
        }

        /// <summary>
        /// 弹出一个用完即释放的右键菜单。
        /// <para>
        /// ⚠️ 早期版本每次右键都 <c>new ContextMenuStrip()</c> 且从不 Dispose。
        /// ContextMenuStrip 内部持有原生窗口句柄，右键点几百次就是几百个泄漏句柄
        /// （任务管理器里"句柄数"一路涨，最后 GDI 对象耗尽、界面开始花屏）。
        /// </para>
        /// </summary>
        private void ShowContextMenu(Control owner, Point location, Action<ContextMenuStrip> build)
        {
            var menu = new ContextMenuStrip();

            try
            {
                build(menu);
            }
            catch (Exception ex)
            {
                menu.Dispose();
                _logger.Warn("构建右键菜单失败: " + ex.Message);
                return;
            }

            if (menu.Items.Count == 0)
            {
                menu.Dispose();
                return;
            }

            menu.Closed += (s, args) =>
            {
                var target = menu;

                // 用 BeginInvoke 让释放发生在 Closed 事件链完全结束之后，
                // 否则 WinForms 还在用这个对象时就把它 Dispose 了。
                if (IsDisposed || !IsHandleCreated)
                {
                    try { target.Dispose(); } catch { }
                    return;
                }

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        try { target.Dispose(); } catch { }
                    }));
                }
                catch (InvalidOperationException)
                {
                    try { target.Dispose(); } catch { }
                }
            };

            menu.Show(owner, location);
        }

        private bool IsFavorite(Song song)
        {
            if (song == null || string.IsNullOrEmpty(song.MusicNo)) return false;
            try { return _favoriteStore != null && _favoriteStore.Contains(song.MusicNo); }
            catch (Exception ex)
            {
                _logger.Warn("读取收藏状态失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>添加 / 取消收藏。收藏以歌号为准，曲库更新后依然有效。</summary>
        private void ToggleFavorite(Song song)
        {
            if (song == null || string.IsNullOrEmpty(song.MusicNo))
            {
                UpdateStatus("这首歌没有歌号，无法收藏");
                return;
            }

            try
            {
                if (_favoriteStore.Contains(song.MusicNo))
                {
                    _favoriteStore.Remove(song.MusicNo);
                    UpdateStatus("已取消收藏：" + song.DisplayName);
                }
                else
                {
                    _favoriteStore.Add(song.MusicNo);
                    UpdateStatus(string.Format("已收藏：{0}（歌号 {1}）", song.DisplayName, song.MusicNo));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("收藏操作失败", ex);
                UpdateStatus("收藏操作失败：" + ex.Message);
                return;
            }

            // 正停在「收藏」列表上时，改动要立刻反映出来。
            if (_browseMode == BrowseMode.Favorites)
            {
#pragma warning disable CS4014
                LoadFavoritesAsync();
#pragma warning restore CS4014
            }
        }

        /// <summary>
        /// 预览选中的歌曲。
        /// <para>
        /// 源站下发的是加密 TS，浏览器无法直接播放，
        /// 所以交给 <see cref="PreviewService"/> 走「下载片段→解密→转MP4」流程。
        /// 如果本地已有完整下载文件，直接播本地，省去准备时间。
        /// </para>
        /// </summary>
        private void OnPreviewClick(object sender, EventArgs e)
        {
            if (lvResults.SelectedItems.Count == 0 && lvQueue.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先在搜索结果中选择要预览的歌曲。", "KtvDownloader",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Song song = null;

            if (lvResults.SelectedItems.Count > 0)
            {
                song = lvResults.SelectedItems[0].Tag as Song;
            }
            else if (lvQueue.SelectedItems.Count > 0)
            {
                var task = lvQueue.SelectedItems[0].Tag as DownloadTask;
                if (task != null) song = task.Song;
            }

            if (song == null) return;

            // 本地已有完整文件则直接播，跳过准备流程。
            var localPath = FindLocalFile(song);

            var previewForm = new PreviewForm(
                _logger, song, _previewService, _config.PreviewDurationSeconds);
            previewForm.Show(this);

            if (localPath != null)
            {
                previewForm.PlayLocalFile(localPath);
                UpdateStatus("预览本地文件");
            }
            else
            {
                UpdateStatus(_config.PreviewDurationSeconds == 0
                    ? "正在准备完整预览（需下载整首）"
                    : string.Format("正在准备预览（前 {0} 秒）", _config.PreviewDurationSeconds));
            }
        }

        /// <summary>
        /// 在下载目录里找这首歌已下载好的文件。
        /// <para>
        /// 命名规则的**唯一**权威是 <see cref="SongFileNaming"/>：
        /// 下载落盘用它、预览探测也用它。早期这里自己又拼了一遍
        /// <c>歌名-歌手</c>（没有歌号、没有 .mp3、后缀顺序也不同），
        /// 于是明明下过的歌预览时找不到本地文件，白白再下一次。
        /// </para>
        /// </summary>
        private string FindLocalFile(Song song)
        {
            try
            {
                var directory = _config.OutputDirectory;
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return null;
                return SongFileNaming.FindExistingFile(directory, song);
            }
            catch (Exception ex)
            {
                // 探测失败按没有本地文件处理，但要说清楚原因。
                _logger.Warn("查找本地文件失败: " + ex.Message);
                return null;
            }
        }

        private void OnDownloadSelectedClick(object sender, EventArgs e)
        {
            if (lvResults.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先在搜索结果中选择要下载的歌曲。", "KtvDownloader",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var outputDirectory = _config.OutputDirectory;
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                MessageBox.Show("请先在设置中配置下载目录。", "KtvDownloader",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法创建下载目录：" + ex.Message, "KtvDownloader",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var options = BuildDownloadOptions();
            var count = 0;

            foreach (ListViewItem item in lvResults.SelectedItems)
            {
                var song = item.Tag as Song;
                if (song == null) continue;

                EnqueueSong(song, outputDirectory, options, item);
                count++;
            }

            UpdateStatus(string.Format("已加入队列 {0} 首，每首独立下载", count));
        }

        private void EnqueueSong(Song song, string outputDirectory, DownloadOptions options)
        {
            EnqueueSong(song, outputDirectory, options, null);
        }

        private void EnqueueSong(Song song, string outputDirectory, DownloadOptions options, ListViewItem source)
        {
            var task = new DownloadTask(song, outputDirectory);
            task.State = DownloadState.Pending;
            task.StatusText = "排队中";

            // 每首歌一行，各自独立更新进度。
            var item = new ListViewItem(song.DisplayName);
            item.SubItems.Add("排队中");
            item.SubItems.Add("0%");
            item.Tag = task;

            lvQueue.Items.Add(item);
            _queueItems[task.Id] = item;

            _downloadQueue.Enqueue(task, options);
        }

        private DownloadOptions BuildDownloadOptions()
        {
            var resolution = cboResolution.SelectedItem as string ?? "720";

            VocalOption vocal;
            switch (cboVocal.SelectedIndex)
            {
                case 1: vocal = VocalOption.OriginalOnly; break;
                case 2: vocal = VocalOption.AccompanimentOnly; break;
                case 3: vocal = VocalOption.SplitBoth; break;
                default: vocal = VocalOption.KeepBoth; break;
            }

            return new DownloadOptions
            {
                Resolution = resolution,
                Vocal = vocal,
                ConvertToMp4 = chkConvertMp4.Checked,
                AudioBitrateKbps = 192,
                DeleteIntermediateFiles = true
            };
        }

        /// <summary>
        /// 下载任务状态回调。可能来自后台线程，必须封送到 UI 线程。
        /// </summary>
        private void OnDownloadTaskUpdated(object sender, DownloadTaskEventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                BeginInvoke(new Action(() => ApplyTaskUpdate(e)));
            }
            catch (InvalidOperationException)
            {
                // 窗体正在关闭，忽略。
            }
        }

        private void ApplyTaskUpdate(DownloadTaskEventArgs e)
        {
            ListViewItem item;
            if (!_queueItems.TryGetValue(e.Task.Id, out item)) return;
            if (item.ListView == null) return;

            var progress = e.Progress;
            item.SubItems[1].Text = progress != null ? progress.StatusText : e.Task.StatusText;

            // 进度列：下载阶段显示百分比，其它阶段显示阶段名。
            if (progress != null && progress.State == DownloadState.Downloading &&
                progress.TotalBytes.HasValue && progress.TotalBytes.Value > 0)
            {
                item.SubItems[2].Text = progress.Percent.ToString("F0") + "%";
            }
            else if (progress != null && progress.State == DownloadState.Decrypting)
            {
                item.SubItems[2].Text = "解密";
            }
            else if (e.Task.State == DownloadState.Completed)
            {
                item.SubItems[1].Text = "已完成";
                item.SubItems[2].Text = "100%";
            }
            else if (e.Task.State == DownloadState.Failed)
            {
                item.SubItems[1].Text = "失败";
                item.SubItems[2].Text = "-";
            }
            else if (e.Task.State == DownloadState.Canceled)
            {
                item.SubItems[1].Text = "已取消";
                item.SubItems[2].Text = "-";
            }

            // 状态栏汇总：显示当前活跃任务数。
            var active = _downloadQueue != null ? _downloadQueue.ActiveTaskCount : 0;
            UpdateStatus(active > 0 ? string.Format("下载中，队列 {0} 项", active) : "就绪");
        }

        /// <summary>双击队列项：已完成则打开本地文件预览，否则忽略。</summary>
        private void OnQueueDoubleClick(object sender, MouseEventArgs e)
        {
            if (lvQueue.SelectedItems.Count == 0) return;

            var task = lvQueue.SelectedItems[0].Tag as DownloadTask;
            if (task == null) return;

            if (task.State == DownloadState.Completed && !string.IsNullOrEmpty(task.OutputPath)
                && File.Exists(task.OutputPath))
            {
                var form = new PreviewForm(_logger, task.Song, _previewService);
                form.PlayLocalFile(task.OutputPath);
                form.Show(this);
            }
            else if (task.State == DownloadState.Completed)
            {
                MessageBox.Show("找不到下载的文件，可能已被移动或删除。", "KtvDownloader",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>队列右键菜单。</summary>
        private void OnQueueMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            if (lvQueue.SelectedItems.Count == 0) return;

            var task = lvQueue.SelectedItems[0].Tag as DownloadTask;
            if (task == null) return;

            ShowContextMenu(lvQueue, e.Location, menu => BuildQueueMenu(menu, task));
        }

        private void BuildQueueMenu(ContextMenuStrip menu, DownloadTask task)
        {
            if (task.State == DownloadState.Completed)
            {
                menu.Items.Add("预览", null, (s, args) =>
                {
                    if (!string.IsNullOrEmpty(task.OutputPath) && File.Exists(task.OutputPath))
                    {
                        var form = new PreviewForm(_logger, task.Song, _previewService);
                        form.PlayLocalFile(task.OutputPath);
                        form.Show(this);
                    }
                });

                menu.Items.Add("打开所在目录", null, (s, args) =>
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(task.OutputPath) && File.Exists(task.OutputPath))
                            Process.Start("explorer.exe", "/select,\"" + task.OutputPath + "\"");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn("打开目录失败: " + ex.Message);
                    }
                });

                menu.Items.Add(new ToolStripSeparator());
            }

            var cancelItem = new ToolStripMenuItem("取消此任务");
            cancelItem.Enabled = task.State == DownloadState.Pending ||
                                 task.State == DownloadState.Resolving ||
                                 task.State == DownloadState.Downloading;
            cancelItem.Click += (s, args) =>
            {
                if (_downloadQueue != null) _downloadQueue.Cancel(task.Id);
            };
            menu.Items.Add(cancelItem);

            // 完成的歌也可以收藏，省得回搜索结果里再找一遍。
            if (task.Song != null && !string.IsNullOrEmpty(task.Song.MusicNo))
            {
                menu.Items.Add(new ToolStripSeparator());
                var isFavorite = IsFavorite(task.Song);
                var song = task.Song;
                var favoriteItem = new ToolStripMenuItem(isFavorite ? "取消收藏" : "添加收藏");
                favoriteItem.Click += (s, args) => ToggleFavorite(song);
                menu.Items.Add(favoriteItem);
            }
        }

        private void OnCancelAllClick(object sender, EventArgs e)
        {
            if (_downloadQueue == null) return;

            if (_downloadQueue.ActiveTaskCount == 0)
            {
                UpdateStatus("队列为空");
                return;
            }

            var result = MessageBox.Show("确定要取消所有下载任务吗？", "KtvDownloader",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            _downloadQueue.CancelAll();
            UpdateStatus("已请求取消全部任务");
        }

        private void OnClearFinishedClick(object sender, EventArgs e)
        {
            var toRemove = new List<ListViewItem>();

            foreach (ListViewItem item in lvQueue.Items)
            {
                var task = item.Tag as DownloadTask;
                if (task == null) continue;

                if (task.State == DownloadState.Completed ||
                    task.State == DownloadState.Failed ||
                    task.State == DownloadState.Canceled)
                {
                    toRemove.Add(item);
                }
            }

            foreach (var item in toRemove)
            {
                var task = item.Tag as DownloadTask;
                if (task != null) _queueItems.Remove(task.Id);
                lvQueue.Items.Remove(item);
            }

            UpdateStatus("已清除 " + toRemove.Count + " 条记录");
        }

        #endregion

        #region 曲库更新

        private async void OnCheckUpdateClick(object sender, EventArgs e)
        {
            if (!await EnsureDatabaseReadyAsync()) return;

            try
            {
                UpdateStatus("正在检查曲库更新...");
                menuItemCheckUpdate.Enabled = false;

                var info = await _databaseUpdateService.CheckUpdateAsync(_config.DatabasePath);

                if (info.HasUpdate)
                {
                    var result = MessageBox.Show(
                        string.Format("发现新的曲库版本。{0}{0}本地版本：{1}{0}最新版本：{2}{0}{0}是否立即更新？（约 418MB）",
                            Environment.NewLine,
                            info.LocalVersion ?? "未知",
                            info.Version ?? "未知"),
                        "曲库更新", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                    if (result == DialogResult.Yes) await UpdateDatabaseAsync();
                    else UpdateStatus("已取消更新");
                }
                else
                {
                    MessageBox.Show("曲库已是最新版本。" + Environment.NewLine +
                                    "版本：" + (info.Version ?? "未知"),
                        "曲库更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    UpdateStatus("曲库已是最新");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("检查更新失败", ex);
                UpdateStatus("检查更新失败");
                MessageBox.Show("检查更新失败：" + ex.Message, "KtvDownloader",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                menuItemCheckUpdate.Enabled = true;
            }
        }

        private async void OnUpdateDatabaseClick(object sender, EventArgs e)
        {
            await UpdateDatabaseAsync();
        }

        /// <summary>
        /// 下载并安装最新曲库。带取消与进度显示。
        /// </summary>
        private async Task UpdateDatabaseAsync()
        {
            var targetPath = DatabasePathResolver.ResolveUpdateTarget(_config.DatabasePath);

            if (_updateCts != null)
            {
                _updateCts.Cancel();
                _updateCts.Dispose();
            }
            _updateCts = new CancellationTokenSource();
            var token = _updateCts.Token;

            var progressForm = new DatabaseUpdateForm();
            var progress = new Progress<DatabaseUpdateProgress>(p =>
            {
                progressForm.UpdateProgress(p);
                UpdateStatus(p.ToDisplayText());
            });

            UpdateStatus("开始更新曲库");
            menuItemUpdateDatabase.Enabled = false;
            menuItemCheckUpdate.Enabled = false;

            // 非模态显示进度，用户仍可取消。
            progressForm.Show(this);

            try
            {
                var resultPath = await _databaseUpdateService.UpdateAsync(targetPath, progress, token);

                progressForm.Close();

                _config.DatabasePath = resultPath;
                _configStore.Save(_config);

                // 只重建曲库相关的服务，让新曲库立即生效。
                // 更新曲库的时候完全可能有下载任务在跑，重建整套服务会把它们全部取消。
                RebuildDatabaseServices();
                await ReloadDatabaseAsync();

                MessageBox.Show(
                    "曲库更新完成。" + Environment.NewLine + Environment.NewLine +
                    "位置：" + resultPath,
                    "曲库更新", MessageBoxButtons.OK, MessageBoxIcon.Information);

                UpdateStatus("曲库更新完成");
            }
            catch (OperationCanceledException)
            {
                progressForm.Close();
                UpdateStatus("曲库更新已取消");
            }
            catch (Exception ex)
            {
                progressForm.Close();
                _logger.Error("曲库更新失败", ex);
                UpdateStatus("曲库更新失败");
                MessageBox.Show("曲库更新失败：" + ex.Message, "KtvDownloader",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                menuItemUpdateDatabase.Enabled = true;
                menuItemCheckUpdate.Enabled = true;
            }
        }

        private async Task ReloadDatabaseAsync()
        {
            try
            {
                var total = await Task.Run(() => _searchService.TotalSongCount);
                var version = _databaseUpdateService.ReadLocalVersion(_config.DatabasePath);
                statusSpring.Text = string.Format("曲库：{0} 首  版本 {1}  {2}",
                    total, version ?? "未知", Path.GetFileName(_config.DatabasePath));
            }
            catch (Exception ex)
            {
                _logger.Warn("刷新曲库信息失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 曲库是否可用；不可用时提示用户。检查本身放到后台线程，理由见
        /// <see cref="IsDatabaseReadyAsync"/>。
        /// </summary>
        private async Task<bool> EnsureDatabaseReadyAsync()
        {
            if (await IsDatabaseReadyAsync()) return true;

            if (!IsDisposed)
            {
                MessageBox.Show("曲库未就绪，请先配置曲库或更新曲库。", "KtvDownloader",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }

        /// <summary>
        /// 在后台线程上判断曲库是否可用。
        /// <para>
        /// ⚠️ <c>ISongSearchService.IsAvailable</c> 不是纯查询：它内部会
        /// <c>TryEnsureConnection()</c>，第一次要真的打开一个近 1GB 的 SQLite 文件，
        /// 曲库刚被更新替换过时还要重开一次。直接在 UI 线程上调就是几百毫秒的界面冻结 ——
        /// 而且每次搜索、每次翻页都会冻一下。
        /// </para>
        /// </summary>
        private Task<bool> IsDatabaseReadyAsync()
        {
            var service = _searchService;
            if (service == null) return Task.FromResult(false);

            return Task.Run(() =>
            {
                try { return service.IsAvailable; }
                catch (Exception ex)
                {
                    _logger.Warn("检查曲库可用性失败: " + ex.Message);
                    return false;
                }
            });
        }

        #endregion

        #region 歌单

        private async void OnPlaylistsClick(object sender, EventArgs e)
        {
            if (!await EnsureDatabaseReadyAsync()) return;

            try
            {
                using (var form = new PlaylistForm(_playlistService, _logger))
                {
                    form.ShowDialog(this);

                    // 用户在歌单窗口里选的歌，直接进队列。
                    if (form.SongsToDownload.Count == 0) return;

                    var outputDirectory = _config.OutputDirectory;
                    if (string.IsNullOrWhiteSpace(outputDirectory))
                    {
                        MessageBox.Show("请先在设置中配置下载目录。", "KtvDownloader",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);

                    var options = BuildDownloadOptions();
                    foreach (var song in form.SongsToDownload)
                        EnqueueSong(song, outputDirectory, options);

                    UpdateStatus(string.Format("已加入队列 {0} 首", form.SongsToDownload.Count));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("打开歌单失败", ex);
                MessageBox.Show("打开歌单失败：" + ex.Message, "KtvDownloader",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region 菜单

        private void OnSettingsClick(object sender, EventArgs e)
        {
            using (var form = new SettingsForm(_config))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;

                // 记住改动前的值，用来判断到底哪些东西需要重建。
                var oldDatabasePath = _config.DatabasePath;

                form.ApplyTo(_config);
                _config.Normalize();

                if (_configStore.Save(_config))
                {
                    _logger.Info("配置已保存");
                    UpdateStatus("设置已保存");
                }
                else
                {
                    _logger.Warn("配置保存失败: " + _configStore.LastError);
                    MessageBox.Show(
                        "设置保存失败，本次改动只在这次运行里生效。" + Environment.NewLine +
                        (_configStore.LastError ?? "请检查目录权限。"),
                        "KtvDownloader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                // 配置变了，**原地**应用：并发数热调整、ffmpeg 路径热更新、
                // 曲库路径变了才重建搜索服务。
                // 绝不能调 InitializeServices() —— 那会 Dispose 下载队列，
                // 而队列 Dispose 等于 CancelAll，用户只是改了个目录，正在下的歌就全没了。
                ApplyConfigurationChanges(oldDatabasePath);

                if (!string.Equals(oldDatabasePath, _config.DatabasePath, StringComparison.OrdinalIgnoreCase))
                {
                    CheckDatabase();
                }
            }
        }

        private void OnOpenOutputClick(object sender, EventArgs e)
        {
            try
            {
                var directory = _config.OutputDirectory;
                if (string.IsNullOrWhiteSpace(directory)) return;

                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                Process.Start("explorer.exe", "\"" + directory + "\"");
            }
            catch (Exception ex)
            {
                _logger.Error("打开下载目录失败", ex);
                MessageBox.Show("无法打开下载目录：" + ex.Message, "KtvDownloader",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnAboutClick(object sender, EventArgs e)
        {
            var ffmpegStatus = _mediaService != null && _mediaService.IsFfmpegAvailable
                ? "可用 (" + Path.GetFileName(_mediaService.FfmpegPath) + ")"
                : "不可用（转码与音轨分离需要 ffmpeg）";

            MessageBox.Show(
                "KtvDownloader" + Environment.NewLine +
                "版本 " + Infrastructure.Extensions.AssemblyInfoHelper.GetVersion() + Environment.NewLine + Environment.NewLine +
                "麦动 KTV 曲库下载器" + Environment.NewLine +
                "搜索 / 批量下载 / 音轨分离" + Environment.NewLine + Environment.NewLine +
                "ffmpeg：" + ffmpegStatus + Environment.NewLine +
                "曲库：" + (_config.DatabasePath ?? "未配置"),
                "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnExitClick(object sender, EventArgs e)
        {
            Close();
        }

        #endregion

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_downloadQueue != null && _downloadQueue.ActiveTaskCount > 0)
            {
                var result = MessageBox.Show(
                    "还有下载任务未完成，确定要退出吗？",
                    "KtvDownloader", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            if (_searchCts != null)
            {
                _searchCts.Cancel();
                _searchCts.Dispose();
                _searchCts = null;
            }

            if (_updateCts != null)
            {
                _updateCts.Cancel();
                _updateCts.Dispose();
                _updateCts = null;
            }

            DisposeServices();
            base.OnFormClosing(e);
        }
    }
}
