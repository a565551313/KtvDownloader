using System;
using System.Collections.Generic;

namespace KtvDownloader.Services
{
    /// <summary>
    /// 搜索关键词解析。
    /// <para>
    /// 支持「歌名 歌手」格式（中间一个空格），例如：
    /// <list type="bullet">
    /// <item><c>发如雪 周杰伦</c> → 歌名=发如雪, 歌手=周杰伦</item>
    /// <item><c>童话</c> → 歌名=童话, 歌手=空</item>
    /// <item><c>周杰伦 童话</c> → 顺序反过来也能识别（见 <see cref="TryParseReversed"/></item>
    /// </list>
    /// </para>
    /// <para>
    /// 难点：中文歌手名和歌名都可能有空格（如「陈奕迅 王菲」是两个人），
    /// 纯靠空格无法 100% 判断。策略是：
    /// 1. 从右往左尝试切分，优先认为后半部分是歌手名；
    /// 2. 用曲库校验——歌手名在 singers 表里存在才认可；
    /// 3. 都不匹配则整体当歌名搜。
    /// </para>
    /// </summary>
    public sealed class SearchQuery
    {
        /// <summary>歌名部分（可能为空）。</summary>
        public string SongName { get; private set; } = string.Empty;

        /// <summary>歌手部分（可能为空）。</summary>
        public string SingerName { get; private set; } = string.Empty;

        /// <summary>是否为「歌名 + 歌手」组合搜索。</summary>
        public bool IsCombined => SongName.Length > 0 && SingerName.Length > 0;

        /// <summary>原始输入。</summary>
        public string Raw { get; private set; } = string.Empty;

        /// <summary>
        /// 解析输入。不做数据库校验的简单版本，
        /// 只按「最后一个空格」切分。
        /// </summary>
        public static SearchQuery Parse(string input)
        {
            var query = new SearchQuery { Raw = (input ?? string.Empty).Trim() };
            var text = query.Raw;

            if (text.Length == 0) return query;

            // 全角空格统一成半角，避免用户输入法问题。
            text = text.Replace('\u3000', ' ').Trim();

            // 没有空格 → 全部当歌名。
            var spaceIndex = text.LastIndexOf(' ');
            if (spaceIndex <= 0 || spaceIndex >= text.Length - 1)
            {
                query.SongName = text;
                return query;
            }

            var left = text.Substring(0, spaceIndex).Trim();
            var right = text.Substring(spaceIndex + 1).Trim();

            if (left.Length == 0 || right.Length == 0)
            {
                query.SongName = text;
                return query;
            }

            // 默认：左边是歌名，右边是歌手。
            query.SongName = left;
            query.SingerName = right;
            return query;
        }

        /// <summary>
        /// 生成所有可能的切分方案，供调用方用曲库校验后择优。
        /// 按「歌手名在曲库中是否存在」筛选。
        /// </summary>
        /// <param name="input">用户输入。</param>
        /// <param name="singerExists">校验歌手名是否存在于曲库。</param>
        public static SearchQuery Resolve(string input, Func<string, bool> singerExists)
        {
            var trimmed = (input ?? string.Empty).Trim().Replace('\u3000', ' ');
            if (trimmed.Length == 0) return new SearchQuery { Raw = string.Empty };

            // 先按最后一个空格切。若歌手名在库里存在，直接采用。
            var simple = Parse(trimmed);
            if (!simple.IsCombined) return simple;

            if (singerExists != null && singerExists(simple.SingerName))
                return simple;

            // 最后一段不是歌手名，尝试从左往右找：
            // 例如「陈奕迅 王菲 因为爱情」→ 歌名=因为爱情, 歌手=陈奕迅 王菲
            var parts = SplitBySpace(trimmed);
            if (parts.Count < 2) return simple;

            // 尝试各种切分：前 k 段当歌手，后 n-k 段当歌名（以及反过来）。
            var candidates = new List<SearchQuery>();

            for (var k = 1; k < parts.Count; k++)
            {
                // 前 k 段是歌名，后面是歌手
                var songPart = string.Join(" ", parts.GetRange(0, k));
                var singerPart = string.Join(" ", parts.GetRange(k, parts.Count - k));
                candidates.Add(new SearchQuery
                {
                    Raw = trimmed,
                    SongName = songPart,
                    SingerName = singerPart
                });

                // 前 k 段是歌手，后面是歌名
                var singerPart2 = string.Join(" ", parts.GetRange(0, k));
                var songPart2 = string.Join(" ", parts.GetRange(k, parts.Count - k));
                candidates.Add(new SearchQuery
                {
                    Raw = trimmed,
                    SongName = songPart2,
                    SingerName = singerPart2
                });
            }

            // 优先选「歌手名能匹配上」的方案。
            if (singerExists != null)
            {
                foreach (var candidate in candidates)
                {
                    if (candidate.SingerName.Length > 0 && singerExists(candidate.SingerName))
                        return candidate;
                }
            }

            // 都不匹配，退回最简单的切分。
            return simple;
        }

        private static List<string> SplitBySpace(string text)
        {
            var parts = new List<string>();
            foreach (var part in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var value = part.Trim();
                if (value.Length > 0) parts.Add(value);
            }
            return parts;
        }

        public override string ToString()
        {
            if (IsCombined) return string.Format("{0} / {1}", SongName, SingerName);
            return SongName;
        }
    }
}
