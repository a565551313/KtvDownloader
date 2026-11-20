using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KtvDownloader.Infrastructure.Config
{
    /// <summary>
    /// 极简 JSON 工具。只支持扁平对象（值为字符串/数字/布尔），
    /// 足够读写配置文件，避免为了几行配置引入 JSON 库。
    /// </summary>
    internal static class MiniJson
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

                var key = ReadString(json, ref index);
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

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static string ReadString(string s, ref int i)
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
                        if (!int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
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

            if (s[i] == '"') return ReadString(s, ref i);

            var start = i;
            while (i < s.Length && s[i] != ',' && s[i] != '}' && !char.IsWhiteSpace(s[i])) i++;
            if (i == start) return null;

            var raw = s.Substring(start, i - start);
            if (raw == "null") return string.Empty;
            return raw;
        }
    }
}
