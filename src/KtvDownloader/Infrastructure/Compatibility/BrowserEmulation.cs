using System;
using System.IO;
using Microsoft.Win32;

namespace KtvDownloader.Infrastructure.Compatibility
{
    /// <summary>
    /// 让 WinForms 的 <see cref="System.Windows.Forms.WebBrowser"/> 以 IE11 模式运行。
    /// <para>
    /// ⚠️ **当前没有任何调用者**，是刻意保留的。
    /// 预览早已改用 libVLC（原生支持 MPEG-TS + mp2，不需要 HTML5 &lt;video&gt;），
    /// 所以这条路径走不到了。留着是因为：将来若要内嵌任何网页
    /// （歌词、公告、扫码登录…），net48 的 WebBrowser 默认跑在 IE7 兼容模式，
    /// 忘了这一步就会得到一片黑屏，而且没有任何报错。
    /// </para>
    /// <para>
    /// net48 的 WebBrowser 默认跑在 IE7 兼容模式，不支持 HTML5 &lt;video&gt;，
    /// 预览播放会直接黑屏。必须在注册表里为当前 exe 声明浏览器模拟版本。
    /// </para>
    /// <para>
    /// 写入位置：HKCU\Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION
    /// 值名 = exe 文件名，值 = 11001（IE11 边缘模式，忽略 DOCTYPE 强制标准）
    /// </para>
    /// </summary>
    public static class BrowserEmulation
    {
        private const string RegistryPath =
            @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION";

        /// <summary>IE11 边缘模式。</summary>
        private const int Ie11EdgeMode = 11001;

        /// <summary>
        /// 为当前进程注册 IE11 模式。应在创建任何 WebBrowser 之前调用。
        /// 返回是否设置成功（失败不致命，只是播放器可能不可用）。
        /// </summary>
        public static bool EnsureModernMode()
        {
            try
            {
                var exeName = Path.GetFileName(Environment.GetCommandLineArgs()[0]);
                if (string.IsNullOrEmpty(exeName)) return false;

                using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key == null) return false;

                    var existing = key.GetValue(exeName);
                    if (existing is int && (int)existing == Ie11EdgeMode) return true;

                    key.SetValue(exeName, Ie11EdgeMode, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch
            {
                // 注册表不可写（受限账户）时静默失败，播放器会提示不支持。
                return false;
            }
        }
    }
}
