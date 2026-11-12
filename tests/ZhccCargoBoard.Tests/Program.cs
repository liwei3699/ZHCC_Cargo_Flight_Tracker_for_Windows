using System.Text.Json;
using ZhccCargoBoard.Core.Export;
using ZhccCargoBoard.Core.Models;
using ZhccCargoBoard.Core.Storage;

namespace ZhccCargoBoard.Tests;

/// <summary>
/// 轻量自检程序（不依赖测试框架，直接 dotnet run 即可）。
/// 覆盖：JSON 映射 / 时间解析 / 状态文案 / 内容指纹 / SQLite 入库与清理 / xlsx 导出格式。
/// </summary>
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("== 货机航班动态 · Core 自检 ==");

        Run("FlightRecord：字段映射", FlightRecordTests.MapsCoreFields);
        Run("FlightRecord：时间解析与空值", FlightRecordTests.TimeProperties);
        Run("FlightRecord：状态为空时显示“计划”", FlightRecordTests.StatusFallback);
        Run("FlightRecord：内容指纹随状态变化", FlightRecordTests.Fingerprint);
        Run("FlightRecord：ffid 缺失时的兜底", FlightRecordTests.FfidFallback);

        Run("FlightStore：新增 / 未变化 / 变化", FlightStoreTests.InsertUnchangedChanged);
        Run("FlightStore：批内去重", FlightStoreTests.BatchDedupe);
        Run("FlightStore：按航班日期过滤", FlightStoreTests.LoadAllByDate);
        Run("FlightStore：按保留天数清理", FlightStoreTests.Purge);

        Run("XlsxExporter：基础 9 列与 Excel 类型", XlsxTests.BaseColumns);
        Run("XlsxExporter：附加列", XlsxTests.ExtraColumns);
        Run("ExportColumns：表头顺序与 example.xlsx 一致", XlsxTests.HeaderOrder);

        Console.WriteLine();
        Console.WriteLine($"通过 {_passed} 项，失败 {_failed} 项。");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"  [通过] {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"  [失败] {name}");
            Console.WriteLine($"         {ex.GetType().Name}: {ex.Message}");
            var frames = (ex.StackTrace ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var frame in frames.Take(4)) Console.WriteLine("         " + frame.Trim());
        }
    }
}

internal static class Check
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"期望 <{expected}>，实际 <{actual}>");
    }

    public static void EqualDouble(double expected, double actual, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"期望 <{expected}>，实际 <{actual}>");
    }

    public static void True(bool condition, string message = "条件不成立")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void False(bool condition, string message = "条件应当不成立")
    {
        if (condition) throw new InvalidOperationException(message);
    }

    public static void Null(object? value, string message = "应当为 null")
    {
        if (value is not null) throw new InvalidOperationException($"{message}，实际 <{value}>");
    }

    public static void NotNull(object? value, string message = "不应当为 null")
    {
        if (value is null) throw new InvalidOperationException(message);
    }

    public static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        var e = expected.ToArray();
        var a = actual.ToArray();
        if (!e.SequenceEqual(a))
            throw new InvalidOperationException($"期望 [{string.Join(", ", e)}]，实际 [{string.Join(", ", a)}]");
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T ex) { return ex; }
        throw new InvalidOperationException($"应当抛出 {typeof(T).Name}");
    }
}

internal static class FlightRecordTests
{
    private const string SampleJson = """
    {
      "flightNo": "6376",
      "airlinesIataCd": "CZ",
      "agency": "南航",
      "startStationCn": "乌鲁木齐",
      "terminalStationCn": "郑州",
      "startOpaIataCd": "URC",
      "terminalOpaIataCd": "CGO",
      "airrouteCn": "乌鲁木齐-郑州",
      "ioAttr": "A",
      "normalState": "ARR",
      "normalStateCn": "到达",
      "execDate": "2026-11-05",
      "scheTakeOffTime": "21:00",
      "actlTakeOffTime": "21:12",
      "scheLandingTime": "00:50",
      "actlLandingTime": "",
      "craftNo": "B1400",
      "craftType": "B738",
      "flightTaskCn": "正班",
      "regionAttr": "DOM",
      "stand": "223",
      "terminal": "T2",
      "ffid": "CZ-6376-20261105005000-A",
      "recordTime": "1793825454171",
      "id": 114985524
    }
    """;

    internal static FlightRecord Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return FlightRecord.FromJson(doc.RootElement, new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc));
    }

    public static void MapsCoreFields()
    {
        var r = Parse(SampleJson);
        Check.Equal("6376", r.FlightNo);
        Check.Equal("CZ", r.AirlineCode);
        Check.Equal("CZ6376", r.FlightInfo);
        Check.Equal("乌鲁木齐", r.OriginCn);
        Check.Equal("郑州", r.DestCn);
        Check.Equal("URC", r.OriginIata);
        Check.Equal("CGO", r.DestIata);
        Check.Equal("到达", r.StatusCn);
        Check.Equal("A", r.IoAttr);
        Check.Equal("2026-11-05", r.ExecDate);
        Check.Equal("B738", r.CraftType);
        Check.Equal("B1400", r.CraftNo);
        Check.Equal("223", r.Stand);
        Check.Equal("T2", r.Terminal);
        Check.Equal("CZ-6376-20261105005000-A", r.Ffid);
        Check.Equal("114985524", r.RecordId);
    }

    public static void TimeProperties()
    {
        var r = Parse(SampleJson);
        Check.Equal(new TimeSpan(21, 0, 0), r.ScheDepTime);
        Check.Equal(new TimeSpan(21, 12, 0), r.ActlDepTime);
        Check.Equal(new TimeSpan(0, 50, 0), r.ScheArrTime);
        Check.Null(r.ActlArrTime);   // 空字符串 -> null，导出时为真空白单元格
    }

    public static void StatusFallback()
    {
        Check.Equal("计划", Parse("""{"flightNo":"1","normalStateCn":""}""").StatusText);
        Check.Equal("到达", Parse(SampleJson).StatusText);
    }

    public static void Fingerprint()
    {
        var a = Parse(SampleJson);
        var b = Parse(SampleJson.Replace("\"normalStateCn\": \"到达\"", "\"normalStateCn\": \"起飞\""));
        Check.False(a.ContentFingerprint() == b.ContentFingerprint(), "状态变化后指纹应当不同");
        Check.Equal(a.ContentFingerprint(), Parse(SampleJson).ContentFingerprint());
    }

    public static void FfidFallback()
    {
        var r = Parse("""{"flightNo":"819","airlinesIataCd":"K4","execDate":"2026-11-06","ioAttr":"D","scheTakeOffTime":"03:00","id":42}""");
        Check.Equal("id:42", r.Ffid);
        Check.Equal("K4819", r.FlightInfo);
    }
}

internal static class TestPaths
{
    /// <summary>
    /// 测试用临时目录。默认系统 TEMP；受限环境可用环境变量 ZHCC_TEST_DIR 指到可写目录。
    /// </summary>
    public static string NewDirectory(string prefix)
    {
        var root = Environment.GetEnvironmentVariable("ZHCC_TEST_DIR");
        if (string.IsNullOrWhiteSpace(root)) root = Path.GetTempPath();
        var dir = Path.Combine(root, $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}

internal sealed class FlightStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly FlightStore _store;

    private FlightStoreTests()
    {
        _dir = TestPaths.NewDirectory("zhcc-tests");
        _store = new FlightStore(Path.Combine(_dir, "test.db"));
        _store.Initialize();
    }

    private static FlightStoreTests New() => new();

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* 忽略清理失败 */ }
    }

    private static FlightRecord Record(string ffid, string status = "到达", string actlDep = "21:12") => new()
    {
        Ffid = ffid,
        FlightNo = "6376",
        AirlineCode = "CZ",
        OriginCn = "乌鲁木齐",
        DestCn = "郑州",
        ExecDate = "2026-11-05",
        ScheDep = "21:00",
        ActlDep = actlDep,
        ScheArr = "00:50",
        StatusCn = status,
        StatusCode = "ARR",
        IoAttr = "A",
        FirstSeen = DateTime.UtcNow,
        LastSeen = DateTime.UtcNow
    };

    public static void InsertUnchangedChanged()
    {
        using var t = New();
        var first = t._store.Upsert(new[] { Record("a"), Record("b") });
        Check.Equal(2, first.Inserted);
        Check.Equal(0, first.Changed);

        var same = t._store.Upsert(new[] { Record("a"), Record("b") });
        Check.Equal(0, same.Inserted);
        Check.Equal(0, same.Changed);
        Check.Equal(2, same.Unchanged);

        var changed = t._store.Upsert(new[] { Record("a", status: "起飞"), Record("b") });
        Check.Equal(0, changed.Inserted);
        Check.Equal(1, changed.Changed);

        var all = t._store.LoadAll();
        Check.Equal(2, all.Count);
        Check.Equal("起飞", all.Single(r => r.Ffid == "a").StatusCn);
    }

    public static void BatchDedupe()
    {
        using var t = New();
        var result = t._store.Upsert(new[] { Record("dup", "到达"), Record("dup", "起飞") });
        Check.Equal(1, result.Inserted);
        Check.Equal("起飞", t._store.LoadAll().Single().StatusCn);
    }

    public static void LoadAllByDate()
    {
        using var t = New();
        var d1 = Record("d1"); d1.ExecDate = "2026-11-05";
        var d2 = Record("d2"); d2.ExecDate = "2026-11-06";
        var d3 = Record("d3"); d3.ExecDate = "2026-11-07";
        t._store.Upsert(new[] { d1, d2, d3 });

        Check.Equal(2, t._store.LoadAll("2026-11-06", "2026-11-07").Count);
        Check.Equal(1, t._store.LoadAll("2026-11-05", "2026-11-05").Count);
    }

    public static void Purge()
    {
        using var t = New();
        t._store.Upsert(new[] { Record("old"), Record("new") });
        Check.Equal(2, t._store.GetStatistics().Total);

        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(t._dir, "test.db")}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE flight SET last_seen = @t WHERE ffid = 'old'";
            cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.AddDays(-40).ToString("o"));
            cmd.ExecuteNonQuery();
        }

        Check.Equal(1, t._store.Purge(30));
        Check.Equal("new", t._store.LoadAll().Single().Ffid);
    }
}

internal static class XlsxTests
{
    private static string NewDir() => TestPaths.NewDirectory("zhcc-xlsx");

    private static FlightRecord Sample() => new()
    {
        Ffid = "f1",
        FlightNo = "6376",
        AirlineCode = "CZ",
        OriginCn = "乌鲁木齐",
        DestCn = "郑州",
        ExecDate = "2026-11-05",
        ScheDep = "21:00",
        ActlDep = "21:12",
        ScheArr = "00:50",
        ActlArr = "",
        StatusCn = "到达"
    };

    public static void BaseColumns()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "out.xlsx");
            var rows = XlsxExporter.Export(path, new[] { Sample() }, new ExportOptions { ExtraColumnKeys = Array.Empty<string>() });
            Check.Equal(1, rows);

            using var wb = new ClosedXML.Excel.XLWorkbook(path);
            var ws = wb.Worksheet(1);
            Check.Equal("航班信息", ws.Cell(1, 1).GetString());
            Check.Equal("航班状态", ws.Cell(1, 9).GetString());
            Check.Equal("CZ6376", ws.Cell(2, 1).GetString());
            Check.Equal("乌鲁木齐", ws.Cell(2, 2).GetString());
            Check.Equal("郑州", ws.Cell(2, 3).GetString());

            // 时间写成 Excel 时间序列，格式 h:mm（显示为 21:00 / 00:50）
            Check.Equal("h:mm", ws.Cell(2, 4).Style.DateFormat.Format);
            Check.Equal("21:00", ws.Cell(2, 4).GetFormattedString());
            Check.Equal("21:12", ws.Cell(2, 5).GetFormattedString());
            Check.Equal("0:50", ws.Cell(2, 6).GetFormattedString());   // h:mm 不补前导零，与示例格式一致

            // 空的实际降落时间保持真空白
            Check.True(ws.Cell(2, 7).IsEmpty(), "实际降落时间应为空单元格");

            // 日期是真日期
            Check.Equal(new DateTime(2026, 11, 5), ws.Cell(2, 8).GetDateTime());

            Check.Equal(9, ws.ColumnsUsed().Count());
            Check.Equal(1, ws.SheetView.SplitRow);
            Check.True(ws.AutoFilter.IsEnabled, "应当有自动筛选");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* 忽略 */ }
        }
    }

    public static void ExtraColumns()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "extras.xlsx");
            XlsxExporter.Export(path, new[] { Sample() }, new ExportOptions
            {
                ExtraColumnKeys = new List<string> { "agency", "craft_type" }
            });

            using var wb = new ClosedXML.Excel.XLWorkbook(path);
            var ws = wb.Worksheet(1);
            Check.Equal("代理", ws.Cell(1, 10).GetString());
            Check.Equal("机型", ws.Cell(1, 11).GetString());
            Check.Equal(11, ws.Row(1).LastCellUsed()!.Address.ColumnNumber);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* 忽略 */ }
        }
    }

    public static void HeaderOrder()
    {
        Check.Sequence(new[]
        {
            "航班信息", "起飞机场", "降落机场", "计划起飞时间", "实际起飞时间",
            "计划降落时间", "实际降落时间", "航班日期", "航班状态"
        }, ExportColumns.Base.Select(c => c.Header));
    }
}
