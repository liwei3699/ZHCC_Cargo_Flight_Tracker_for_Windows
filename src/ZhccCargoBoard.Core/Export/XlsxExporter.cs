using ClosedXML.Excel;
using ZhccCargoBoard.Core.Models;

namespace ZhccCargoBoard.Core.Export;

public sealed record ExportOptions
{
    public IReadOnlyList<string>? ExtraColumnKeys { get; init; } = ExportColumns.DefaultExtraKeys;

    /// <summary>“航班日期”列格式，默认 yyyy-mm-dd。</summary>
    public string DateFormat { get; init; } = "yyyy-mm-dd";

    public string SheetName { get; init; } = "货机航班动态";

    public bool FreezeHeader { get; init; } = true;

    public bool AutoFilter { get; init; } = true;
}

/// <summary>
/// 导出 xlsx。前 9 列的列名、顺序、字体（等线 11）、居中方式与 example.xlsx 保持一致；
/// 时间列写真正的 Excel 时间（h:mm），日期列写真正的 Excel 日期。
/// </summary>
public static class XlsxExporter
{
    private const string FontName = "等线";
    private const double FontSize = 11;

    public static int Export(string path, IReadOnlyList<FlightRecord> records, ExportOptions? options = null)
    {
        options ??= new ExportOptions();
        var columns = ExportColumns.Resolve(options.ExtraColumnKeys);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SanitizeSheetName(options.SheetName));

        // 表头
        for (var c = 0; c < columns.Count; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = columns[c].Header;
        }

        // 数据
        for (var r = 0; r < records.Count; r++)
        {
            var record = records[r];
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                var value = columns[c].Get(record);
                switch (value)
                {
                    case null:
                        continue;
                    case TimeSpan time:
                        // 直接写 Excel 的“天”序列值（0.875 = 21:00），避免 1899 年基准日期带来的负序列。
                        cell.Value = time.TotalDays;
                        cell.Style.DateFormat.Format = "h:mm";
                        break;
                    case DateTime date:
                        cell.Value = date;
                        cell.Style.DateFormat.Format = options.DateFormat;
                        break;
                    default:
                        var text = value.ToString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            cell.Value = text;
                        }
                        break;
                }
            }
        }

        var lastRow = Math.Max(1, records.Count + 1);
        var used = sheet.Range(1, 1, lastRow, columns.Count);

        used.Style.Font.FontName = FontName;
        used.Style.Font.FontSize = FontSize;
        used.Style.Font.Bold = false;
        used.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        used.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        for (var c = 0; c < columns.Count; c++)
        {
            sheet.Column(c + 1).Width = columns[c].Width;
        }

        if (options.FreezeHeader)
        {
            sheet.SheetView.FreezeRows(1);
        }

        if (options.AutoFilter && records.Count > 0)
        {
            used.SetAutoFilter();
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        workbook.SaveAs(path);

        return records.Count;
    }

    private static string SanitizeSheetName(string name)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var cleaned = new string(name.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        if (string.IsNullOrEmpty(cleaned))
        {
            cleaned = "货机航班动态";
        }
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }
}
