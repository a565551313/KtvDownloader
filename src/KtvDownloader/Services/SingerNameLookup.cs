using System;
using System.Collections.Generic;
using System.Data.SQLite;
using KtvDownloader.Models;

namespace KtvDownloader.Services
{
    /// <summary>
    /// 批量补歌手名。
    /// <para>
    /// 为什么不用相关子查询：<c>(SELECT group_concat(...) WHERE r.song_id = s.id)</c>
    /// 每行都要跑一次关联查询，50 行就要 450ms（docs §坑7）。
    /// 改成"先取歌曲，再用一条 IN 查询批量补名"，同样 50 行只要 2ms。
    /// </para>
    /// <para>
    /// 为什么单独一个类：<c>SongSearchService</c> 与 <c>PlaylistService</c> 都需要它。
    /// 歌单查询原本还在用慢的相关子查询，一个上千首的歌单会卡很久。
    /// </para>
    /// </summary>
    internal static class SingerNameLookup
    {
        /// <summary>
        /// 单条 IN 查询里最多放多少个 id。
        /// <para>
        /// SQLite 编译期的 <c>SQLITE_MAX_VARIABLE_NUMBER</c> 老版本是 999，
        /// 每页条数可配到 500、后台预取一批也是 500，留出余量分块最稳妥。
        /// </para>
        /// </summary>
        private const int ChunkSize = 400;

        /// <summary>
        /// 就地填充 <see cref="Song.Singer"/>。查不到就保持原值。
        /// 调用方必须持有连接（本方法不管理连接生命周期）。
        /// </summary>
        public static void Fill(SQLiteConnection connection, IList<Song> songs)
        {
            if (connection == null || songs == null || songs.Count == 0) return;

            var ids = new List<string>(songs.Count);
            foreach (var song in songs)
            {
                if (!string.IsNullOrEmpty(song.Id) && !ids.Contains(song.Id)) ids.Add(song.Id);
            }

            if (ids.Count == 0) return;

            var map = new Dictionary<string, string>(ids.Count, StringComparer.Ordinal);

            for (var offset = 0; offset < ids.Count; offset += ChunkSize)
            {
                var count = Math.Min(ChunkSize, ids.Count - offset);
                FillChunk(connection, ids, offset, count, map);
            }

            foreach (var song in songs)
            {
                string names;
                if (song.Id != null && map.TryGetValue(song.Id, out names))
                    song.Singer = NormalizeSinger(names);
            }
        }

        private static void FillChunk(
            SQLiteConnection connection,
            List<string> ids,
            int offset,
            int count,
            Dictionary<string, string> map)
        {
            var placeholders = new string[count];
            for (var i = 0; i < count; i++) placeholders[i] = "?";

            var sql = @"
SELECT r.song_id, group_concat(sg.name, '/')
  FROM song_singer_relations r
  JOIN singers sg ON sg.id = r.singer_id
 WHERE r.song_id IN (" + string.Join(",", placeholders) + @")
   AND r.deleted_at IS NULL
 GROUP BY r.song_id";

            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;

                // SQLite 的 ? 是位置参数，按顺序追加即可。
                for (var i = 0; i < count; i++)
                {
                    var parameter = command.CreateParameter();
                    parameter.DbType = System.Data.DbType.String;
                    parameter.Value = ids[offset + i];
                    command.Parameters.Add(parameter);
                }

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;
                        map[reader.GetString(0)] = reader.GetString(1);
                    }
                }
            }
        }

        /// <summary>歌手名可能带空白或重复分隔符，统一成「A/B」形式。</summary>
        public static string NormalizeSinger(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var parts = raw.Split(new[] { '/', '、' }, StringSplitOptions.RemoveEmptyEntries);
            var cleaned = new List<string>(parts.Length);
            foreach (var part in parts)
            {
                var value = part.Trim();
                if (value.Length > 0 && !cleaned.Contains(value)) cleaned.Add(value);
            }
            return string.Join("/", cleaned.ToArray());
        }
    }
}
