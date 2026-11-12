using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ZhccCargoBoard.Core;

namespace ZhccCargoBoard.App.Dialogs;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        foreach (var minutes in new[] { 5, 10, 15, 20, 30, 45, 60, 90, 120 })
        {
            RefreshBox.Items.Add(minutes);
        }
        RefreshBox.Text = settings.RefreshMinutes.ToString();

        foreach (var days in new[] { 7, 15, 30, 60, 90, 180, 365 })
        {
            RetentionBox.Items.Add(days);
        }
        RetentionBox.Text = settings.RetentionDays.ToString();

        DateFormatBox.Items.Add(new ComboBoxItem { Content = "2026-11-05（软件内显示格式）", Tag = "yyyy-mm-dd" });
        DateFormatBox.Items.Add(new ComboBoxItem { Content = "11-05-26（与 example.xlsx 一致）", Tag = "mm-dd-yy" });
        DateFormatBox.SelectedIndex = string.Equals(settings.DateFormat, "mm-dd-yy", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        CloseToTrayBox.IsChecked = settings.CloseToTray;
        StartMinimizedBox.IsChecked = settings.StartMinimized;
        DataPathBox.Text = AppPaths.DataDirectory;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RefreshBox.Text.Trim(), out var minutes) || minutes < 1 || minutes > 1440)
        {
            MessageBox.Show(this, "自动刷新间隔需为 1–1440 之间的整数（分钟）。", "设置", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshBox.Focus();
            return;
        }

        if (!int.TryParse(RetentionBox.Text.Trim(), out var days) || days < 1 || days > 3650)
        {
            MessageBox.Show(this, "数据保留天数需为 1–3650 之间的整数。", "设置", MessageBoxButton.OK, MessageBoxImage.Information);
            RetentionBox.Focus();
            return;
        }

        _settings.RefreshMinutes = minutes;
        _settings.RetentionDays = days;
        _settings.DateFormat = (DateFormatBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "yyyy-mm-dd";
        _settings.CloseToTray = CloseToTrayBox.IsChecked == true;
        _settings.StartMinimized = StartMinimizedBox.IsChecked == true;
        _settings.Save();

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.DataDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"无法打开数据目录：{ex.Message}", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
