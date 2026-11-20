using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KtvDownloader.Infrastructure.Config
{
    /// <summary>
    /// 极简 JSON 工具（零第三方依赖）。
    /// <para>
    /// 两类用途：
    /// 1. 读写扁平配置对象（<see cref="ParseObject"/> / <see cref="Quote"/>）；
    /// 2. 从外部接口响应里抠出某个标量字段（<see cref="ReadString"/> / <see cref="ReadInt"/> /
    ///    <see cref="ReadRaw"/>）—— 这些响应可能不是严格 JSON（前面混着 PHP 的 HTML 警告），
    ///    所以用"找 key → 读值"的宽容扫描而不是完整解析。
    /// </para>
    /// <para>
    /// 历史上项目里有**三份**各写一遍的 JSON 读取器（这里、SongUrlService、
    /// DatabaseUpdateService），行为并不一致：其中一份忘记做反转义，
    /// 于是 <c>"url":"http:\/\/..."</c> 会带着反斜杠直接拿去下载而失败。
    /// 现在统一到这一份，并有单元测试盯着转义行为。
    /// </para>
    /// </summary>
    public static class MiniJson
    {
        /// <summary>把字符串转义为 JSON 字符串字面量（含引号）。</summary>
        public static string Quote(string value)
        {
            if (value == null) return "null";

            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }

        /// <summary>
        /// 解析扁平 JSON 对象为键值字典。所有值统一转成字符串。
        /// 解析失败返回 null。
        /// </summary>
        public static Dictionary<string, string> ParseObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var index = 0;

            SkipWhitespace(json, ref index);
            if (index >= json.Length || json[index] != '{') return null;
            index++;

            while (true)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length) return null;
                if (json[index] == '}') { index++; break; }

                var key = ReadStringLiteral(json, ref index);
                if (key == null) return null;

                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] != ':') return null;
                index++;

                SkipWhitespace(json, ref index);
                var value = ReadValue(json, ref index);
                if (value == null) return null;

                result[key] = value;

                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') { index++; continue; }
                if (index < json.Length && json[index] == '}') { index++; break; }
                return null;
            }

            return result;
        }

        /// <summary>
        /// 从（可能不严格的）JSON 文本里读出字符串字段的值，**会做反转义**。
        /// 找不到、值不是字符串时返回 null。
        /// </summary>
        public static string ReadString(string json, string key)
        {
            var raw = ReadRaw(json, key);
            if (raw == null) return null;
            if (raw.Length < 2 || raw[0] != '"' || raw[raw.Length - 1] != '"') return null;

            return Unescape(raw.Substring(1, raw.Length - 2));
        }

        /// <summary>读出数字字段，失败返回 fallback。</summary>
        public static int ReadInt(string json, string key, int fallback = 0)
        {
            var raw = ReadRaw(json, key);
            if (string.IsNullOrEmpty(raw)) return fallback;

            var i = 0;
            if (raw[i] == '-' || raw[i] == '+') i++;
            var start = i;
            while (i < raw.Length && char.IsDigit(raw[i])) i++;
            if (i == start) return fallback;

            int value;
            return int.TryParse(raw.Substring(0, i), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        /// <summary>
        /// 读出字段的原始文本（字符串含引号，数字/布尔/null 原样）。
        /// 找不到返回 null。
        /// </summary>
        public static string ReadRaw(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;

            var pattern = "\"" + key + "\"";
            var index = json.IndexOf(pattern, StringComparison.Ordinal);
            if (index < 0) return null;

            var colon = json.IndexOf(':', index + pattern.Length);
            if (colon < 0) return null;

            var i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length) return null;

            if (json[i] == '"')
            {
                var start = i;
                i++;
                while (i < json.Length)
                {
                    if (json[i] == '\\') { i += 2; continue; }
                    if (json[i] == '"') { i++; break; }
                    i++;
                }
                return json.Substring(start, Math.Min(i, json.Length) - start);
            }

            var valueStart = i;
            while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']') i++;
            return json.Substring(valueStart, i - valueStart).Trim();
        }

        /// <summary>
        /// 解析字符串数组，形如 <c>["高清歌曲","流行歌曲"]</c>。
        /// 曲库的 <c>tags_str</c> 字段就是这个形态。失败返回空数组。
        /// </summary>
        public static string[] ParseStringArray(string json)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(json)) return result.ToArray();

            var i = json.IndexOf('[');
            if (i < 0) return result.ToArray();
            i++;

            while (i < json.Length)
            {
                while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ',')) i++;
                if (i >= json.Length || json[i] == ']') break;
                if (json[i] != '"') break;

                var value = ReadStringLiteral(json, ref i);
                if (value == null) break;

                var trimmed = value.Trim();
                if (trimmed.Length > 0) result.Add(trimmed);
            }

            return result.ToArray();
        }

        /// <summary>反转义 JSON 字符串正文（不含首尾引号）。</summary>
        public static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0) return value;

            var builder = new StringBuilder(value.Length);
            var i = 0;
            while (i < value.Length)
            {
                var c = value[i++];
                if (c != '\\' || i >= value.Length)
                {
                    builder.Append(c);
                    continue;
                }

                var esc = value[i++];
                switch (esc)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (i + 4 <= value.Length)
                        {
                            int code;
                            if (int.TryParse(value.Substring(i, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out code))
                            {
                                builder.Append((char)code);
                                i += 4;
                                continue;
                            }
                        }
                        builder.Append(esc);
                        break;
                    default:
                        // 未知转义：保留原字符，宽容处理外部接口的不规范输出。
                        builder.Append(esc);
                        break;
                }
            }

            return builder.ToString();
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        /// <summary>从 i 位置读一个带引号的字符串字面量，i 前进到字面量之后。</summary>
        private static string ReadStringLiteral(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') return null;
            i++;
            var builder = new StringBuilder();
            while (i < s.Length)
            {
                var c = s[i++];
                if (c == '"') return builder.ToString();
                if (c != '\\') { builder.Append(c); continue; }
                if (i >= s.Length) return null;

                var esc = s[i++];
                switch (esc)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) return null;
                        int code;
                        if (!int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out code))
                            return null;
                        builder.Append((char)code);
                        i += 4;
                        break;
                    default: return null;
                }
            }
            return null;
        }

        private static string ReadValue(string s, ref int i)
        {
            if (i >= s.Length) return null;

            if (s[i] == '"') return ReadStringLiteral(s, ref i);

            var start = i;
            while (i < s.Length && s[i] != ',' && s[i] != '}' && !char.IsWhiteSpace(s[i])) i++;
            if (i == start) return null;

            var raw = s.Substring(start, i - start);
            if (raw == "null") return string.Empty;
            return raw;
        }
    }
}
