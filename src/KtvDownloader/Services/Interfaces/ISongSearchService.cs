using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Models;

namespace KtvDownloader.Services.Interfaces
{
    /// <summary>
    /// 曲库检索服务。数据来源为 muse.db（SQLite）。
    /// </summary>
    public interface ISongSearchService
    {
        /// <summary>曲库是否已就绪（文件存在且能打开）。</summary>
        bool IsAvailable { get; }

        /// <summary>曲库中的歌曲总数。</summary>
        int TotalSongCount { get; }

        /// <summary>按歌名检索。支持中文与拼音首字母。</summary>
        Task<IReadOnlyList<Song>> SearchSongsAsync(
            string keyword,
            string language,
            int offset,
            int limit,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按歌手名检索该歌手的歌曲。</summary>
        Task<IReadOnlyList<Song>> SearchBySingerAsync(
            string keyword,
            int offset,
            int limit,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>检索歌手。</summary>
        Task<IReadOnlyList<Singer>> SearchSingersAsync(
            string keyword,
            int offset,
            int limit,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>获取热门歌曲。</summary>
        Task<IReadOnlyList<Song>> GetHotSongsAsync(
            int offset,
            int limit,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按歌号取单曲，用于补全信息。</summary>
        Task<Song> GetByMusicNoAsync(string musicNo, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>获取全部语种（供筛选下拉框）。</summary>
        Task<IReadOnlyList<string>> GetLanguagesAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按语种浏览歌曲（分类模式）。</summary>
        Task<IReadOnlyList<Song>> GetSongsByLanguageAsync(
            string language,
            int offset,
            int limit,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// 按标签浏览歌曲（分类模式）。
        /// 标签来自 songs.tags_str，如「流行歌曲」「高清歌曲」。
        /// </summary>
        Task<IReadOnlyList<Song>> GetSongsByTagAsync(
            string tag,
            int offset,
            int limit,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>获取全部可用标签（供分类浏览）。</summary>
        Task<IReadOnlyList<string>> GetTagsAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// 获取新歌（按曲库入库顺序倒序）。
        /// 曲库没有入库时间字段，用 tid 倒序近似，tid 越大通常越新。
        /// </summary>
        Task<IReadOnlyList<Song>> GetNewSongsAsync(
            int offset,
            int limit,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// 统计匹配条件的总条数。用于分页显示总页数。
        /// </summary>
        Task<int> CountAsync(
            string keyword,
            string language,
            bool bySinger,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// 「歌名 + 歌手」组合搜索。只返回同时满足两个条件的歌曲。
        /// <para>
        /// 匹配规则：歌名命中，且（歌手名命中 OR 歌名中包含歌手名）。
        /// 后者用于匹配「发如雪（Cover：周杰伦）」这类无原唱授权、歌手被标为佚名的情况。
        /// </para>
        /// </summary>
        Task<IReadOnlyList<Song>> SearchCombinedAsync(
            string songName,
            string singerName,
            int offset,
            int limit,
            bool fastOnly = false,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>校验歌手名是否存在于曲库，用于解析「歌名 歌手」格式。</summary>
        Task<bool> SingerExistsAsync(string singerName, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// 快速搜索：只用前缀匹配（走索引，通常 1-10ms）。
        /// 用于「秒出首屏」，结果可能不完整，后续用 <see cref="SearchSongsAsync"/> 补齐。
        /// </summary>
        Task<IReadOnlyList<Song>> SearchSongsFastAsync(
            string keyword,
            string language,
            int offset,
            int limit,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// 一次性取出全部匹配结果（用于后台预加载）。
        /// 数据量大时较慢，调用方应放到后台线程并限制上限。
        /// </summary>
        Task<IReadOnlyList<Song>> GetAllMatchesAsync(
            string keyword,
            string language,
            bool bySinger,
            int maxCount,
            IProgress<int> progress,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
