using System;
using System.Windows.Forms;
using KtvDownloader.Forms;
using KtvDownloader.Infrastructure.Logging;

namespace KtvDownloader
{
    /// <summary>
    /// 程序入口。
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// 应用程序主入口点。
        /// </summary>
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 顶层异常兜底：任何未捕获异常都记日志并提示，避免直接闪退。
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;

            var logger = new FileLogger();
            logger.Info("==== KtvDownloader 启动，版本 " +
                        Infrastructure.Extensions.AssemblyInfoHelper.GetVersion() + " ====");
            logger.CleanupOldLogs();

            // 预览已改用 libVLC（原生支持 MPEG-TS，无需转码），
            // 不再依赖 WebBrowser 的 IE11 模式。
            // 保留 BrowserEmulation 类以备将来需要内嵌网页时使用。

            try
            {
                Application.Run(new MainForm(logger));
            }
            catch (Exception ex)
            {
                logger.Error("主窗体异常退出", ex);
                MessageBox.Show("程序异常退出：" + Environment.NewLine + ex.Message,
                    "KtvDownloader", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                logger.Info("==== KtvDownloader 退出 ====");
            }
        }

        private static void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            HandleFatal(e.Exception, "界面线程未处理异常");
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            HandleFatal(e.ExceptionObject as Exception, "后台线程未处理异常");
        }

        private static void HandleFatal(Exception exception, string title)
        {
            try
            {
                new FileLogger().Error(title, exception);
            }
            catch
            {
                // 日志失败时忽略，仍要弹提示。
            }

            MessageBox.Show(
                title + "：" + Environment.NewLine + (exception == null ? "未知错误" : exception.Message),
                "KtvDownloader", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
