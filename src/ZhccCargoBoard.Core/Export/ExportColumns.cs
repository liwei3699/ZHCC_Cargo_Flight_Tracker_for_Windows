using ZhccCargoBoard.Core.Models;

namespace ZhccCargoBoard.Core.Export;

/// <summary>一列导出定义。值为 <see cref="DateTime"/> 时按日期写入，<see cref="TimeSpan"/> 时按时间写入。</summary>
public sealed record ExportColumn(string Key, string Header, Func<FlightRecord, object?> Get, double Width);

public static class ExportColumns
{
    /// <summary>与 example.xlsx 完全一致的前 9 列（顺序、名称都不许改）。</summary>
    public static IReadOnlyList<ExportColumn> Base { get; } = new List<ExportColumn>
    {
        new("flight_info", "航班信息", r => r.FlightInfo, 12),
        new("origin", "起飞机场", r => r.OriginCn, 24),
        new("dest", "降落机场", r => r.DestCn, 26),
        new("sche_dep", "计划起飞时间", r => r.ScheDepTime, 14),
        new("actl_dep", "实际起飞时间", r => r.ActlDepTime, 14),
        new("sche_arr", "计划降落时间", r => r.ScheArrTime, 14),
        new("actl_arr", "实际降落时间", r => r.ActlArrTime, 14),
        new("exec_date", "航班日期", r => r.ExecDateValue, 13),
        new("status", "航班状态", r => r.StatusText, 10)
    };

    /// <summary>附加的分析列，可在导出时勾选。</summary>
    public static IReadOnlyList<ExportColumn> Extras { get; } = new List<ExportColumn>
    {
        new("agency", "代理", r => r.Agency, 10),
        new("direction", "进出港", r => r.DirectionText, 8),
        new("region", "区域", r => r.RegionText, 8),
        new("craft_type", "机型", r => r.CraftType, 10),
        new("craft_no", "机号", r => r.CraftNo, 10),
        new("route", "航线", r => r.RouteAndStations, 22),
        new("stand", "机位", r => r.Stand, 8),
        new("terminal", "航站楼", r => r.Terminal, 8),
        new("task", "航班任务", r => r.FlightTaskCn, 10),
        new("abnormal", "异常", r => r.AbnormalStateCn, 12)
    };

    public static IReadOnlyList<string> ExtraKeys { get; } =
        Extras.Select(c => c.Key).ToList();

    public static IReadOnlyList<string> DefaultExtraKeys { get; } = ExtraKeys.ToList();

    public static IReadOnlyList<ExportColumn> Resolve(IEnumerable<string>? extraKeys)
    {
        var columns = new List<ExportColumn>(Base);
        if (extraKeys is null)
        {
            return columns;
        }

        var wanted = new HashSet<string>(extraKeys, StringComparer.OrdinalIgnoreCase);
        columns.AddRange(Extras.Where(c => wanted.Contains(c.Key)));
        return columns;
    }
}
