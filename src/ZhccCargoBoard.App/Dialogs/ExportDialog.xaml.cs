using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ZhccCargoBoard.App.ViewModels;
using ZhccCargoBoard.Core;
using ZhccCargoBoard.Core.Export;

namespace ZhccCargoBoard.App.Dialogs;

public partial class ExportDialog : Window
{
    private readonly MainViewModel _viewModel;

    public ExportDialog(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;

        ScopeFiltered.Content = $"当前筛选结果（{_viewModel.Rows.Count} 条）";
        ScopeAll.Content = $"全部本地记录（{_viewModel.TotalCount} 条）";

        var folder = _viewModel.Settings.LastExportDirectory;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            folder = AppPaths.DefaultExportDirectory;
        }
        PathBox.Text = Path.Combine(folder, $"货机航班动态_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");

        BuildExtras();
        RangeFrom.SelectedDate = DateTime.Today;
        RangeTo.SelectedDate = DateTime.Today.AddDays(3);
        UpdateScopeState();
    }

    private void BuildExtras()
    {
        var selected = new HashSet<string>(
            _viewModel.Settings.ExtraExportColumns,
            StringComparer.OrdinalIgnoreCase);

        foreach (var column in ExportColumns.Extras)
        {
            ExtrasPanel.Children.Add(new CheckBox
            {
                Content = column.Header,
                Tag = column.Key,
                IsChecked = selected.Contains(column.Key),
                Margin = new Thickness(0, 0, 16, 6),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
    }

    private IEnumerable<string> CheckedExtras() =>
        ExtrasPanel.Children.OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => (string)c.Tag);

    private void Scope_Changed(object sender, RoutedEventArgs e) => UpdateScopeState();

    private void UpdateScopeState()
    {
        if (!IsInitialized) return;
        var useRange = ScopeRange.IsChecked == true;
        RangeFrom.IsEnabled = useRange;
        RangeTo.IsEnabled = useRange;
        UpdateCount();
    }

    private void UpdateCount()
    {
        if (!IsInitialized) return;
        try
        {
            var records = CollectRecords(out _);
            CountText.Text = records.Count == 0
                ? "当前范围没有可导出的记录"
                : $"将导出 {records.Count} 条记录";
        }
        catch
        {
            CountText.Text = "";
        }
    }

    private List<ZhccCargoBoard.Core.Models.FlightRecord> CollectRecords(out ExportScope scope)
    {
        if (ScopeFiltered.IsChecked == true)
        {
            scope = ExportScope.Filtered;
            return _viewModel.BuildExportRecords(ExportScope.Filtered, null, null);
        }

        if (ScopeAll.IsChecked == true)
        {
            scope = ExportScope.All;
            return _viewModel.BuildExportRecords(ExportScope.All, null, null);
        }

        scope = ExportScope.DateRange;
        return _viewModel.BuildExportRecords(ExportScope.DateRange, RangeFrom.SelectedDate, RangeTo.SelectedDate);
    }

    private void SelectAllExtras_Click(object sender, RoutedEventArgs e)
    {
        foreach (var box in ExtrasPanel.Children.OfType<CheckBox>())
        {
            box.IsChecked = true;
        }
    }

    private void ClearExtras_Click(object sender, RoutedEventArgs e)
    {
        foreach (var box in ExtrasPanel.Children.OfType<CheckBox>())
        {
            box.IsChecked = false;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var current = PathBox.Text;
        var directory = Path.GetDirectoryName(current);
        var dialog = new SaveFileDialog
        {
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = Path.GetFileName(current),
            InitialDirectory = Directory.Exists(directory) ? directory : AppPaths.DefaultExportDirectory
        };

        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FileName;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (ScopeRange.IsChecked == true && RangeFrom.SelectedDate is null && RangeTo.SelectedDate is null)
        {
            MessageBox.Show(this, "请选择至少一个日期。", "导出", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var records = CollectRecords(out var scope);
        if (records.Count == 0)
        {
            MessageBox.Show(this, "当前范围内没有可导出的记录，请调整筛选条件或先刷新数据。", "导出",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var path = PathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            MessageBox.Show(this, "请填写保存路径。", "导出", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            path += ".xlsx";
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var extras = CheckedExtras().ToList();
            var rows = XlsxExporter.Export(path, records, new ExportOptions
            {
                ExtraColumnKeys = extras,
                DateFormat = _viewModel.Settings.DateFormat
            });

            _viewModel.Settings.LastExportDirectory = directory;
            _viewModel.Settings.ExtraExportColumns = extras;
            _viewModel.Settings.Save();

            Log.Info($"导出 {rows} 行 -> {path}（范围：{scope}）");

            var answer = MessageBox.Show(this,
                $"已导出 {rows} 行到：\n{path}\n\n是否立即打开该文件？",
                "导出成功", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (answer == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }

            DialogResult = true;
        }
        catch (Exception ex)
        {
            Log.Error("导出失败", ex);
            MessageBox.Show(this, $"导出失败：\n{ex.Message}", "导出", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
