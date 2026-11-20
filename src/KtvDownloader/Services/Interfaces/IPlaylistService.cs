using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Models;

namespace KtvDownloader.Services.Interfaces
{
    /// <summary>
    /// 歌单 / 榜单服务。数据来源为 muse.db 的 playlists 与 playlist_songs 表。
    /// </summary>
    public interface IPlaylistService
    {
        /// <summary>获取所有歌单。</summary>
        Task<System.Collections.Generic.IReadOnlyList<Playlist>> GetPlaylistsAsync(
            int type,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>获取指定歌单下的歌曲，按歌单内排序。</summary>
        Task<System.Collections.Generic.IReadOnlyList<Song>> GetPlaylistSongsAsync(
            string playlistId,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
