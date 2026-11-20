using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using KtvDownloader.Forms;
using KtvDownloader.Infrastructure.Config;
using KtvDownloader.Infrastructure.Logging;

namespace KtvDownloader
{
    /// <summary>
    /// 程序入口。
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// 全局唯一的日志实例。
        /// <para>
        /// ⚠️ 必须是唯一一个。<see cref="FileLogger"/> 现在是缓冲写 + 常驻文件句柄，
        /// 早期版本在 <c>HandleFatal</c> 里 <c>new FileLogger()</c> 又开了一个，
        /// 两个实例抢同一个 .log 文件（FileShare 冲突）恰好会让"崩溃时的最后一条日志"写不进去。
        /// </para>
        /// </summary>
        private static FileLogger _logger;

        /// <summary>
        /// 应用程序主入口点。
        /// </summary>
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 日志要在异常兜底注册之前就位，否则启动阶段的异常无处可去。
            InitializeLogger();

            // 顶层异常兜底：任何未捕获异常都记日志并提示，避免直接闪退。
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;

            // 后台 Task 里没人 await 的异常：.NET 4.5 之后默认被静默吞掉，
            // 下载队列 / ffmpeg 那边一旦漏了个异常，界面上什么反应都没有，
            // 日志里也一片干净，只能靠猜。这里把它捞出来记进日志。
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            try
            {
                Application.Run(new MainForm(_logger));
            }
            catch (Exception ex)
            {
                _logger.Error("主窗体异常退出", ex);
                MessageBox.Show("程序异常退出：" + Environment.NewLine + ex.Message,
                    "KtvDownloader", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _logger.Info("==== KtvDownloader 退出 ====");

                // 缓冲写意味着最后几十行还在内存里，不刷盘就等于丢了。
                _logger.Flush();
                _logger.Dispose();
            }
        }

        private static void InitializeLogger()
        {
            LogLevel level = LogLevel.Debug;
            try
            {
                // 读配置只是为了拿日志级别；读失败（配置文件损坏等）就用默认值，
                // 这一步绝不能因为日志本身而起不来。
                var config = new ConfigStore().Load();
                level = LogLevelParser.Parse(config.LogLevel, LogLevel.Debug);
            }
            catch
            {
                level = LogLevel.Debug;
            }

            _logger = new FileLogger(null, 14, level);
            _logger.Info("==== KtvDownloader 启动，版本 " +
                         Infrastructure.Extensions.AssemblyInfoHelper.GetVersion() +
                         "，日志级别 " + level + " ====");
            _logger.CleanupOldLogs();
        }

        private static void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            // 界面线程：记日志 + 提示，然后**继续运行**。
            // Application.ThreadException 被 CatchException 模式接管后进程不会终止，
            // 让用户有机会把当前正在跑的任务处理完。
            HandleFatal(e.Exception, "界面线程未处理异常", false);
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            // AppDomain.UnhandledException 在 .NET 2.0+ 里，只要 IsTerminating 为 true，
            // CLR 就一定会结束进程 —— 这时候弹一个模态框只会卡住退出流程，
            // 而且用户看到的是"程序卡死了"而不是"程序崩了"。
            // 所以：终止性的只记日志并刷盘；非终止性的才提示。
            HandleFatal(e.ExceptionObject as Exception, "后台线程未处理异常", e.IsTerminating);
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            // 只记录，不提示：这类异常通常来自已经被上层 catch 掉的分支，
            // 弹窗会打断用户。SetObserved 表明"我们看见了"，避免将来策略变化时升级成崩溃。
            try
            {
                if (e.Exception != null)
                {
                    foreach (var inner in e.Exception.InnerExceptions)
                        _logger.Error("后台任务未观察异常", inner);
                }
                else
                {
                    _logger.Error("后台任务未观察异常", null);
                }
            }
            catch
            {
                // 兜底里的兜底。
            }

            e.SetObserved();
        }

        private static void HandleFatal(Exception exception, string title, bool terminating)
        {
            try
            {
                _logger.Error(title, exception);
                _logger.Flush();
            }
            catch
            {
                // 日志失败时忽略，仍要弹提示。
            }

            if (terminating) return;

            try
            {
                MessageBox.Show(
                    title + "：" + Environment.NewLine + (exception == null ? "未知错误" : exception.Message),
                    "KtvDownloader", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                // 界面已经不可用时连提示都弹不出来，别让兜底自己再抛一次。
            }
        }
    }
}
