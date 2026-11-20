using System;
using System.Globalization;

namespace KtvDownloader.Services
{
    /// <summary>
    /// 解析 ffmpeg <c>-progress pipe:1</c> 的输出，换算成 0–100 的百分比。
    /// <para>
    /// 为什么单独一个类：烧录叠加层要整首重编码，一首歌一两分钟，
    /// 没有进度上报的话队列项会长时间停在"正在转码"一动不动，看着像卡死。
    /// 而解析逻辑里有几个只有踩过才知道的点，值得单独钉死测试。
    /// </para>
    /// <para>三个坑：</para>
    /// <list type="number">
    /// <item>
    /// <b><c>out_time_ms</c> 的单位其实是微秒</b>，不是毫秒。这是 ffmpeg 的历史遗留命名，
    /// 新版另外输出了一个名副其实的 <c>out_time_us</c>。两个都认，别只信字段名 ——
    /// 按毫秒算的话进度会快 1000 倍，刚开头就报 100%。
    /// </item>
    /// <item>
    /// <b>ffmpeg 每帧吐一整块 key=value</b>，一秒能来几十行。逐行上报会把 UI 消息队列淹掉
    /// （WinForms 的 BeginInvoke 排队排到界面卡住），所以整数百分比没变就不上报。
    /// </item>
    /// <item>总时长未知时不要瞎算，直接不上报，好过报一个乱跳的数字。</item>
    /// </list>
    /// </summary>
    public sealed class FfmpegProgressParser
    {
        private readonly double _totalSeconds;
        private double _lastPercent = -1;

        /// <param name="totalSeconds">总时长（秒）。&lt;=0 表示未知，此时不产生任何进度。</param>
        public FfmpegProgressParser(double totalSeconds)
        {
            _totalSeconds = totalSeconds;
        }

        /// <summary>总时长（秒）。</summary>
        public double TotalSeconds => _totalSeconds;

        /// <summary>
        /// 喂一行输出。有值得上报的百分比时返回 true 并给出 <paramref name="percent"/>。
        /// <para>线程安全性：ffmpeg 的输出泵线程会调它，但同一时刻只有一个泵在跑，
        /// 所以内部那点状态不加锁也没问题 —— 别在多个进程间共享同一个实例。</para>
        /// </summary>
        public bool TryParse(string line, out double percent)
        {
            percent = 0;

            if (_totalSeconds <= 0 || string.IsNullOrEmpty(line)) return false;

            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1) return false;

            var key = line.Substring(0, separator).Trim();
            if (key != "out_time_us" && key != "out_time_ms") return false;

            long microseconds;
            if (!long.TryParse(line.Substring(separator + 1).Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out microseconds)) return false;

            if (microseconds < 0) return false;

            var elapsed = microseconds / 1000000.0;
            var value = Math.Max(0, Math.Min(100, elapsed / _totalSeconds * 100));

            // 整数百分比没变就不上报（100% 例外，那是收尾信号，必须发一次）。
            if (value < 100 && Math.Abs(value - _lastPercent) < 1.0) return false;

            _lastPercent = value;
            percent = value;
            return true;
        }
    }
}
