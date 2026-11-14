using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ZhccCargoBoard.Desktop.ViewModels;

namespace ZhccCargoBoard.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) await vm.RefreshAsync();
    }

    private void OnResetClick(object? sender, RoutedEventArgs e) => Vm?.ResetFilters();

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm?.SelectedFlight is not { } flight) return;
        new DetailWindow { DataContext = flight }.ShowDialog(this);
    }
}
