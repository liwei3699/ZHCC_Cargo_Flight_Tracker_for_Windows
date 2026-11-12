using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ZhccCargoBoard.App.Dialogs;
using ZhccCargoBoard.App.ViewModels;

namespace ZhccCargoBoard.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Width = viewModel.Settings.WindowWidth;
        Height = viewModel.Settings.WindowHeight;
        if (viewModel.Settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    public MainViewModel ViewModel => _viewModel;

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAsync(manual: true);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ExportDialog(_viewModel) { Owner = this };
        dialog.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_viewModel.Settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.Settings.Save();
            _viewModel.ApplySettings();
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => _viewModel.ResetFilters();

    private void CloseDetail_Click(object sender, RoutedEventArgs e) => _viewModel.SelectedFlight = null;

    private void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        var key = e.Column.SortMemberPath;
        if (string.IsNullOrWhiteSpace(key)) return;

        e.Handled = true;
        _viewModel.SortBy(key);

        foreach (var column in FlightGrid.Columns)
        {
            column.SortDirection = null;
        }
        e.Column.SortDirection = _viewModel.SortDescending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            _viewModel.Settings.WindowMaximized = true;
        }
        else if (WindowState == WindowState.Normal)
        {
            _viewModel.Settings.WindowMaximized = false;
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!App.IsExiting && _viewModel.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            (Application.Current as App)?.NotifyMinimizedToTray();
            return;
        }

        SaveWindowPlacement();
    }

    public void SaveWindowPlacement()
    {
        if (WindowState == WindowState.Normal)
        {
            _viewModel.Settings.WindowWidth = Math.Max(MinWidth, Width);
            _viewModel.Settings.WindowHeight = Math.Max(MinHeight, Height);
        }
        _viewModel.Settings.WindowMaximized = WindowState == WindowState.Maximized;
        _viewModel.Settings.Save();
    }
}
