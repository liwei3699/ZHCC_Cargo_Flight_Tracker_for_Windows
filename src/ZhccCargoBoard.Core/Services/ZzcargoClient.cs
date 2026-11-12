using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using ZhccCargoBoard.Core.Models;

namespace ZhccCargoBoard.Core.Services;

public sealed record FetchProgress(int Completed, int Total, string Message);

public sealed record FetchResult(
    IReadOnlyList<FlightRecord> Records,
    int Pages,
    int Total,
    TimeSpan Elapsed,
    int Requests);

public sealed class ZzcargoException : Exception
{
    public ZzcargoException(string message) : base(message) { }
    public ZzcargoException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>郑州机场货运航班动态抓取客户端。</summary>
public sealed class ZzcargoClient : IDisposable
{
    public const string SiteBase = "https://www.zzcargo.com";
    public const string Page1Url = SiteBase + "/airline/1";
    private const string PageEndpoint = SiteBase + "/flightDynamic/skipTo";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public ZzcargoClient() : this(null, false) { }

    public ZzcargoClient(bool allowInsecureTls) : this(null, allowInsecureTls) { }

    public ZzcargoClient(HttpMessageHandler? handler, bool allowInsecureTls = false)
    {
        handler ??= CreateDefaultHandler(allowInsecureTls);
        _http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(35)
        };
        _ownsHttp = true;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/javascript", 0.9));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
        _http.DefaultRequestHeaders.Referrer = new Uri(Page1Url);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", SiteBase);
    }

    /// <summary>
    /// 关键：本站的 AAAA 记录在部分网络上 TCP 443 不通，系统默认先连 IPv6 会卡满 20 秒以上。
    /// 这里优先连 IPv4，单个地址 5 秒超时，失败再退回其它地址。
    /// </summary>
    public static SocketsHttpHandler CreateDefaultHandler(bool allowInsecureTls = false)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(12),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 8,
            ConnectCallback = ConnectPreferIpv4Async
        };
        if (allowInsecureTls)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }
        return handler;
    }

    private static async ValueTask<Stream> ConnectPreferIpv4Async(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        IPAddress[] addresses = [];
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 交给 Socket 自己解析，下面还有兜底路径
        }

        foreach (var address in addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                await socket.ConnectAsync(new IPEndPoint(address, port), cts.Token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
            }
        }

        var fallback = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await fallback.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(fallback, ownsSocket: true);
        }
        catch
        {
            fallback.Dispose();
            throw new ZzcargoException($"无法连接 {host}:{port}，请检查网络或代理设置。");
        }
    }

    /// <summary>抓取全部分页。先取第 1 页拿到总页数，其余并发抓取，并校验总数一致性。</summary>
    public async Task<FetchResult> FetchAllAsync(
        IProgress<FetchProgress>? progress = null,
        CancellationToken cancellationToken = default,
        int concurrency = 6)
    {
        var sw = Stopwatch.StartNew();
        var now = DateTime.Now;
        var stats = new FetchStats();

        FetchProgress Report(int done, int total, string message) => new(done, total, message);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            progress?.Report(Report(0, 0, "正在读取第 1 页…"));
            var first = await FetchPageAsync(1, now, cancellationToken).ConfigureAwait(false);
            stats.Requests++;
            var pages = first.Pages;
            var total = first.Total;
            if (pages <= 0)
            {
                throw new ZzcargoException("数据源返回的总页数异常（0 页）。");
            }

            var collected = new PageResult[pages + 1];
            collected[1] = first;
            stats.Done = 1;
            progress?.Report(Report(stats.Done, pages, $"正在抓取 1/{pages} 页…"));

            if (pages > 1)
            {
                await Parallel.ForEachAsync(
                    Enumerable.Range(2, pages - 1),
                    new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, concurrency), CancellationToken = cancellationToken },
                    async (page, ct) =>
                    {
                        var result = await FetchPageAsync(page, now, ct).ConfigureAwait(false);
                        collected[page] = result;
                        var current = Interlocked.Increment(ref stats.Done);
                        Interlocked.Increment(ref stats.Requests);
                        progress?.Report(Report(current, pages, $"正在抓取 {current}/{pages} 页…"));
                    }).ConfigureAwait(false);
            }

            var sum = collected.Where(p => p is not null).Sum(p => p!.Records.Count);
            var pagesConsistent = collected.Where(p => p is not null).All(p => p!.Pages == pages);
            var complete = collected.Skip(1).All(p => p is not null);

            if (complete && pagesConsistent && (total == sum || total == 0))
            {
                var records = new List<FlightRecord>(sum);
                for (var i = 1; i <= pages; i++)
                {
                    records.AddRange(collected[i]!.Records);
                }
                sw.Stop();
                return new FetchResult(records, pages, total, sw.Elapsed, stats.Requests);
            }

            // 抓取过程中网站数据滚动，重来一次
            progress?.Report(Report(0, 0, "数据在抓取过程中发生变化，重新抓取…"));
        }

        throw new ZzcargoException("多次抓取后数据仍不一致，请稍后重试。");
    }

    private sealed record PageResult(int Page, int Pages, int Total, List<FlightRecord> Records);

    private async Task<PageResult> FetchPageAsync(int page, DateTime now, CancellationToken cancellationToken)
    {
        var url = $"{PageEndpoint}?jstime={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        Exception? lastError = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(800 * Math.Pow(2, attempt - 1)), cancellationToken)
                    .ConfigureAwait(false);
            }

            try
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["current"] = page.ToString(),
                    ["flightNo"] = "",
                    ["execDate"] = "",
                    ["ioAttr"] = "",
                    ["agency"] = "",
                    ["startStationCn"] = "",
                    ["terminalStationCn"] = ""
                });

                using var response = await _http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new ZzcargoException($"数据源返回 HTTP {(int)response.StatusCode}（第 {page} 页）。");
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return ParsePage(page, body, now);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        var message = lastError switch
        {
            ZzcargoException ze => ze.Message,
            TaskCanceledException => "请求超时（35 秒），请检查网络。",
            _ => lastError?.Message ?? "未知错误"
        };
        throw new ZzcargoException($"抓取第 {page} 页失败：{message}", lastError ?? new Exception(message));
    }

    private static PageResult ParsePage(int page, string body, DateTime now)
    {
        var trimmed = body.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{')
        {
            var snippet = body.Length > 120 ? body[..120] : body;
            throw new ZzcargoException($"数据源返回的不是 JSON（第 {page} 页）：{snippet}");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var code = root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number
                ? codeEl.GetInt32()
                : 0;
            if (code != 200)
            {
                throw new ZzcargoException($"数据源返回 code={code}（第 {page} 页）。");
            }

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                throw new ZzcargoException($"数据源响应缺少 data 节点（第 {page} 页）。");
            }

            var pages = data.TryGetProperty("pages", out var pagesEl) && pagesEl.ValueKind == JsonValueKind.Number
                ? pagesEl.GetInt32() : 0;
            var total = data.TryGetProperty("total", out var totalEl) && totalEl.ValueKind == JsonValueKind.Number
                ? totalEl.GetInt32() : 0;

            var records = new List<FlightRecord>();
            if (data.TryGetProperty("records", out var recordsEl) && recordsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in recordsEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        records.Add(FlightRecord.FromJson(item, now));
                    }
                }
            }

            return new PageResult(page, pages, total, records);
        }
        catch (JsonException ex)
        {
            throw new ZzcargoException($"数据源 JSON 解析失败（第 {page} 页）：{ex.Message}", ex);
        }
    }

    private sealed class FetchStats
    {
        public int Done;
        public int Requests;
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
