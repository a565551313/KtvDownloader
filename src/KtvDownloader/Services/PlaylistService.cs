using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KtvDownloader.Infrastructure.Logging;
using KtvDownloader.Models;
using KtvDownloader.Services.Interfaces;

namespace KtvDownloader.Services
{
    /// <summary>
    /// 歌单 / 榜单服务。查询 muse.db 的 playlists 与 playlist_songs 表。
    /// </summary>
    public sealed class PlaylistService : IPlaylistService
    {
        private readonly ILogger _logger;
        private readonly string _databasePath;

        public PlaylistService(ILogger logger, string databasePath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databasePath = databasePath;
        }

        private SQLiteConnection OpenConnection()
        {
            if (string.IsNullOrWhiteSpace(_databasePath) || !File.Exists(_databasePath))
                throw new FileNotFoundException("曲库文件不存在", _databasePath ?? string.Empty);

            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = _databasePath,
                ReadOnly = true,
                FailIfMissing = true,
                Pooling = true
            };

            var connection = new SQLiteConnection(builder.ToString());
            connection.Open();
            return connection;
        }

        public Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(
            int type,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run<IReadOnlyList<Playlist>>(() =>
            {
                var result = new List<Playlist>();

                try
                {
                    using (var connection = OpenConnection())
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
SELECT id, name, type, song_count, is_hq
  FROM playlists
 WHERE deleted_at IS NULL
   AND (@type = 0 OR type = @type)
 ORDER BY song_count DESC, hot_score DESC";

                        command.Parameters.AddWithValue("@type", type);

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                result.Add(new Playlist
                                {
                                    Id = GetString(reader, 0),
                                    Name = GetString(reader, 1),
                                    Type = GetInt(reader, 2),
                                    SongCount = GetInt(reader, 3),
                                    IsHighQuality = GetInt(reader, 4) != 0
                                });
                            }
                        }
                    }

                    _logger.Info("查询到 " + result.Count + " 个歌单 (type=" + type + ")");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error("查询歌单失败", ex);
                }

                return result;
            }, cancellationToken);
        }

        public Task<IReadOnlyList<Song>> GetPlaylistSongsAsync(
            string playlistId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run<IReadOnlyList<Song>>(() =>
            {
                var result = new List<Song>();
                if (string.IsNullOrWhiteSpace(playlistId)) return result;

                try
                {
                    using (var connection = OpenConnection())
                    using (var command = connection.CreateCommand())
                    {
                        // 通过 playlist_songs 关联 songs，保持歌单内的排序。
                        command.CommandText = @"
SELECT s.id, s.tid, s.name, s.name_trim, s.lang,
       s.hot_score, s.filename, s.accomp, s.mv_type, s.volume, s.tags_str,
       (SELECT group_concat(sg.name, '/')
          FROM song_singer_relations r
          JOIN singers sg ON sg.id = r.singer_id
         WHERE r.song_id = s.id) AS singer_names
  FROM playlist_songs ps
  JOIN songs s ON s.id = ps.song_id
 WHERE ps.playlist_id = @playlistId
   AND ps.deleted_at IS NULL
   AND s.deleted_at IS NULL
   AND s.filename IS NOT NULL
   AND s.filename <> ''
 ORDER BY ps.sort_no ASC";

                        command.Parameters.AddWithValue("@playlistId", playlistId);

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                var fileName = GetString(reader, 6);
                                result.Add(new Song
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
                                    Singer = GetString(reader, 11)
                                });
                            }
                        }
                    }

                    _logger.Info("歌单 " + playlistId + " 含 " + result.Count + " 首歌");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error("查询歌单曲目失败", ex);
                }

                return result;
            }, cancellationToken);
        }

        private static string[] SplitTags(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new string[0];

            var tags = new List<string>();
            var builder = new System.Text.StringBuilder();
            var inString = false;

            foreach (var c in raw)
            {
                if (c == '"')
                {
                    if (inString)
                    {
                        var tag = builder.ToString().Trim();
                        if (tag.Length > 0) tags.Add(tag);
                        builder.Length = 0;
                    }
                    inString = !inString;
                    continue;
                }

                if (inString) builder.Append(c);
            }

            return tags.ToArray();
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
            if (value is int) return (int)value;

            int parsed;
            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : 0;
        }
    }
}
