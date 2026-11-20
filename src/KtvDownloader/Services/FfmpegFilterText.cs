using System;
using System.Globalization;
using System.Text;

namespace KtvDownloader.Services
{
    /// <summary>
    /// ffmpeg 滤镜图的字符串引用与转义。<b>纯函数</b>，可单元测试。
    /// <para>
    /// 为什么单独一个类：滤镜参数要穿过<b>两层</b>解析器，转义规则各不相同，
    /// 写错的后果从"文字显示成乱码"到"整条命令失败"都有，而且只有真跑一遍才看得见。
    /// 这类错误和当年 pan 滤镜把同一个声道赋两次（产物右声道恒静音）是同一类 ——
    /// 靠人眼审是审不出来的，只能靠断言钉死。
    /// </para>
    /// <para>两层解析器：</para>
    /// <list type="number">
    /// <item>
    /// <b>Windows 命令行（MSVCRT 规则）</b>：整个 <c>-filter_complex</c> 的值由
    /// <see cref="FfmpegArguments.Quote"/> 用双引号包住，单引号在这一层是普通字符。
    /// </item>
    /// <item>
    /// <b>ffmpeg 的 filtergraph 语法</b>：<c>,</c> 分隔滤镜、<c>;</c> 分隔滤镜链、
    /// <c>:</c> 分隔参数、<c>[</c><c>]</c> 是流标签、<c>=</c> 是赋值、<c>\</c> 是转义符。
    /// 用 <c>'…'</c> 包住一段值可以保护里面所有这些字符 —— <b>但保护不了单引号自己</b>。
    /// </item>
    /// </list>
    /// <para>
    /// <b>文本内容刻意不走转义。</b> <c>drawtext</c> 的 <c>text=</c> 选项里，
    /// <c>:</c> <c>'</c> <c>%</c> <c>\</c> 都要转义，叠加上面两层就是四重转义，
    /// 一句 <c>100%的爱:献给"你"</c> 足够写出 bug。这里改用
    /// <c>textfile='路径':expansion=none</c> —— 文本落在临时文件里由 ffmpeg 自己读，
    /// <c>expansion=none</c> 关掉 <c>%{...}</c> 变量展开，于是<b>文本内容完全不需要转义</b>，
    /// 只有那个文件路径要过一遍滤镜引用。少一层转义就少一类 bug。
    /// </para>
    /// </summary>
    public static class FfmpegFilterText
    {
        /// <summary>
        /// 把 Windows 路径转成滤镜参数里安全的形式：反斜杠换成正斜杠。
        /// <para>
        /// ffmpeg 在 Windows 上完全接受正斜杠。反过来如果保留 <c>\</c>，
        /// 它在滤镜语法里是转义符，<c>C:\temp\a.png</c> 里的 <c>\t</c> 会被吃掉，
        /// 得写成 <c>C\\:/temp/a.png</c> 这种鬼样子 —— 能少一层就少一层。
        /// </para>
        /// </summary>
        public static string ToFilterPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return path.Replace('\\', '/');
        }

        /// <summary>
        /// 用单引号包住一个滤镜参数值，保护里面的 <c>,</c> <c>:</c> <c>[</c> <c>]</c> <c>;</c> <c>=</c>。
        /// <para>
        /// 值里本身含单引号时，按 ffmpeg 文档的办法处理：闭合引号 → 转义的单引号 → 重新开引号，
        /// 即 <c>'foo'\''bar'</c>。直接写 <c>'foo'bar'</c> 会解析错位。
        /// </para>
        /// </summary>
        public static string QuoteFilterValue(string value)
        {
            if (value == null) return "''";

            if (value.IndexOf('\'') < 0) return "'" + value + "'";

            var builder = new StringBuilder(value.Length + 8);
            var start = 0;

            while (true)
            {
                var quote = value.IndexOf('\'', start);
                if (quote < 0)
                {
                    builder.Append('\'').Append(value, start, value.Length - start).Append('\'');
                    break;
                }

                builder.Append('\'').Append(value, start, quote - start).Append('\'');
                builder.Append("\\'");
                start = quote + 1;
            }

            return builder.ToString();
        }

        /// <summary>
        /// 拼一个<b>带引用</b>的滤镜参数：<c>key='value'</c>。
        /// 用于路径、表达式（<c>enable</c>）这类含特殊字符的值。
        /// </summary>
        public static string QuotedOption(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("key 为空", nameof(key));
            return key + "=" + QuoteFilterValue(value ?? string.Empty);
        }

        /// <summary>
        /// 拼一个<b>不带引用</b>的滤镜参数：<c>key=value</c>。
        /// 用于纯数字和已知的安全标识符（<c>x=40</c>、<c>fontsize=48</c>）。
        /// </summary>
        public static string Option(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("key 为空", nameof(key));
            return key + "=" + (value ?? string.Empty);
        }

        /// <summary>拼一个整数参数（InvariantCulture，避免区域设置把小数点变成逗号）。</summary>
        public static string Option(string key, int value)
        {
            return Option(key, value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>拼一个小数参数（InvariantCulture）。</summary>
        public static string Option(string key, double value)
        {
            return Option(key, value.ToString("0.###", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// <c>#RRGGBB</c> → ffmpeg 的 <c>0xRRGGBB</c>。
        /// <para>
        /// 两种写法 ffmpeg 都认，选 <c>0x</c> 是因为它不含 <c>#</c>，
        /// 不用担心某些 shell / 脚本环境把 <c>#</c> 当注释起始。
        /// 输入非法时回落到 <paramref name="fallback"/>，不抛异常 ——
        /// 颜色写错不该让整次下载失败。
        /// </para>
        /// </summary>
        public static string Color(string htmlColor, string fallback = "#FFFFFF")
        {
            var text = (htmlColor ?? string.Empty).Trim();
            if (text.StartsWith("#", StringComparison.Ordinal)) text = text.Substring(1);

            if (text.Length != 6)
            {
                var fb = (fallback ?? "#FFFFFF").Trim();
                if (fb.StartsWith("#", StringComparison.Ordinal)) fb = fb.Substring(1);
                text = fb.Length == 6 ? fb : "FFFFFF";
            }

            return "0x" + text.ToUpperInvariant();
        }

        /// <summary>
        /// 判断一个编码器名是不是硬件编码器（影响 preset/crf 的传法）。
        /// <para>硬编不认 <c>-preset veryfast</c> 那一套（nvenc 有自己的 p1–p7），
        /// 也不认 <c>-crf</c>（要用 <c>-cq</c>/<c>-qp</c>），传错了 ffmpeg 直接失败。</para>
        /// </summary>
        public static bool IsHardwareEncoder(string encoderName)
        {
            var text = (encoderName ?? string.Empty).Trim().ToLowerInvariant();

            return text.EndsWith("_nvenc", StringComparison.Ordinal)
                   || text.EndsWith("_qsv", StringComparison.Ordinal)
                   || text.EndsWith("_amf", StringComparison.Ordinal)
                   || text.EndsWith("_mf", StringComparison.Ordinal)
                   || text.EndsWith("_videotoolbox", StringComparison.Ordinal);
        }
    }
}
