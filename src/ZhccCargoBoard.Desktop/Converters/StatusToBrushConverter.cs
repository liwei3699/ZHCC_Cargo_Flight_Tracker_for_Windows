using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ZhccCargoBoard.Desktop.Converters;

/// <summary>航班状态 → 状态圆点颜色（与 Windows 版配色一致）。</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly IBrush Arrived = Brush.Parse("#2E7D32");
    private static readonly IBrush Departed = Brush.Parse("#1565C0");
    private static readonly IBrush Planned = Brush.Parse("#8A96A3");
    private static readonly IBrush InProgress = Brush.Parse("#C77700");
    private static readonly IBrush Disrupted = Brush.Parse("#C0392B");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
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

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
