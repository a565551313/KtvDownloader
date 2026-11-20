using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KtvDownloader.Infrastructure.Config
{
    /// <summary>
    /// 收藏存储。把歌号列表保存在 %AppData%\KtvDownloader\favorites.txt，
    /// 一行一个歌号，便于用户手工编辑。
    /// </summary>
    public sealed class FavoriteStore
    {
        private readonly string _filePath;
        private readonly object _sync = new object();

        public FavoriteStore(string filePath = null)
        {
            _filePath = filePath ?? Path.Combine(Models.AppConfig.AppDataDirectory, "favorites.txt");
        }

        /// <summary>读取全部收藏歌号。</summary>
        public List<string> Load()
        {
            var result = new List<string>();

            lock (_sync)
            {
                try
                {
                    if (!File.Exists(_filePath)) return result;

                    foreach (var line in File.ReadAllLines(_filePath, Encoding.UTF8))
                    {
                        var value = line.Trim();
                        if (value.Length > 0 && !result.Contains(value)) result.Add(value);
                    }
                }
                catch
                {
                    // 读取失败按空收藏处理。
                }
            }

            return result;
        }

        /// <summary>添加收藏。已存在则忽略。</summary>
        public bool Add(string musicNo)
        {
            if (string.IsNullOrWhiteSpace(musicNo)) return false;

            lock (_sync)
            {
                try
                {
                    var list = Load();
                    if (list.Contains(musicNo)) return false;

                    list.Add(musicNo);
                    Save(list);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>移除收藏。</summary>
        public bool Remove(string musicNo)
        {
            if (string.IsNullOrWhiteSpace(musicNo)) return false;

            lock (_sync)
            {
                try
                {
                    var list = Load();
                    if (!list.Remove(musicNo)) return false;

                    Save(list);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>判断是否已收藏。</summary>
        public bool Contains(string musicNo)
        {
            if (string.IsNullOrWhiteSpace(musicNo)) return false;
            return Load().Contains(musicNo);
        }

        private void Save(List<string> items)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllLines(_filePath, items, new UTF8Encoding(false));
        }
    }
}
