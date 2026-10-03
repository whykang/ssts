using System.Threading;
using System.Windows;
using System.Windows.Threading;
using tingshu.Core;
using tingshu.Themes;

namespace tingshu;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, "TingShu.Windows.SingleInstance", out var isNew);
        if (!isNew)
        {
            MessageBox.Show("随身听书已经在运行了。", "随身听书", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Logger.Error("未处理的异常", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("未观察的任务异常", args.Exception);
            args.SetObserved();
        };

        Logger.Info($"启动，数据目录：{AppPaths.DataDir}");
        ThemeManager.Initialize();
        PluginManager.CleanupPendingDeletes();
        PluginManager.Instance.LoadAll();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("界面异常", e.Exception);
        ViewModels.MainViewModel.Instance.ShowToast("出错了：" + e.Exception.Message);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            PlayerService.Instance.Shutdown();
            LibraryStore.Instance.SaveNow();
            AppSettings.Current.SaveNow();
        }
        catch (Exception ex)
        {
            Logger.Error("退出时保存失败", ex);
        }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
