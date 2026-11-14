using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ZhccCargoBoard.Core;
using ZhccCargoBoard.Core.Storage;
using ZhccCargoBoard.Desktop.ViewModels;
using ZhccCargoBoard.Desktop.Views;

namespace ZhccCargoBoard.Desktop;

public sealed class App : Application
{
    private FlightStore? _store;
    private MainViewModel? _viewModel;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppPaths.EnsureCreated();
            Log.Info("==== 程序启动（Linux 查看器）====");

            var settings = AppSettings.Load();
            _store = new FlightStore();
            _store.Initialize();

            _viewModel = new MainViewModel(settings, _store);
            var window = new MainWindow { DataContext = _viewModel };

            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.Exit += (_, _) =>
            {
                _viewModel?.Dispose();
                _store?.Dispose();
                Log.Info("==== 程序退出 ====");
            };

            _viewModel.Initialize();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
