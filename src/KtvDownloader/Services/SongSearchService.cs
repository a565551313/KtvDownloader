using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Infrastructure.Config;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Models;
using KtvDownloader.Services.Interfaces;

namespace KtvDownloader.Services
{
    /// <summary>
    /// 曲库检索服务。直接查询 muse.db（SQLite）。
    /// <para>
    /// 检索策略与官方 App 一致：
    /// 英文/拼音走 <c>name_cap</c>（首字母）与 <c>name_full</c>（全拼）的 GLOB 前缀匹配，
    /// 这两列有索引，查询很快；
    /// 中文走 <c>name</c> / <c>name_trim</c> 的 LIKE，只匹配中文列以减少逐行比较开销。
    /// </para>
    /// </summary>
    public sealed class SongSearchService : ISongSearchService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly string _databasePath;
        private readonly object _connectionLock = new object();

        private SQLiteConnection _connection;
        private bool _initialized;
        private int _totalSongCount = -1;

        /// <summary>
        /// 歌曲列表查询语句。
        /// <para>
        /// 注意：这里故意不包含歌手名子查询。
        /// 原来的写法用 <c>(SELECT group_concat(...) ... WHERE song_id=s.id)</c>，
        /// 每行都要跑一次关联查询，50 行要 450ms。
        /// 改为先取歌曲，再用一条 IN 查询批量补歌手名，同样 50 行只要 2ms。
        /// </para>
        /// </summary>
        private const string SongSelect = @"
SELECT s.id, s.tid, s.name, s.name_trim, s.lang,
       s.hot_score, s.filename, s.accomp, s.mv_type, s.volume, s.tags_str
  FROM songs s
 WHERE s.deleted_at IS NULL
   AND s.filename IS NOT NULL
   AND s.filename <> ''";

        /// <summary>
        /// 排序规则。
        /// <para>
        /// 官方是 <c>rec_score DESC, local_hot_score DESC, hot_score DESC</c>，
        /// 但 rec_score 全库只有 24 首有值（其余 NULL），
        /// 加进 ORDER BY 会让 SQLite 无法利用任何索引，只能全表排序。
        /// 去掉后按 hot_score 排序，结果几乎一致但快很多。
        /// </para>
        /// </summary>
        private const string OrderClause =
            " ORDER BY s.hot_score DESC, s.name ASC";

        public SongSearchService(ILogger logger, string databasePath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databasePath = databasePath;
        }

        public bool IsAvailable => TryEnsureConnection();

        public int TotalSongCount
        {
            get
            {
                if (_totalSongCount >= 0) return _totalSongCount;
                if (!TryEnsureConnection()) return 0;

                try
                {
                    lock (_connectionLock)
                    {
                        if (!HasConnectionLocked) return 0;

                        using (var command = _connection.CreateCommand())
                        {
                            command.CommandText = "SELECT COUNT(*) FROM songs WHERE deleted_at IS NULL";
                            _totalSongCount = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("统计歌曲总数失败", ex);
                    // ⚠️ 不能把失败结果缓存成 0：getter 是 ">= 0 就直接返回"，
                    // 一次瞬时失败（例如曲库正在被更新替换）会让主界面永久显示"0 首"直到重启。
                    _totalSongCount = -1;
                    return 0;
                }

                return _totalSongCount;
            }
        }

        public Task<IReadOnlyList<Song>> SearchSongsAsync(
            string keyword,
            string language,
            int offset,
            int limit,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run<IReadOnlyList<Song>>(
                () => SearchSongsCore(keyword, language, offset, limit, cancellationToken),
                cancellationToken);
        }

        /// <summary>
        /// 按歌名搜索的同步实现。
        /// <para>
        /// 抽成 Core 是为了消除 sync-over-async：<see cref="GetAllMatchesAsync"/> 本身已经跑在
        /// 线程池线程上，早期它在里面用 <c>.GetAwaiter().GetResult()</c> 调异步方法，
        /// 等于"一个线程池线程阻塞等另一个线程池线程"，预取 5000 条要这样嵌套十次，
        /// 并发一高就是线程池饥饿。
        /// </para>
        /// </summary>
        private IReadOnlyList<Song> SearchSongsCore(
            string keyword,
            string language,
            int offset,
            int limit,
            CancellationToken cancellationToken)
        {
            var trimmed = (keyword ?? string.Empty).Trim();
            if (trimmed.Length == 0) return new List<Song>();

            if (!TryEnsureConnection()) return new List<Song>();

            try
            {
                var builder = new StringBuilder(SongSelect);
                var parameters = new List<SQLiteParameter>();

                if (IsAscii(trimmed))
                {
                    // 拼音/英文：前缀 GLOB，走索引。
                    var pattern = trimmed.ToUpperInvariant().Replace("[", "[[]") + "*";
                    builder.Append(" AND (s.name_cap GLOB @kwCap OR s.name_full GLOB @kwFull)");
                    parameters.Add(new SQLiteParameter("@kwCap", pattern));
                    parameters.Add(new SQLiteParameter("@kwFull", pattern));
                }
                else
                {
                    // 中文：LIKE 全表扫，只匹配中文列。
                    builder.Append(" AND (s.name LIKE @kw OR s.name_trim LIKE @kwTrim)");
                    parameters.Add(new SQLiteParameter("@kw", "%" + trimmed + "%"));
                    parameters.Add(new SQLiteParameter("@kwTrim", "%" + trimmed + "%"));
                }

                AppendLanguageFilter(builder, parameters, language);

                builder.Append(OrderClause);
                builder.Append(" LIMIT @limit OFFSET @offset");
                parameters.Add(new SQLiteParameter("@limit", Math.Max(1, limit)));
                parameters.Add(new SQLiteParameter("@offset", Math.Max(0, offset)));

                return QuerySongs(builder.ToString(), parameters, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error("搜索歌曲失败 keyword=" + trimmed, ex);
                return new List<Song>();
            }
        }

        public Task<IReadOnlyList<Song>> SearchBySingerAsync(
            string keyword,
            int offset,
            int limit,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run<IReadOnlyList<Song>>(
                () => SearchBySingerCore(keyword, offset, limit, cancellationToken),
                cancellationToken);
        }

        /// <summary>按歌手搜索的同步实现，理由同 <see cref="SearchSongsCore"/>。</summary>
        private IReadOnlyList<Song> SearchBySingerCore(
            string keyword,
            int offset,
            int limit,
            CancellationToken cancellationToken)
        {
            var trimmed = (keyword ?? string.Empty).Trim();
            if (trimmed.Length == 0) return new List<Song>();
            if (!TryEnsureConnection()) return new List<Song>();

            try
            {
                var builder = new StringBuilder(SongSelect);
                var parameters = new List<SQLiteParameter>();

                builder.Append(@" AND s.id IN (
SELECT r.song_id
  FROM song_singer_relations r
  JOIN singers sg ON sg.id = r.singer_id
 WHERE sg.name LIKE @singer OR sg.name_trim LIKE @singerTrim)");

                var pattern = "%" + trimmed + "%";
                parameters.Add(new SQLiteParameter("@singer", pattern));
                parameters.Add(new SQLiteParameter("@singerTrim", pattern));

                builder.Append(OrderClause);
                builder.Append(" LIMIT @limit OFFSET @offset");
                parameters.Add(new SQLiteParameter("@limit", Math.Max(1, limit)));
                parameters.Add(new SQLiteParameter("@offset", Math.Max(0, offset)));

                return QuerySongs(builder.ToString(), parameters, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error("按歌手搜索失败 keyword=" + trimmed, ex);
                return new List<Song>();
            }
        }

        public Task<IReadOnlyList<Singer>> SearchSingersAsync(
            string keyword,
            int offset,
            int limit,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run<IReadOnlyList<Singer>>(() =>
            {
                var trimmed = (keyword ?? string.Empty).Trim();
                if (!TryEnsureConnection()) return new List<Singer>();

                try
                {
                    var builder = new StringBuilder(@"
SELECT id, name, name_cap, name_full, name_trim, image, type, area, hot_score
  FROM singers
 WHERE deleted_at IS NULL");

                    var parameters = new List<SQLiteParameter>();

                    if (trimmed.Length > 0)
                    {
                        if (IsAscii(trimmed))
                        {
                            var pattern = trimmed.ToUpperInvariant() + "*";
                            builder.Append(" AND (name_cap GLOB @cap OR name_full GLOB @full)");
                            parameters.Add(new SQLiteParameter("@cap", pattern));
                            parameters.Add(new SQLiteParameter("@full", pattern));
                        }
                        else
                        {
                            builder.Append(" AND (name LIKE @kw OR name_trim LIKE @kwTrim)");
                            parameters.Add(new SQLiteParameter("@kw", "%" + trimmed + "%"));
                            parameters.Add(new SQLiteParameter("@kwTrim", "%" + trimmed + "%"));
                        }
                    }

                    builder.Append(" ORDER BY hot_score DESC, name ASC LIMIT @limit OFFSET @offset");
                    parameters.Add(new SQLiteParameter("@limit", Math.Max(1, limit)));
                    parameters.Add(new SQLiteParameter("@offset", Math.Max(0, offset)));

                    var result = new List<Singer>();
                    lock (_connectionLock)
                    {
                        if (!HasConnectionLocked) return result;

                        using (var command = _connection.CreateCommand())
                        {
                            command.CommandText = builder.ToString();
                            command.Parameters.AddRange(parameters.ToArray());

                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    result.Add(MapSinger(reader));
                                }
                            }
                        }
                    }
                    return (IReadOnlyList<Singer>)result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error("搜索歌手失败", ex);
                    return new List<Singer>();
                }
            }, cancellationToken);
        }

        public Task<IReadOnlyList<Song>> GetHotSongsAsync(
            int offset,
            int limit,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run<IReadOnlyList<Song>>(() =>
            {
                if (!TryEnsureConnection()) return new List<Song>();

                try
                {
                    var sql = SongSelect + OrderClause + " LIMIT @limit OFFSET @offset";
                    var parameters = new List<SQLiteParameter>
                    {
                        new SQLiteParameter("@limit", Math.Max(1, limit)),
                        new SQLiteParameter("@offset", Math.Max(0, offset))
                    };
                    return QuerySongs(sql, parameters, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error("获取热门歌曲失败", ex);
                    return new List<Song>();
                }
            }, cancellationToken);
        }

        public Task<Song> GetByMusicNoAsync(string musicNo, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(musicNo)) return null;
                if (!TryEnsureConnection()) return null;

                try
                {
                    var sql = SongSelect + " AND s.filename LIKE @file LIMIT 1";
                    var parameters = new List<SQLiteParameter>
                    {
                        new SQLiteParameter("@file", musicNo.Trim() + "%")
                    };

                    var list = QuerySongs(sql, parameters, cancellationToken);
                    return list.Count > 0 ? list[0] : null;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error("按歌号查询失败 musicNo=" + musicNo, ex);
                    return null;
                }
            }, cancellationToken);
        }

        public Task<IReadOnlyList<string>> GetLanguagesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run<IReadOnlyList<string>>(() =>
            {
                var result = new List<string>();
                if (!TryEnsureConnection()) return result;

                try
                {
                    lock (_connectionLock)
                    {
                        if (!HasConnectionLocked) return result;

                        using (var command = _connection.CreateCommand())
                        {
                            command.CommandText =
                                "SELECT name FROM song_langs WHERE deleted_at IS NULL ORDER BY sort_no";

                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    var name = reader.IsDBNull(0) ? null : reader.GetString(0);
                                    if (!string.IsNullOrWhiteSpace(name)) result.Add(name);
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error("获取语种列表失败", ex);
                }

                return result;
            }, cancellationToken);
        }


        public Task<IReadOnlyList<Song>> SearchSongsFastAsync(
            string keyword, string language, int offset, int limit,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run<IReadOnlyList<Song>>(() =>
            {
                var trimmed = (keyword ?? string.Empty).Trim();
                if (trimmed.Length == 0 || !TryEnsureConnection()) return new List<Song>();

                try
                {
                    var builder = new StringBuilder(SongSelect);
                    var parameters = new List<SQLiteParameter>();

                    // 只用前缀匹配：name_trim GLOB '关键词*' 能命中索引，通常 1-10ms。
                    // 中文与拼音都支持，因为 name_trim 是纯中文、name_cap 是拼音首字母。
                    builder.Append(" AND (s.name_trim GLOB @prefix OR s.name_cap GLOB @capPrefix)");
                    parameters.Add(new SQLiteParameter("@prefix", trimmed + "*"));
                    parameters.Add(new SQLiteParameter("@capPrefix", trimmed.ToUpperInvariant() + "*"));

                    AppendLanguageFilter(builder, parameters, language);
                    builder.Append(OrderClause);
                    builder.Append(" LIMIT @limit OFFSET @offset");
                    parameters.Add(new SQLiteParameter("@limit", Math.Max(1, limit)));
                    parameters.Add(new SQLiteParameter("@offset", Math.Max(0, offset)));

                    return QuerySongs(builder.ToString(), parameters, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.Error("快速搜索失败", ex);
                    return new List<Song>();
                }
            }, cancellationToken);
        }

        #region 组合搜索

        public Task<IReadOnlyList<Song>> SearchCombinedAsync(
            string songName, string singerName, int offset, int limit,
            bool fastOnly = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run<IReadOnlyList<Song>>(() =>
            {
                var song = (songName ?? string.Empty).Trim();
                var singer = (singerName ?? string.Empty).Trim();

                if (song.Length == 0 && singer.Length == 0) return new List<Song>();
                if (!TryEnsureConnection()) return new List<Song>();

                // 只有歌手没有歌名 -> 退化为按歌手搜索。
                // 这里已经在线程池线程上，直接调同步 Core，不要嵌套等待异步方法。
                if (song.Length == 0)
                {
                    return SearchBySingerCore(singer, offset, limit, cancellationToken);
                }

                try
                {
                    var builder = new StringBuilder(SongSelect);
                    var parameters = new List<SQLiteParameter>();

                    // 歌名条件
                    if (fastOnly)
                    {
                        // 快路径：前缀 GLOB，走索引。
                        builder.Append(" AND (s.name_trim GLOB @songPrefix OR s.name_cap GLOB @songCap)");
                        parameters.Add(new SQLiteParameter("@songPrefix", song + "*"));
                        parameters.Add(new SQLiteParameter("@songCap", song.ToUpperInvariant() + "*"));
                    }
                    else
                    {
                        // 完整路径：LIKE，能命中歌名中间部分。
                        builder.Append(" AND (s.name LIKE @songLike OR s.name_trim LIKE @songLike)");
                        parameters.Add(new SQLiteParameter("@songLike", "%" + song + "%"));
                    }

                    // 歌手条件
                    if (singer.Length > 0)
                    {
                        // 两种命中方式：
                        // A. 关联表里确实有这个歌手
                        // B. 歌名里包含歌手名（覆盖「发如雪（Cover：周杰伦）」）
                        builder.Append(@" AND (
    EXISTS (SELECT 1 FROM song_singer_relations r
             JOIN singers sg ON sg.id = r.singer_id
            WHERE r.song_id = s.id
              AND (sg.name LIKE @singerLike OR sg.name_trim LIKE @singerLike))
    OR s.name LIKE @singerLike
)");
                        parameters.Add(new SQLiteParameter("@singerLike", "%" + singer + "%"));
                    }

                    builder.Append(OrderClause);
                    builder.Append(" LIMIT @limit OFFSET @offset");
                    parameters.Add(new SQLiteParameter("@limit", Math.Max(1, limit)));
                    parameters.Add(new SQLiteParameter("@offset", Math.Max(0, offset)));

                    return QuerySongs(builder.ToString(), parameters, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.Error(string.Format("组合搜索失败 歌名={0} 歌手={1}", song, singer), ex);
                    return new List<Song>();
                }
            }, cancellationToken);
        }

        public Task<bool> SingerExistsAsync(
            string singerName, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() =>
            {
                var trimmed = (singerName ?? string.Empty).Trim();
                if (trimmed.Length == 0 || !TryEnsureConnection()) return false;

                try
                {
                    lock (_connectionLock)
                    {
                        if (!HasConnectionLocked) return false;

                        using (var command = _connection.CreateCommand())
                        {
                            command.CommandText =
                                "SELECT 1 FROM singers WHERE deleted_at IS NULL " +
                                "AND (name = @name OR name_trim = @name) LIMIT 1";
                            command.Parameters.AddWithValue("@name", trimmed);
                            return command.ExecuteScalar() != null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn("校验歌手名失败: " + ex.Message);
                    return false;
                }
            }, cancellationToken);
        }

        /// <summary>统计组合搜索的总条数。</summary>
        public Task<int> CountCombinedAsync(
            string songName, string singerName,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() =>
            {
                var song = (songName ?? string.Empty).Trim();
                var singer = (singerName ?? string.Empty).Trim();
                if (song.Length == 0 || !TryEnsureConnection()) return 0;

                try
                {
                    var builder = new StringBuilder(@"
SELECT COUNT(*) FROM songs s
 WHERE s.deleted_at IS NULL AND s.filename IS NOT NULL AND s.filename <> ''
   AND (s.name LIKE @songLike OR s.name_trim LIKE @songLike)");

                    var parameters = new List<SQLiteParameter>
                    {
                        new SQLiteParameter("@songLike", "%" + song + "%")
                    };

                    if (singer.Length > 0)
                    {
                        builder.Append(@" AND (
    EXISTS (SELECT 1 FROM song_singer_relations r
             JOIN singers sg ON sg.id = r.singer_id
            WHERE r.song_id = s.id
              AND (sg.name LIKE @singerLike OR sg.name_trim LIKE @singerLike))
    OR s.name LIKE @singerLike)");
                        parameters.Add(new SQLiteParameter("@singerLike", "%" + singer + "%"));
                    }

                    lock (_connectionLock)
                    {
                        if (!HasConnectionLocked) return 0;

                        using (var command = _connection.CreateCommand())
                        {
                            command.CommandText = builder.ToString();
                            command.Parameters.AddRange(parameters.ToArray());
                            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("统计组合搜索结果失败", ex);
                    return 0;
                }
            }, cancellationToken);
        }

        #endregion

        #region 分类 / 新歌 / 统计

        public Task<IReadOnlyList<Song>> GetSongsByLanguageAsync(
            string language, int offset, int limit,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run<IReadOnlyList<Song>>(() =>
            {
                if (!TryEnsureConnection()) return new List<Song>();

                try
                {
                    var builder = new StringBuilder(SongSelect);
                    var parameters = new List<SQLiteParameter>();

                    AppendLanguageFilter(builder, parameters, language);
                    builder.Append(OrderClause);
                    builder.Append(" LIMIT @limit OFFSET @offset");
                    parameters.Add(new SQLiteParameter("@limit", Math.Max(1, limit)));
                    parameters.Add(new SQLiteParameter("@offset", Math.Max(0, offset)));

                    return QuerySongs(builder.ToString(), parameters, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.Error("按语种浏览失败", ex);
                    return new List<Song>();
                }
            }, cancellationToken);
        }

        public Task<IReadOnlyList<Song>> GetSongsByTagAsync(
            string tag, int offset, int limit,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run<IReadOnlyList<Song>>(() =>
            {
                var trimmed = (tag ?? string.Empty).Trim();
                if (trimmed.Length == 0 || !TryEnsureConnection()) return new List<Song>();

                try
                {
                    var builder = new StringBuilder(SongSelect);
                    var parameters = new List<SQLiteParameter>();

                    // tags_str 是 JSON 数组字符串，用 LIKE 匹配标签名。
                    builder.Append(" AND s.tags_str LIKE @tag");
                    parameters.Add(new SQLiteParameter("@tag", "%\"" + trimmed + "\"%"));

                    builder.Append(OrderClause);
                    builder.Append(" LIMIT @limit OFFSET @offset");
                    parameters.Add(new SQLiteParameter("@limit", Math.Max(1, limit)));
                    parameters.Add(new SQLiteParameter("@offset", Math.Max(0, offset)));

                    return QuerySongs(builder.ToString(), parameters, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.Error("按标签浏览失败", ex);
                    return new List<Song>();
                }
            }, cancellationToken);
        }

        public Task<IReadOnlyList<string>> GetTagsAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run<IReadOnlyList<string>>(() =>
            {
                var result = new List<string>();
                if (!TryEnsureConnection()) return result;

                try
                {
                    lock (_connectionLock)
                    {
                        if (!HasConnectionLocked) return result;

                        // 从 tag_groups 取标准标签集合，比扫全表 tags_str 快得多。
                        using (var command = _connection.CreateCommand())
                        {
                            command.CommandText = @"
SELECT tags FROM tag_groups
 WHERE deleted_at IS NULL AND tags IS NOT NULL AND tags <> ''";

                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    if (reader.IsDBNull(0)) continue;

                                    foreach (var tag in SplitTags(reader.GetString(0)))
                                    {
                                        if (!result.Contains(tag)) result.Add(tag);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.Error("获取标签失败", ex);
                }

                return result;
            }, cancellationToken);
        }

        public Task<IReadOnlyList<Song>> GetNewSongsAsync(
            int offset, int limit,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run<IReadOnlyList<Song>>(() =>
            {
                if (!TryEnsureConnection()) return new List<Song>();

                try
                {
                    var sql = SongSelect
                              + " ORDER BY CAST(s.tid AS INTEGER) DESC"
                              + " LIMIT @limit OFFSET @offset";

                    var parameters = new List<SQLiteParameter>
                    {
                        new SQLiteParameter("@limit", Math.Max(1, limit)),
                        new SQLiteParameter("@offset", Math.Max(0, offset))
                    };

                    return QuerySongs(sql, parameters, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.Error("获取新歌失败", ex);
                    return new List<Song>();
                }
            }, cancellationToken);
        }

        public Task<int> CountAsync(
            string keyword, string language, bool bySinger,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() =>
            {
                var trimmed = (keyword ?? string.Empty).Trim();
                if (trimmed.Length == 0 || !TryEnsureConnection()) return 0;

                try
                {
                    var builder = new StringBuilder();
                    var parameters = new List<SQLiteParameter>();

                    if (bySinger)
                    {
                        builder.Append(@"
SELECT COUNT(*) FROM songs s
 WHERE s.deleted_at IS NULL AND s.filename IS NOT NULL AND s.filename <> ''
   AND s.id IN (SELECT r.song_id FROM song_singer_relations r
                JOIN singers sg ON sg.id = r.singer_id
                WHERE sg.name LIKE @kw OR sg.name_trim LIKE @kwTrim)");
                        var pattern = "%" + trimmed + "%";
                        parameters.Add(new SQLiteParameter("@kw", pattern));
                        parameters.Add(new SQLiteParameter("@kwTrim", pattern));
                    }
                    else
                    {
                        builder.Append(@"
SELECT COUNT(*) FROM songs s
 WHERE s.deleted_at IS NULL AND s.filename IS NOT NULL AND s.filename <> ''");

                        if (IsAscii(trimmed))
                        {
                            var pattern = trimmed.ToUpperInvariant().Replace("[", "[[]") + "*";
                            builder.Append(" AND (s.name_cap GLOB @cap OR s.name_full GLOB @full)");
                            parameters.Add(new SQLiteParameter("@cap", pattern));
                            parameters.Add(new SQLiteParameter("@full", pattern));
                        }
                        else
                        {
                            builder.Append(" AND (s.name LIKE @kw OR s.name_trim LIKE @kwTrim)");
                            parameters.Add(new SQLiteParameter("@kw", "%" + trimmed + "%"));
                            parameters.Add(new SQLiteParameter("@kwTrim", "%" + trimmed + "%"));
                        }

                        AppendLanguageFilter(builder, parameters, language);
                    }

                    lock (_connectionLock)
                    {
                        if (!HasConnectionLocked) return 0;

                        using (var command = _connection.CreateCommand())
                        {
                            command.CommandText = builder.ToString();
                            command.Parameters.AddRange(parameters.ToArray());
                            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.Error("统计结果数失败", ex);
                    return 0;
                }
            }, cancellationToken);
        }

        public Task<IReadOnlyList<Song>> GetAllMatchesAsync(
            string keyword, string language, bool bySinger, int maxCount,
            IProgress<int> progress,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run<IReadOnlyList<Song>>(() =>
            {
                var trimmed = (keyword ?? string.Empty).Trim();
                if (trimmed.Length == 0 || !TryEnsureConnection()) return new List<Song>();

                var cap = maxCount <= 0 ? 5000 : maxCount;
                const int PageSize = 500;
                var all = new List<Song>();

                try
                {
                    while (all.Count < cap)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // 已经在 Task.Run 里，直接调同步 Core，避免线程池自己等自己。
                        var page = bySinger
                            ? SearchBySingerCore(trimmed, all.Count, PageSize, cancellationToken)
                            : SearchSongsCore(trimmed, language, all.Count, PageSize, cancellationToken);

                        if (page.Count == 0) break;

                        all.AddRange(page);
                        if (progress != null) progress.Report(all.Count);

                        if (page.Count < PageSize) break;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.Error("后台预加载失败", ex);
                }

                if (all.Count > cap) all.RemoveRange(cap, all.Count - cap);
                return all;
            }, cancellationToken);
        }

        #endregion

        #region 内部实现
        /// <summary>确认连接可用。曲库文件可能被删除或替换，所以每次都要校验。</summary>
        /// <summary>
        /// 确认连接可用。曲库文件可能被删除或替换，所以每次都要校验。
        /// <para>
        /// ⚠️ 所有判断与释放都必须在 <see cref="_connectionLock"/> 内完成。
        /// 早期版本在锁外读 <c>_connection.State</c> 并在文件消失时调 Dispose()，
        /// 而 <see cref="QuerySongs"/> 拿到锁后直接用 <c>_connection</c> 且不判空，
        /// 于是曲库更新的一瞬间并发查询会撞上 NullReferenceException。
        /// </para>
        /// </summary>
        private bool TryEnsureConnection()
        {
            lock (_connectionLock)
            {
                if (_initialized && HasConnectionLocked)
                {
                    if (File.Exists(_databasePath)) return true;

                    // 文件不在了，释放连接（已在锁内，直接内联清理）。
                    CloseConnectionLocked();
                    return false;
                }

                if (HasConnectionLocked) return true;

                if (string.IsNullOrWhiteSpace(_databasePath) || !File.Exists(_databasePath))
                {
                    _logger.Warn("曲库文件不存在: " + (_databasePath ?? "(未配置)"));
                    return false;
                }

                try
                {
                    var builder = new SQLiteConnectionStringBuilder
                    {
                        DataSource = _databasePath,
                        ReadOnly = true,
                        FailIfMissing = true,
                        // 只读 + 池化，避免频繁打开 1GB 文件。
                        Pooling = true
                    };

                    _connection = new SQLiteConnection(builder.ToString());
                    _connection.Open();
                    _initialized = true;
                    _logger.Info("曲库已打开: " + _databasePath);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error("打开曲库失败: " + _databasePath, ex);
                    _connection = null;
                    return false;
                }
            }
        }

        private IReadOnlyList<Song> QuerySongs(
            string sql,
            List<SQLiteParameter> parameters,
            CancellationToken cancellationToken)
        {
            var result = new List<Song>();
            var ids = new List<string>();

            lock (_connectionLock)
            {
                if (!HasConnectionLocked) return result;

                using (var command = _connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.Parameters.AddRange(parameters.ToArray());

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var song = MapSong(reader);
                            result.Add(song);
                            if (!string.IsNullOrEmpty(song.Id)) ids.Add(song.Id);
                        }
                    }
                }

                // 批量补歌手名。一条 IN 查询取代 N 次子查询。
                if (ids.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FillSingerNames(result, ids);
                }
            }

            return result;
        }

        /// <summary>
        /// 一次性查出这批歌曲的所有歌手名，填回 <see cref="Song.Singer"/>。
        /// 实现见 <see cref="SingerNameLookup"/>（内部按 400 个 id 分块，
        /// 避免撞上 SQLite 的绑定变量上限）。
        /// </summary>
        private void FillSingerNames(List<Song> songs, List<string> ids)
        {
            try
            {
                SingerNameLookup.Fill(_connection, songs);
            }
            catch (Exception ex)
            {
                _logger.Warn("批量查询歌手名失败: " + ex.Message);
            }
        }

        private static void AppendLanguageFilter(
            StringBuilder builder,
            List<SQLiteParameter> parameters,
            string language)
        {
            if (string.IsNullOrWhiteSpace(language) ||
                language == "全部" || language == "All")
                return;

            builder.Append(" AND s.lang = @lang");
            parameters.Add(new SQLiteParameter("@lang", language));
        }

        private static bool IsAscii(string value)
        {
            foreach (var c in value)
            {
                if (c >= 128) return false;
            }
            return true;
        }

        private static Song MapSong(SQLiteDataReader reader)
        {
            var fileName = GetString(reader, 6);

            return new Song
            {
                Id = GetString(reader, 0),
                MusicNo = Path.GetFileNameWithoutExtension(fileName),
                Name = GetString(reader, 2),
                NameTrim = GetString(reader, 3),
                Language = GetString(reader, 4),
                HotScore = GetInt(reader, 5),
                FileName = fileName,
                Accomp = GetInt(reader, 7),
                MvType = GetString(reader, 8),
                Volume = GetInt(reader, 9),
                Tags = SplitTags(GetString(reader, 10)),
                // Singer 由 FillSingerNames 批量填充，这里留空。
                Singer = string.Empty
            };
        }

        private static Singer MapSinger(SQLiteDataReader reader)
        {
            return new Singer
            {
                Id = GetString(reader, 0),
                Name = GetString(reader, 1),
                NameCap = GetString(reader, 2),
                NameFull = GetString(reader, 3),
                NameTrim = GetString(reader, 4),
                Image = GetString(reader, 5),
                Type = GetString(reader, 6),
                Area = GetString(reader, 7),
                HotScore = GetInt(reader, 8)
            };
        }

        /// <summary>
        /// 把 tags_str（形如 <c>["高清歌曲","流行歌曲"]</c>）解析成数组。
        /// 统一用 <see cref="MiniJson.ParseStringArray"/>，不再各写一份解析器。
        /// </summary>
        private static string[] SplitTags(string raw)
        {
            return MiniJson.ParseStringArray(raw);
        }

        private static string GetString(SQLiteDataReader reader, int index)
        {
            if (reader.IsDBNull(index)) return string.Empty;
            return reader.GetValue(index) as string ?? reader.GetValue(index).ToString();
        }

        private static int GetInt(SQLiteDataReader reader, int index)
        {
            if (reader.IsDBNull(index)) return 0;
            var value = reader.GetValue(index);
            int parsed;
            if (value is int) return (int)value;
            if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return 0;
        }

        #endregion

        public void Dispose()
        {
            lock (_connectionLock)
            {
                CloseConnectionLocked();
            }
        }

        /// <summary>连接是否可用。**只能在持有 <see cref="_connectionLock"/> 时调用**。</summary>
        private bool HasConnectionLocked
        {
            get
            {
                return _connection != null &&
                       _connection.State == System.Data.ConnectionState.Open;
            }
        }

        /// <summary>关闭并清空连接。**只能在持有 <see cref="_connectionLock"/> 时调用**。</summary>
        private void CloseConnectionLocked()
        {
            if (_connection != null)
            {
                try { _connection.Close(); } catch { }
                try { _connection.Dispose(); } catch { }
                _connection = null;
            }
            _initialized = false;
            _totalSongCount = -1;
        }
    }
}
