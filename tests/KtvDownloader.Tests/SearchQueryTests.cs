using System;
using System.Collections.Generic;
using KtvDownloader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KtvDownloader.Tests
{
    /// <summary>
    /// 「歌名 歌手」关键词解析。
    /// <para>
    /// 每一次 <c>singerExists</c> 校验都是一趟 SQLite 查询，而且在主窗体里是同步阻塞的，
    /// 所以候选枚举**必须去重** —— 这几条断言同时钉住正确性和查询次数。
    /// </para>
    /// </summary>
    [TestClass]
    public class SearchQueryTests
    {
        [TestMethod]
        public void Parse_没有空格时整体当歌名()
        {
            var query = SearchQuery.Parse("童话");

            Assert.AreEqual("童话", query.SongName);
            Assert.AreEqual(string.Empty, query.SingerName);
            Assert.IsFalse(query.IsCombined);
        }

        [TestMethod]
        public void Parse_按最后一个空格切分()
        {
            var query = SearchQuery.Parse("发如雪 周杰伦");

            Assert.AreEqual("发如雪", query.SongName);
            Assert.AreEqual("周杰伦", query.SingerName);
            Assert.IsTrue(query.IsCombined);
        }

        [TestMethod]
        public void Parse_全角空格等同半角()
        {
            // 中文输入法很容易打出全角空格，不处理的话整串会被当成歌名。
            var query = SearchQuery.Parse("发如雪\u3000周杰伦");

            Assert.AreEqual("发如雪", query.SongName);
            Assert.AreEqual("周杰伦", query.SingerName);
        }

        [TestMethod]
        public void Parse_多余空白被吃掉()
        {
            var query = SearchQuery.Parse("  发如雪   周杰伦  ");

            Assert.AreEqual("发如雪", query.SongName);
            Assert.AreEqual("周杰伦", query.SingerName);
        }

        [TestMethod]
        public void Parse_空输入()
        {
            Assert.IsFalse(SearchQuery.Parse(null).IsCombined);
            Assert.IsFalse(SearchQuery.Parse("   ").IsCombined);
            Assert.AreEqual(string.Empty, SearchQuery.Parse(null).Raw);
        }

        [TestMethod]
        public void Resolve_最后一段就是歌手时直接采用()
        {
            var query = SearchQuery.Resolve("发如雪 周杰伦", name => name == "周杰伦");

            Assert.AreEqual("发如雪", query.SongName);
            Assert.AreEqual("周杰伦", query.SingerName);
        }

        [TestMethod]
        public void Resolve_用户把顺序写反了也能纠正()
        {
            // 「周杰伦 发如雪」：按最后一个空格切会得到 歌手=发如雪（曲库里没有），
            // 这时必须尝试反过来切。
            var query = SearchQuery.Resolve("周杰伦 发如雪", name => name == "周杰伦");

            Assert.IsTrue(query.IsCombined);
            Assert.AreEqual("周杰伦", query.SingerName);
            Assert.AreEqual("发如雪", query.SongName);
        }

        [TestMethod]
        public void Resolve_多段歌手名()
        {
            // 「陈奕迅 王菲 因为爱情」→ 歌手是两个人。
            var singers = new HashSet<string>(StringComparer.Ordinal) { "陈奕迅 王菲" };
            var query = SearchQuery.Resolve("陈奕迅 王菲 因为爱情", singers.Contains);

            Assert.IsTrue(query.IsCombined);
            Assert.AreEqual("陈奕迅 王菲", query.SingerName);
            Assert.AreEqual("因为爱情", query.SongName);
        }

        [TestMethod]
        public void Resolve_谁都匹配不上时退回最简切分()
        {
            var query = SearchQuery.Resolve("随便 什么 东西", name => false);

            Assert.AreEqual("随便 什么", query.SongName);
            Assert.AreEqual("东西", query.SingerName);
        }

        [TestMethod]
        public void Resolve_没有校验函数时不做多余尝试()
        {
            var query = SearchQuery.Resolve("发如雪 周杰伦", null);

            Assert.AreEqual("发如雪", query.SongName);
            Assert.AreEqual("周杰伦", query.SingerName);
        }

        [TestMethod]
        public void Resolve_单个词时不查库()
        {
            var calls = 0;
            var query = SearchQuery.Resolve("童话", name => { calls++; return true; });

            Assert.IsFalse(query.IsCombined);
            Assert.AreEqual(0, calls, "没有空格就没有候选可校验");
        }

        [TestMethod]
        public void Resolve_候选去重_重复组合不重复查库()
        {
            var asked = new List<string>();

            SearchQuery.Resolve("A B C D", name =>
            {
                asked.Add(name);
                return false;
            });

            var unique = new HashSet<string>(asked);
            Assert.AreEqual(unique.Count, asked.Count,
                "同一个歌手名不该被查询两次（每次都阻塞 UI 线程）：" + string.Join(",", asked.ToArray()));
        }

        [TestMethod]
        public void Resolve_校验次数有上限()
        {
            var calls = 0;

            SearchQuery.Resolve("A B C D E", name => { calls++; return false; });

            // 5 段最多 4 个切点 × 2 种方向 = 8 个候选，去重后更少；再加最简切分那一次。
            Assert.IsTrue(calls <= 9, "实际查询了 " + calls + " 次");
        }

        [TestMethod]
        public void ToString_组合时同时给出两部分()
        {
            var query = SearchQuery.Parse("发如雪 周杰伦");

            Assert.AreEqual("发如雪 / 周杰伦", query.ToString());
            Assert.AreEqual("童话", SearchQuery.Parse("童话").ToString());
        }
    }
}
