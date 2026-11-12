using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ZhccCargoBoard.App.Converters;

/// <summary>把航班状态文字映射成状态圆点颜色。</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly Brush Arrived = Freeze("#2E7D32");
    private static readonly Brush Departed = Freeze("#1565C0");
    private static readonly Brush Planned = Freeze("#8A96A3");
    private static readonly Brush InProgress = Freeze("#C77700");
    private static readonly Brush Disrupted = Freeze("#C0392B");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? "";
        if (text.Contains("延误", StringComparison.Ordinal) ||
            text.Contains("取消", StringComparison.Ordinal) ||
            text.Contains("备降", StringComparison.Ordinal) ||
            text.Contains("返航", StringComparison.Ordinal))
        {
            return Disrupted;
        }

        return text switch
        {
            "到达" => Arrived,
            "起飞" => Departed,
            "计划" or "" => Planned,
            "前方起飞" or "正在值机" or "登机" or "登机结束" or "催促登机" => InProgress,
            _ => Planned
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>空字符串显示为短横线，避免表格出现大片空白。</summary>
public sealed class EmptyToDashConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString();
        return string.IsNullOrWhiteSpace(text) ? "—" : text;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
