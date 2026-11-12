using System.Diagnostics;
using ZhccCargoBoard.Core;
using ZhccCargoBoard.Core.Export;
using ZhccCargoBoard.Core.Services;
using ZhccCargoBoard.Core.Storage;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
var target = args.Length > 1 ? args[1] : Path.Combine(Environment.CurrentDirectory, "out", "货机航班动态.xlsx");

AppPaths.EnsureCreated();
Console.WriteLine($"数据目录：{AppPaths.DataDirectory}");

using var store = new FlightStore();
store.Initialize();
Log.Info($"CLI 启动：{command}");

try
{
    switch (command)
    {
        case "fetch":
            await FetchAsync(store, printRecords: false);
            break;
        case "export":
            Export(store, target);
            break;
        case "all":
            await FetchAsync(store, printRecords: false);
            Export(store, target);
            break;
        case "print":
            await FetchAsync(store, printRecords: true);
            break;
        default:
            Console.WriteLine("用法：ZhccCargoBoard.Cli [fetch|export|all|print] [输出.xlsx]");
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"失败：{ex.Message}");
    Log.Error("CLI 失败", ex);
    return 1;
}

return 0;

static async Task FetchAsync(FlightStore store, bool printRecords)
{
    using var client = new ZzcargoClient();
    var progress = new Progress<FetchProgress>(p => Console.WriteLine($"  {p.Message}"));
    var result = await client.FetchAllAsync(progress);
    Console.WriteLine($"抓取完成：{result.Records.Count} 条 / {result.Pages} 页 / 请求 {result.Requests} 次 / 耗时 {result.Elapsed.TotalSeconds:F1}s");

    var upsert = store.Upsert(result.Records);
    store.SetLastRefreshUtc(DateTime.UtcNow);
    Console.WriteLine($"入库：新增 {upsert.Inserted}，状态变化 {upsert.Changed}，未变化 {upsert.Unchanged}");

    var stats = store.GetStatistics();
    Console.WriteLine($"本地共有 {stats.Total} 条，日期范围 {stats.EarliestDate} ~ {stats.LatestDate}");

    if (printRecords)
    {
        foreach (var r in result.Records.Take(5))
        {
            Console.WriteLine($"  {r.FlightInfo,-8} {r.OriginCn,-10} -> {r.DestCn,-12} {r.ExecDate} {r.ScheDep} {r.ActlDep} {r.StatusText}");
        }
    }
}

static void Export(FlightStore store, string path)
{
    var records = store.LoadAll().OrderBy(r => r.ExecDate).ThenBy(r => r.ScheDep).ToList();
    Console.WriteLine($"读取 {records.Count} 条，导出到 {path}");
    var count = XlsxExporter.Export(path, records, new ExportOptions { ExtraColumnKeys = new List<string>() });
    Console.WriteLine($"已导出 {count} 行");
}
