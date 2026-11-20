using System;
using KtvDownloader.Controls;
using KtvDownloader.Infrastructure.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KtvDownloader.Tests
{
    /// <summary>
    /// 手写 JSON 工具。
    /// <para>
    /// 项目里原本有**三份**各写一遍的 JSON 读取器，其中一份忘了做反转义，
    /// 于是接口返回的 <c>"url":"http:\/\/a\/b"</c> 会带着反斜杠直接拿去下载 → 失败。
    /// 现在统一到 MiniJson，这几条断言就是防止它再退化回去。
    /// </para>
    /// </summary>
    [TestClass]
    public class MiniJsonTests
    {
        [TestMethod]
        public void ReadString_必须反转义斜杠()
        {
            const string json = "{\"url\":\"http:\\/\\/cdn.example.com\\/a\\/b.ts\"}";

            Assert.AreEqual("http://cdn.example.com/a/b.ts", MiniJson.ReadString(json, "url"));
        }

        [TestMethod]
        public void ReadString_必须反转义引号与unicode()
        {
            const string json = "{\"t\":\"a\\\"b\\u0041\\/c\"}";

            Assert.AreEqual("a\"bA/c", MiniJson.ReadString(json, "t"));
        }

        [TestMethod]
        public void ReadString_键不存在返回null()
        {
            Assert.IsNull(MiniJson.ReadString("{\"a\":\"b\"}", "zzz"));
            Assert.IsNull(MiniJson.ReadString(null, "a"));
            Assert.IsNull(MiniJson.ReadString(string.Empty, "a"));
        }

        [TestMethod]
        public void ReadString_值不是字符串时返回null()
        {
            Assert.IsNull(MiniJson.ReadString("{\"n\":42}", "n"));
        }

        [TestMethod]
        public void ReadInt_读整数()
        {
            Assert.AreEqual(42, MiniJson.ReadInt("{\"n\":42}", "n"));
            Assert.AreEqual(-7, MiniJson.ReadInt("{\"n\":-7}", "n"));
        }

        [TestMethod]
        public void ReadInt_失败返回fallback()
        {
            Assert.AreEqual(-1, MiniJson.ReadInt("{\"n\":\"abc\"}", "n", -1));
            Assert.AreEqual(9, MiniJson.ReadInt("{}", "n", 9));
            Assert.AreEqual(9, MiniJson.ReadInt(null, "n", 9));
        }

        [TestMethod]
        public void ReadRaw_保留原始字面量()
        {
            Assert.AreEqual("\"b\"", MiniJson.ReadRaw("{\"a\":\"b\"}", "a"));
            Assert.AreEqual("true", MiniJson.ReadRaw("{\"a\":true}", "a"));
            Assert.AreEqual("null", MiniJson.ReadRaw("{\"a\":null}", "a"));
        }

        [TestMethod]
        public void ParseObject_扁平键值对()
        {
            var map = MiniJson.ParseObject("{\"A\":\"1\", \"b\":2, \"c\":true, \"d\":null}");

            Assert.IsNotNull(map);
            Assert.AreEqual("1", map["A"]);
            Assert.AreEqual("2", map["b"]);
            Assert.AreEqual("true", map["c"]);
            // null 统一成空串，调用方不用到处判 null。
            Assert.AreEqual(string.Empty, map["d"]);
        }

        [TestMethod]
        public void ParseObject_键大小写不敏感()
        {
            // 配置文件是人手写的，MaxConcurrency / maxconcurrency 都该认。
            var map = MiniJson.ParseObject("{\"MaxConcurrency\":3}");

            Assert.IsNotNull(map);
            Assert.AreEqual("3", map["maxconcurrency"]);
        }

        [TestMethod]
        public void ParseObject_非法输入返回null而不是抛异常()
        {
            Assert.IsNull(MiniJson.ParseObject("not json"));
            Assert.IsNull(MiniJson.ParseObject("{"));
            Assert.IsNull(MiniJson.ParseObject("{\"a\"}"));
            Assert.IsNull(MiniJson.ParseObject("{\"a\":}"));
            Assert.IsNull(MiniJson.ParseObject(null));
            Assert.IsNull(MiniJson.ParseObject("   "));
        }

        [TestMethod]
        public void ParseObject_值里带转义引号也能解析()
        {
            var map = MiniJson.ParseObject("{\"a\":\"x\\\"y\",\"b\":2}");

            Assert.IsNotNull(map);
            Assert.AreEqual("x\"y", map["a"]);
            Assert.AreEqual("2", map["b"]);
        }

        [TestMethod]
        public void ParseStringArray_曲库tags_str()
        {
            var tags = MiniJson.ParseStringArray("[\"高清歌曲\",\"流行歌曲\"]");

            Assert.AreEqual(2, tags.Length);
            Assert.AreEqual("高清歌曲", tags[0]);
            Assert.AreEqual("流行歌曲", tags[1]);
        }

        [TestMethod]
        public void ParseStringArray_空与非法输入返回空数组()
        {
            Assert.AreEqual(0, MiniJson.ParseStringArray("[]").Length);
            Assert.AreEqual(0, MiniJson.ParseStringArray(null).Length);
            Assert.AreEqual(0, MiniJson.ParseStringArray(string.Empty).Length);
            Assert.AreEqual(0, MiniJson.ParseStringArray("高清,流行").Length);
            Assert.AreEqual(1, MiniJson.ParseStringArray("[\"a\",]").Length);
        }

        [TestMethod]
        public void ParseStringArray_跳过空白项()
        {
            var tags = MiniJson.ParseStringArray("[\"a\", \"  \", \"b\"]");

            Assert.AreEqual(2, tags.Length);
        }

        [TestMethod]
        public void Quote_转义特殊字符()
        {
            Assert.AreEqual("\"a\\\"b\"", MiniJson.Quote("a\"b"));
            Assert.AreEqual("\"a\\\\b\"", MiniJson.Quote("a\\b"));
            Assert.AreEqual("\"a\\nb\"", MiniJson.Quote("a\nb"));
            Assert.AreEqual("null", MiniJson.Quote(null));
        }

        [TestMethod]
        public void Quote_斜杠不需要转义且往返一致()
        {
            // JSON 允许但不要求转义 '/'。这里不转义，往返必须还原。
            var quoted = MiniJson.Quote("http://a/b");

            Assert.AreEqual("\"http://a/b\"", quoted);

            var map = MiniJson.ParseObject("{\"u\":" + quoted + "}");
            Assert.AreEqual("http://a/b", map["u"]);
        }

        [TestMethod]
        public void Quote_中文原样保留()
        {
            Assert.AreEqual("\"发如雪\"", MiniJson.Quote("发如雪"));
        }

        [TestMethod]
        public void Unescape_未知转义宽容处理()
        {
            // 外部接口偶尔会吐出不规范的转义，不能因此整个响应解析失败。
            Assert.AreEqual("aXb", MiniJson.Unescape("a\\Xb"));
        }

        [TestMethod]
        public void Unescape_残缺的unicode宽容处理()
        {
            // \u 后面不足 4 位十六进制时，按普通字符处理，不能让整段解析崩掉。
            Assert.AreEqual("au12", MiniJson.Unescape("a\\u12"));
        }
    }

    /// <summary>分页计算（纯函数）。</summary>
    [TestClass]
    public class PaginationMathTests
    {
        [TestMethod]
        public void 总页数_向上取整()
        {
            Assert.AreEqual(1, PaginationMath.ComputeTotalPages(1, 50));
            Assert.AreEqual(1, PaginationMath.ComputeTotalPages(50, 50));
            Assert.AreEqual(2, PaginationMath.ComputeTotalPages(51, 50));
            Assert.AreEqual(3, PaginationMath.ComputeTotalPages(150, 50));
        }

        [TestMethod]
        public void 总页数_总数未知时返回1()
        {
            // 返回 1 才能让"下一页/尾页"保持禁用，
            // 而不是让用户跳到一个编出来的页码上。
            Assert.AreEqual(1, PaginationMath.ComputeTotalPages(0, 50));
            Assert.AreEqual(1, PaginationMath.ComputeTotalPages(-5, 50));
            Assert.AreEqual(1, PaginationMath.ComputeTotalPages(100, 0));
        }

        [TestMethod]
        public void 页码_夹在合法区间()
        {
            Assert.AreEqual(1, PaginationMath.ClampPage(0, 10));
            Assert.AreEqual(1, PaginationMath.ClampPage(-3, 10));
            Assert.AreEqual(10, PaginationMath.ClampPage(99, 10));
            Assert.AreEqual(5, PaginationMath.ClampPage(5, 10));
            Assert.AreEqual(1, PaginationMath.ClampPage(5, 0));
        }

        [TestMethod]
        public void 估算总数_满页说明后面还有()
        {
            Assert.AreEqual(100, PaginationMath.EstimateTotalCount(1, 50, 50));
            Assert.AreEqual(150, PaginationMath.EstimateTotalCount(2, 50, 50));
        }

        [TestMethod]
        public void 估算总数_不满页说明到底了()
        {
            Assert.AreEqual(30, PaginationMath.EstimateTotalCount(1, 50, 30));
            Assert.AreEqual(80, PaginationMath.EstimateTotalCount(2, 50, 30));
        }

        [TestMethod]
        public void 估算总数_每页条数非法时退回已加载数()
        {
            Assert.AreEqual(12, PaginationMath.EstimateTotalCount(1, 0, 12));
        }
    }
}
