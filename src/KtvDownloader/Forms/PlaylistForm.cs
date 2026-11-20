using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Models;
using KtvDownloader.Services.Interfaces;

namespace KtvDownloader.Forms
{
    /// <summary>
    /// 歌单浏览窗体。左侧选歌单，右侧看曲目，可一键加入下载队列。
    /// </summary>
    public partial class PlaylistForm : Form
    {
        private readonly IPlaylistService _playlistService;
        private readonly ILogger _logger;

        private readonly List<Song> _songs = new List<Song>();
        private CancellationTokenSource _loadCts;

        /// <summary>用户选择要下载的歌曲。</summary>
        public List<Song> SongsToDownload { get; } = new List<Song>();

        public PlaylistForm(IPlaylistService playlistService, ILogger logger)
        {
            _playlistService = playlistService ?? throw new ArgumentNullException(nameof(playlistService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            InitializeComponent();
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            cboPlaylistType.Items.Clear();
            cboPlaylistType.Items.Add("全部");
            cboPlaylistType.Items.Add("主题歌单");
            cboPlaylistType.Items.Add("榜单");
            cboPlaylistType.Items.Add("排行榜");
            cboPlaylistType.SelectedIndex = 0;

            await LoadPlaylistsAsync(0);
        }

        private async System.Threading.Tasks.Task LoadPlaylistsAsync(int type)
        {
            lvPlaylists.Items.Clear();
            lvPlaylistSongs.Items.Clear();
            _songs.Clear();
            btnAddAll.Enabled = false;
            btnAddSelected.Enabled = false;

            lblSongSummary.Text = "正在加载歌单...";

            try
            {
                var playlists = await _playlistService.GetPlaylistsAsync(type);

                lvPlaylists.BeginUpdate();
                try
                {
                    foreach (var playlist in playlists)
                    {
                        var item = new ListViewItem(playlist.Name);
                        item.SubItems.Add(playlist.TypeName);
                        item.SubItems.Add(playlist.SongCount.ToString());
                        item.Tag = playlist;
                        lvPlaylists.Items.Add(item);
                    }
                }
                finally
                {
                    lvPlaylists.EndUpdate();
                }

                lblSongSummary.Text = string.Format("共 {0} 个歌单", playlists.Count);
            }
            catch (Exception ex)
            {
                _logger.Error("加载歌单失败", ex);
                lblSongSummary.Text = "加载歌单失败";
            }
        }

        private async void OnPlaylistTypeChanged(object sender, EventArgs e)
        {
            var type = cboPlaylistType.SelectedIndex;
            await LoadPlaylistsAsync(type);
        }

        private async void OnPlaylistSelected(object sender, EventArgs e)
        {
            if (lvPlaylists.SelectedItems.Count == 0) return;

            var playlist = lvPlaylists.SelectedItems[0].Tag as Playlist;
            if (playlist == null) return;

            if (_loadCts != null)
            {
                _loadCts.Cancel();
                _loadCts.Dispose();
            }
            _loadCts = new CancellationTokenSource();

            lvPlaylistSongs.Items.Clear();
            _songs.Clear();
            btnAddAll.Enabled = false;
            btnAddSelected.Enabled = false;
            lblSongSummary.Text = "正在加载曲目...";

            try
            {
                var songs = await _playlistService.GetPlaylistSongsAsync(playlist.Id, _loadCts.Token);

                _songs.AddRange(songs);

                lvPlaylistSongs.BeginUpdate();
                try
                {
                    for (var i = 0; i < songs.Count; i++)
                    {
                        var song = songs[i];
                        var item = new ListViewItem((i + 1).ToString());
                        item.SubItems.Add(song.Name);
                        item.SubItems.Add(song.Singer);
                        item.SubItems.Add(song.MusicNo);
                        item.Tag = song;
                        lvPlaylistSongs.Items.Add(item);
                    }
                }
                finally
                {
                    lvPlaylistSongs.EndUpdate();
                }

                btnAddAll.Enabled = songs.Count > 0;
                btnAddSelected.Enabled = songs.Count > 0;
                lblSongSummary.Text = string.Format("{0} 共 {1} 首", playlist.Name, songs.Count);
            }
            catch (OperationCanceledException)
            {
                // 切换到别的歌单，正常。
            }
            catch (Exception ex)
            {
                _logger.Error("加载歌单曲目失败", ex);
                lblSongSummary.Text = "加载曲目失败：" + ex.Message;
            }
        }

        private void OnAddSelectedClick(object sender, EventArgs e)
        {
            if (lvPlaylistSongs.SelectedItems.Count == 0)
            {
                MessageBox.Show("请先选择要加入队列的曲目。", "歌单",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            foreach (ListViewItem item in lvPlaylistSongs.SelectedItems)
            {
                var song = item.Tag as Song;
                if (song != null) SongsToDownload.Add(song);
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnAddAllClick(object sender, EventArgs e)
        {
            if (_songs.Count == 0) return;

            var result = MessageBox.Show(
                string.Format("确定要把这 {0} 首歌全部加入下载队列吗？", _songs.Count),
                "歌单", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            SongsToDownload.AddRange(_songs);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnCloseClick(object sender, EventArgs e)
        {
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_loadCts != null)
            {
                _loadCts.Cancel();
                _loadCts.Dispose();
                _loadCts = null;
            }
            base.OnFormClosing(e);
        }
    }
}
