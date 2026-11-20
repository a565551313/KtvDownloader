using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using KtvDownloader.Infrastructure.Logging;

namespace KtvDownloader.Services
{
    /// <summary>
    /// <see cref="IOverlayAssetResolver"/> 的真实实现：读图片尺寸、解析字体、分配文本临时文件。
    /// <para>
    /// 有副作用的部分全集中在这里，好让 <see cref="OverlayFilterBuilder"/> 保持纯函数、可测试。
    /// </para>
    /// <para>
    /// <b>关于这里的 <c>System.Drawing</c></b>：docs §3.3 的铁律禁的是 Services 层引用
    /// <c>System.Windows.Forms</c>（业务逻辑不该依赖窗体）。这里用 GDI+ 只做一件事 ——
    /// 读出图片的像素尺寸，不创建控件、不绘制、不碰任何 UI 对象，
    /// 和"读一个文件的长度"是同一类操作。手搓 PNG/JPEG/BMP/GIF 四种头解析
    /// 能去掉这个引用，但那 60 行代码的出错风险远大于收益。
    /// </para>
    /// </summary>
    public sealed class OverlayAssetResolver : IOverlayAssetResolver
    {
        private readonly FontFileResolver _fonts;
        private readonly string _tempDirectory;
        private readonly ILogger _logger;

        /// <summary>图片尺寸缓存（同一张水印在一次批量下载里会被问几百次）。</summary>
        private readonly Dictionary<string, int[]> _imageSizeCache =
            new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

        private readonly object _sync = new object();
        private int _textFileSequence;

        /// <param name="tempDirectory">文本临时文件的目录。留空用系统临时目录下的子目录。</param>
        public OverlayAssetResolver(FontFileResolver fonts = null, string tempDirectory = null, ILogger logger = null)
        {
            _fonts = fonts ?? new FontFileResolver(null, logger);
            _logger = logger;
            _tempDirectory = string.IsNullOrWhiteSpace(tempDirectory)
                ? Path.Combine(Path.GetTempPath(), "KtvDownloader-overlay")
                : tempDirectory;
        }

        /// <summary>字体解析器（界面要单独问字体警告时用）。</summary>
        public FontFileResolver Fonts => _fonts;

        /// <summary>文本临时文件目录。</summary>
        public string TempDirectory => _tempDirectory;

        public bool TryGetImageSize(string imagePath, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (string.IsNullOrWhiteSpace(imagePath)) return false;

            int[] cached;
            lock (_sync)
            {
                if (_imageSizeCache.TryGetValue(imagePath, out cached))
                {
                    if (cached == null) return false;
                    width = cached[0];
                    height = cached[1];
                    return true;
                }
            }

            try
            {
                if (!File.Exists(imagePath))
                {
                    lock (_sync) _imageSizeCache[imagePath] = null;
                    return false;
                }

                // ⚠️ 用 Image.FromFile 会把文件锁住直到 Dispose，
                //    而水印图正好是用户可能在别处编辑的文件 —— 锁着不放会招来
                //    "另一个程序正在使用此文件"。读进内存再解析，读完立刻放。
                var bytes = File.ReadAllBytes(imagePath);
                using (var stream = new MemoryStream(bytes))
                using (var image = Image.FromStream(stream, false, false))
                {
                    width = image.Width;
                    height = image.Height;
                }

                if (width > 0 && height > 0)
                {
                    lock (_sync) _imageSizeCache[imagePath] = new[] { width, height };
                    return true;
                }

                lock (_sync) _imageSizeCache[imagePath] = null;
                return false;
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Warn("读取图片尺寸失败: " + imagePath + " - " + ex.Message);
                lock (_sync) _imageSizeCache[imagePath] = null;
                return false;
            }
        }

        public bool TryResolveFont(string family, out string filePath, out string warning)
        {
            return _fonts.TryResolve(family, out filePath, out warning);
        }

        public string AllocateTextFilePath()
        {
            int sequence;
            lock (_sync)
            {
                _textFileSequence++;
                sequence = _textFileSequence;
            }

            try
            {
                if (!Directory.Exists(_tempDirectory)) Directory.CreateDirectory(_tempDirectory);
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.Warn("创建叠加临时目录失败，改用系统临时目录: " + ex.Message);
                return Path.Combine(Path.GetTempPath(),
                    "ktv-overlay-" + sequence.ToString(CultureInfo.InvariantCulture) + "-" +
                    Guid.NewGuid().ToString("N") + ".txt");
            }

            // 带 Guid：同一首歌的 SplitBoth 会并发跑两个 ffmpeg，
            // 只用序号的话两次可能撞在同一个文件名上，互相覆盖。
            return Path.Combine(_tempDirectory,
                "text-" + sequence.ToString(CultureInfo.InvariantCulture) + "-" +
                Guid.NewGuid().ToString("N") + ".txt");
        }

        /// <summary>
        /// 写入计划里的文本文件。
        /// <para>
        /// ⚠️ 必须 <b>UTF-8 无 BOM</b>：drawtext 会把 BOM 当成一个普通字符渲染出来，
        /// 于是每段文字的开头多一个方框（豆腐块）。这个 bug 只有看成品才发现。
        /// </para>
        /// </summary>
        public bool WriteTextFiles(IEnumerable<OverlayTextFile> files)
        {
            if (files == null) return true;

            foreach (var file in files)
            {
                if (file == null || string.IsNullOrEmpty(file.Path)) continue;

                try
                {
                    var directory = Path.GetDirectoryName(file.Path);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    // 内容里的换行已经在 OverlayFilterBuilder.NormalizeNewLines 统一成 LF 了，
                    // 这里用 File.WriteAllBytes 而不是 WriteAllText，
                    // 免得 StreamWriter 又把 \n 按环境换行符改写回 \r\n。
                    File.WriteAllBytes(file.Path,
                        new System.Text.UTF8Encoding(false).GetBytes(file.Content ?? string.Empty));
                }
                catch (Exception ex)
                {
                    if (_logger != null) _logger.Warn("写入叠加文本文件失败: " + file.Path + " - " + ex.Message);
                    return false;
                }
            }

            return true;
        }

        /// <summary>删掉计划里产生的文本临时文件。删不掉只记日志，不影响下载结果。</summary>
        public void CleanupTextFiles(IEnumerable<OverlayTextFile> files)
        {
            if (files == null) return;

            foreach (var file in files)
            {
                if (file == null || string.IsNullOrEmpty(file.Path)) continue;

                try
                {
                    if (File.Exists(file.Path)) File.Delete(file.Path);
                }
                catch (Exception ex)
                {
                    if (_logger != null) _logger.Warn("清理叠加临时文件失败: " + file.Path + " - " + ex.Message);
                }
            }
        }

        /// <summary>清空图片尺寸缓存。用户在编辑器里换了图片后调，否则拿到的还是旧尺寸。</summary>
        public void ClearImageCache()
        {
            lock (_sync)
            {
                _imageSizeCache.Clear();
            }
        }
    }
}
