using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ZhccCargoBoard.App.Services;
using ZhccCargoBoard.App.ViewModels;
using ZhccCargoBoard.Core;
using ZhccCargoBoard.Core.Storage;

namespace ZhccCargoBoard.App;

public partial class App : Application
{
    private const string MutexName = "Local\\ZhccCargoBoard.SingleInstance.1F3B7C";

    private Mutex? _mutex;
    private FlightStore? _store;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private TrayService? _tray;
    private bool _trayHintShown;

    public static bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("货机航班动态已经在运行了，请在系统托盘中找到它的图标。", "郑州机场货运航班动态",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        AppPaths.EnsureCreated();
        Log.Prune(14);
        Log.Info("==== 程序启动 ====");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("未处理的后台异常", args.ExceptionObject as Exception);

        var settings = AppSettings.Load();
        _store = new FlightStore();
        _store.Initialize();

        _viewModel = new MainViewModel(settings, _store);
        _mainWindow = new MainWindow(_viewModel);

        _tray = new TrayService(
            show: ShowMainWindow,
            refresh: () => _ = _viewModel.RefreshAsync(manual: true),
            export: ShowExportFromTray,
            exit: ExitApplication);
        _tray.UpdateTooltip("货机航班动态 · 每 " + settings.RefreshMinutes + " 分钟自动刷新");

        _mainWindow.Show();
        _viewModel.Initialize();

        if (settings.StartMinimized)
        {
            _mainWindow.Hide();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _tray?.Dispose();
            _viewModel?.Dispose();
            _store?.Dispose();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch
        {
            // 退出阶段忽略异常
        }

        Log.Info("==== 程序退出 ====");
        base.OnExit(e);
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        if (!_mainWindow.IsVisible) _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();
    }

    private void ShowExportFromTray()
    {
        if (_mainWindow is null || _viewModel is null) return;
        ShowMainWindow();
        new Dialogs.ExportDialog(_viewModel) { Owner = _mainWindow }.ShowDialog();
    }

    public void ExitApplication()
    {
        IsExiting = true;
        _mainWindow?.SaveWindowPlacement();
        Shutdown();
    }

    /// <summary>第一次收进托盘时给个气泡提示，避免用户以为程序被关掉了。</summary>
    public void NotifyMinimizedToTray()
    {
        if (_tray is null || _trayHintShown) return;
        _trayHintShown = true;

        if (!_tray.IsRegistered)
        {
            MessageBox.Show(
                "程序没有成功注册托盘图标，关闭窗口后将无法再从托盘打开。\n" +
                "请改用托盘图标启动，或在设置里取消“关闭主窗口时最小化到系统托盘”。",
                "货机航班动态", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _tray.ShowBalloon("程序已最小化到托盘", "仍在后台刷新。图标可能收在任务栏右下角的“^”里，双击即可重新打开。");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("界面线程异常", e.Exception);
        MessageBox.Show($"程序遇到一个未预期的问题：\n{e.Exception.Message}\n\n已记录到日志，可继续使用。",
            "货机航班动态", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
