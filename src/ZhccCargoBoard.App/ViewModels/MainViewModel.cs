using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using ZhccCargoBoard.Core;
using ZhccCargoBoard.Core.Export;
using ZhccCargoBoard.Core.Models;
using ZhccCargoBoard.Core.Services;
using ZhccCargoBoard.Core.Storage;

namespace ZhccCargoBoard.App.ViewModels;

/// <summary>主界面的状态、筛选、排序与刷新逻辑。</summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    public const string AllOption = "全部";

    private readonly FlightStore _store;
    private readonly DispatcherTimer _timer;
    private List<FlightRecord> _all = new();
    private IReadOnlyList<FlightRecord> _rows = Array.Empty<FlightRecord>();
    private CancellationTokenSource? _cts;
    private bool _suppressFilter;

    private string _keyword = "";
    private string _flightNo = "";
    private string _agent = AllOption;
    private string _direction = AllOption;
    private string _origin = "";
    private string _dest = "";
    private DateTime? _dateFrom;
    private DateTime? _dateTo;
    private string _status = AllOption;
    private string _timeFrom = "";
    private string _timeTo = "";
    private string _craftType = AllOption;
    private string _airline = AllOption;
    private string _craftNo = "";

    private string _sortKey = "exec_date";
    private bool _sortDescending;

    private bool _isBusy;
    private string _statusMessage = "就绪";
    private string? _errorMessage;
    private FlightRecord? _selectedFlight;
    private DateTime? _lastRefreshLocal;
    private DateTime? _nextRefreshAt;
    private string _clockText = "";

    public MainViewModel(AppSettings settings, FlightStore store)
    {
        Settings = settings;
        _store = store;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnTimerTick();
    }

    public AppSettings Settings { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<FlightRecord> Rows
    {
        get => _rows;
        private set
        {
            _rows = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibleCount));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public ObservableCollection<string> AgentOptions { get; } = new() { AllOption };
    public ObservableCollection<string> StatusOptions { get; } = new() { AllOption };
    public ObservableCollection<string> CraftTypeOptions { get; } = new() { AllOption };
    public ObservableCollection<string> AirlineOptions { get; } = new() { AllOption };
    public ObservableCollection<string> DirectionOptions { get; } = new() { AllOption, "进港", "出港" };

    // ---------------- 筛选条件 ----------------
    public string Keyword
    {
        get => _keyword;
        set => SetFilter(ref _keyword, value);
    }

    public string FlightNo
    {
        get => _flightNo;
        set => SetFilter(ref _flightNo, value);
    }

    public string Agent
    {
        get => _agent;
        set => SetFilter(ref _agent, value);
    }

    public string Direction
    {
        get => _direction;
        set => SetFilter(ref _direction, value);
    }

    public string Origin
    {
        get => _origin;
        set => SetFilter(ref _origin, value);
    }

    public string Dest
    {
        get => _dest;
        set => SetFilter(ref _dest, value);
    }

    public DateTime? DateFrom
    {
        get => _dateFrom;
        set
        {
            if (_dateFrom == value) return;
            _dateFrom = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    public DateTime? DateTo
    {
        get => _dateTo;
        set
        {
            if (_dateTo == value) return;
            _dateTo = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    public string Status
    {
        get => _status;
        set => SetFilter(ref _status, value);
    }

    public string TimeFrom
    {
        get => _timeFrom;
        set => SetFilter(ref _timeFrom, value);
    }

    public string TimeTo
    {
        get => _timeTo;
        set => SetFilter(ref _timeTo, value);
    }

    public string CraftType
    {
        get => _craftType;
        set => SetFilter(ref _craftType, value);
    }

    public string Airline
    {
        get => _airline;
        set => SetFilter(ref _airline, value);
    }

    public string CraftNo
    {
        get => _craftNo;
        set => SetFilter(ref _craftNo, value);
    }

    // ---------------- 排序 ----------------
    public string SortKey
    {
        get => _sortKey;
        private set
        {
            _sortKey = value;
            OnPropertyChanged();
        }
    }

    public bool SortDescending
    {
        get => _sortDescending;
        private set
        {
            _sortDescending = value;
            OnPropertyChanged();
        }
    }

    public void SortBy(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (string.Equals(SortKey, key, StringComparison.Ordinal))
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortKey = key;
            SortDescending = false;
        }
        ApplyFilters();
    }

    // ---------------- 状态 ----------------
    public FlightRecord? SelectedFlight
    {
        get => _selectedFlight;
        set
        {
            if (ReferenceEquals(_selectedFlight, value)) return;
            _selectedFlight = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotBusy));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsNotBusy => !IsBusy;

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsEmpty => !IsBusy && Rows.Count == 0;

    public int TotalCount => _all.Count;

    public int VisibleCount => Rows.Count;

    public string CountText => $"本地累积 {TotalCount} 条 · 当前显示 {VisibleCount} 条";

    public string LastRefreshText => _lastRefreshLocal is { } t
        ? (t.Date == DateTime.Today ? $"更新于 {t:HH:mm}" : $"更新于 {t:MM-dd HH:mm}")
        : "尚未刷新";

    public string AutoRefreshText => $"每 {Settings.RefreshMinutes} 分钟自动刷新";

    public string NextRefreshText
    {
        get
        {
            if (_nextRefreshAt is not { } next) return "";
            var remain = next - DateTime.Now;
            if (remain < TimeSpan.Zero) remain = TimeSpan.Zero;
            return $"下次刷新 {remain:mm\\:ss}";
        }
    }

    public string ClockText
    {
        get => _clockText;
        private set
        {
            _clockText = value;
            OnPropertyChanged();
        }
    }

    public DateTime? LastRefreshLocal => _lastRefreshLocal;

    public DateTime? NextRefreshAt => _nextRefreshAt;

    // ---------------- 生命周期 ----------------
    public void Initialize()
    {
        LoadFromStore();
        _lastRefreshLocal = _store.GetLastRefreshUtc()?.ToLocalTime();
        OnPropertyChanged(nameof(LastRefreshText));
        _nextRefreshAt = (_lastRefreshLocal ?? DateTime.Now).Add(Settings.RefreshInterval);
        _timer.Start();
        OnTimerTick();

        var stale = _lastRefreshLocal is null ||
                    DateTime.Now - _lastRefreshLocal.Value >= Settings.RefreshInterval;
        if (_all.Count == 0 || stale)
        {
            _ = RefreshAsync(manual: false);
        }
    }

    public void ApplySettings()
    {
        _nextRefreshAt = (_lastRefreshLocal ?? DateTime.Now).Add(Settings.RefreshInterval);
        OnPropertyChanged(nameof(NextRefreshText));
        OnPropertyChanged(nameof(AutoRefreshText));
    }

    public void LoadFromStore()
    {
        _all = _store.LoadAll();
        RebuildOptions();
        ApplyFilters();
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CountText));
    }

    private void RebuildOptions()
    {
        void Fill(ObservableCollection<string> target, IEnumerable<string> values)
        {
            var current = target.Count > 0 ? target[0] : AllOption;
            target.Clear();
            target.Add(AllOption);
            foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().OrderBy(v => v, StringComparer.Ordinal))
            {
                target.Add(value);
            }
        }

        Fill(AgentOptions, _all.Select(r => r.Agency));
        Fill(StatusOptions, _all.Select(r => r.StatusText));
        Fill(CraftTypeOptions, _all.Select(r => r.CraftType));
        Fill(AirlineOptions, _all.Select(r => r.AirlineCode));

        if (!AgentOptions.Contains(Agent)) Agent = AllOption;
        if (!StatusOptions.Contains(Status)) Status = AllOption;
        if (!CraftTypeOptions.Contains(CraftType)) CraftType = AllOption;
        if (!AirlineOptions.Contains(Airline)) Airline = AllOption;
    }

    private void SetFilter(ref string field, string? value, [CallerMemberName] string? propertyName = null)
    {
        var normalized = value ?? "";
        if (string.Equals(field, normalized, StringComparison.Ordinal)) return;
        field = normalized;
        OnPropertyChanged(propertyName);
        ApplyFilters();
    }

    public void ResetFilters()
    {
        _suppressFilter = true;
        try
        {
            Keyword = ""; FlightNo = ""; Agent = AllOption; Direction = AllOption;
            Origin = ""; Dest = ""; DateFrom = null; DateTo = null; Status = AllOption;
            TimeFrom = ""; TimeTo = ""; CraftType = AllOption; Airline = AllOption; CraftNo = "";
        }
        finally
        {
            _suppressFilter = false;
        }
        ApplyFilters();
    }

    public void ApplyFilters()
    {
        if (_suppressFilter) return;

        var filtered = _all.Where(Matches).ToList();
        filtered.Sort(CompareRecords);
        Rows = filtered;

        if (SelectedFlight is not null && !filtered.Contains(SelectedFlight))
        {
            SelectedFlight = null;
        }
        OnPropertyChanged(nameof(CountText));
    }

    private bool Matches(FlightRecord r)
    {
        if (!string.IsNullOrWhiteSpace(Keyword))
        {
            var k = Keyword.Trim();
            var hit = Contains(r.FlightInfo, k) || Contains(r.FlightNo, k) || Contains(r.OriginCn, k) ||
                      Contains(r.DestCn, k) || Contains(r.AirrouteCn, k) || Contains(r.CraftNo, k) ||
                      Contains(r.CraftType, k) || Contains(r.Agency, k) || Contains(r.Stand, k);
            if (!hit) return false;
        }

        if (!string.IsNullOrWhiteSpace(FlightNo))
        {
            if (!Contains(r.FlightNo, FlightNo.Trim()) && !Contains(r.FlightInfo, FlightNo.Trim())) return false;
        }

        if (!IsAll(Agent) && !string.Equals(r.Agency, Agent, StringComparison.Ordinal)) return false;

        if (!IsAll(Direction))
        {
            var expected = Direction == "进港" ? "A" : "D";
            if (!string.Equals(r.IoAttr, expected, StringComparison.OrdinalIgnoreCase)) return false;
        }

        if (!string.IsNullOrWhiteSpace(Origin) && !Contains(r.OriginCn, Origin.Trim())) return false;
        if (!string.IsNullOrWhiteSpace(Dest) && !Contains(r.DestCn, Dest.Trim())) return false;

        if (DateFrom is { } from)
        {
            if (r.ExecDateValue is not { } date || date.Date < from.Date) return false;
        }
        if (DateTo is { } to)
        {
            if (r.ExecDateValue is not { } date || date.Date > to.Date) return false;
        }

        if (!IsAll(Status) && !string.Equals(r.StatusText, Status, StringComparison.Ordinal)) return false;
        if (!IsAll(CraftType) && !string.Equals(r.CraftType, CraftType, StringComparison.OrdinalIgnoreCase)) return false;
        if (!IsAll(Airline) && !string.Equals(r.AirlineCode, Airline, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(CraftNo) && !Contains(r.CraftNo, CraftNo.Trim())) return false;

        return MatchesTimeWindow(r);
    }

    private bool MatchesTimeWindow(FlightRecord r)
    {
        var hasFrom = TryParseTime(TimeFrom, out var from);
        var hasTo = TryParseTime(TimeTo, out var to);
        if (!hasFrom && !hasTo) return true;
        if (r.ScheDepTime is not { } time) return false;

        if (hasFrom && hasTo)
        {
            return from <= to ? time >= from && time <= to : time >= from || time <= to;
        }
        return hasFrom ? time >= from : time <= to;
    }

    private static bool TryParseTime(string? text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var raw = text.Trim().Replace('：', ':');
        if (raw.Length == 4 && raw[1] == ':') raw = "0" + raw;   // 9:05 -> 09:05
        return TimeSpan.TryParseExact(raw, @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out value)
               || TimeSpan.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private int CompareRecords(FlightRecord a, FlightRecord b)
    {
        var result = SortKey switch
        {
            "exec_date" => string.Compare(a.ExecDate, b.ExecDate, StringComparison.Ordinal),
            "sche_dep" => Nullable.Compare(a.ScheDepTime, b.ScheDepTime),
            "actl_dep" => Nullable.Compare(a.ActlDepTime, b.ActlDepTime),
            "sche_arr" => Nullable.Compare(a.ScheArrTime, b.ScheArrTime),
            "actl_arr" => Nullable.Compare(a.ActlArrTime, b.ActlArrTime),
            "flight_info" => string.Compare(a.FlightInfo, b.FlightInfo, StringComparison.OrdinalIgnoreCase),
            "origin" => string.Compare(a.OriginCn, b.OriginCn, StringComparison.OrdinalIgnoreCase),
            "dest" => string.Compare(a.DestCn, b.DestCn, StringComparison.OrdinalIgnoreCase),
            "status" => string.Compare(a.StatusText, b.StatusText, StringComparison.OrdinalIgnoreCase),
            "craft_type" => string.Compare(a.CraftType, b.CraftType, StringComparison.OrdinalIgnoreCase),
            "craft_no" => string.Compare(a.CraftNo, b.CraftNo, StringComparison.OrdinalIgnoreCase),
            "route" => string.Compare(a.RouteAndStations, b.RouteAndStations, StringComparison.OrdinalIgnoreCase),
            "agency" => string.Compare(a.Agency, b.Agency, StringComparison.OrdinalIgnoreCase),
            _ => 0
        };

        if (SortDescending) result = -result;
        if (result == 0) result = string.Compare(a.ExecDate, b.ExecDate, StringComparison.Ordinal);
        if (result == 0) result = Nullable.Compare(a.ScheDepTime, b.ScheDepTime);
        if (result == 0) result = string.Compare(a.FlightInfo, b.FlightInfo, StringComparison.OrdinalIgnoreCase);
        return result;
    }

    // ---------------- 刷新 ----------------
    public async Task RefreshAsync(bool manual)
    {
        if (IsBusy) return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = "正在连接郑州机场数据源…";
        Log.Info(manual ? "手动刷新开始" : "自动刷新开始");

        try
        {
            using var client = new ZzcargoClient();
            var progress = new Progress<FetchProgress>(p => StatusMessage = p.Message);
            var result = await client.FetchAllAsync(progress, token);

            StatusMessage = "正在写入本地数据库…";
            var upsert = _store.Upsert(result.Records);
            _store.SetLastRefreshUtc(DateTime.UtcNow);
            var purged = _store.Purge(Settings.RetentionDays);

            LoadFromStore();
            _lastRefreshLocal = DateTime.Now;
            OnPropertyChanged(nameof(LastRefreshText));
            _nextRefreshAt = DateTime.Now.Add(Settings.RefreshInterval);

            StatusMessage = $"刷新完成：{result.Records.Count} 条，新增 {upsert.Inserted}，状态变化 {upsert.Changed}，用时 {result.Elapsed.TotalSeconds:F1} 秒";
            Log.Info($"刷新完成：{result.Records.Count} 条 / {result.Pages} 页 / {result.Elapsed.TotalSeconds:F1}s / 新增 {upsert.Inserted} / 变化 {upsert.Changed} / 清理 {purged}");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消本次刷新";
            _nextRefreshAt = DateTime.Now.AddMinutes(1);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "刷新失败，继续显示本地已保存数据";
            _nextRefreshAt = DateTime.Now.AddMinutes(5);
            Log.Error("刷新失败", ex);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(NextRefreshText));
            OnPropertyChanged(nameof(CountText));
        }
    }

    public void CancelRefresh() => _cts?.Cancel();

    private void OnTimerTick()
    {
        ClockText = DateTime.Now.ToString("HH:mm:ss");
        OnPropertyChanged(nameof(NextRefreshText));

        if (_nextRefreshAt is { } next && DateTime.Now >= next && !IsBusy)
        {
            _ = RefreshAsync(manual: false);
        }
    }

    public List<FlightRecord> BuildExportRecords(ExportScope scope, DateTime? from, DateTime? to)
    {
        IEnumerable<FlightRecord> source = scope switch
        {
            ExportScope.Filtered => Rows,
            ExportScope.All => _all,
            _ => _all.Where(r => r.ExecDateValue is { } d &&
                                 (from is null || d.Date >= from.Value.Date) &&
                                 (to is null || d.Date <= to.Value.Date))
        };

        var list = source.ToList();
        list.Sort((a, b) =>
        {
            var c = string.Compare(a.ExecDate, b.ExecDate, StringComparison.Ordinal);
            if (c == 0) c = Nullable.Compare(a.ScheDepTime, b.ScheDepTime);
            if (c == 0) c = string.Compare(a.FlightInfo, b.FlightInfo, StringComparison.OrdinalIgnoreCase);
            return c;
        });
        return list;
    }

    public IEnumerable<string> FilterSummary()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Keyword)) parts.Add($"关键字“{Keyword}”");
        if (!string.IsNullOrWhiteSpace(FlightNo)) parts.Add($"航班号“{FlightNo}”");
        if (!IsAll(Agent)) parts.Add($"代理“{Agent}”");
        if (!IsAll(Direction)) parts.Add(Direction);
        if (!string.IsNullOrWhiteSpace(Origin)) parts.Add($"始发“{Origin}”");
        if (!string.IsNullOrWhiteSpace(Dest)) parts.Add($"目的“{Dest}”");
        if (DateFrom is { } f) parts.Add($"从 {f:yyyy-MM-dd}");
        if (DateTo is { } t) parts.Add($"到 {t:yyyy-MM-dd}");
        if (!IsAll(Status)) parts.Add($"状态“{Status}”");
        if (!string.IsNullOrWhiteSpace(TimeFrom) || !string.IsNullOrWhiteSpace(TimeTo))
            parts.Add($"计划时段 {TimeFrom}-{TimeTo}");
        if (!IsAll(CraftType)) parts.Add($"机型“{CraftType}”");
        if (!IsAll(Airline)) parts.Add($"航司“{Airline}”");
        if (!string.IsNullOrWhiteSpace(CraftNo)) parts.Add($"机号“{CraftNo}”");
        return parts;
    }

    private static bool Contains(string source, string value) =>
        source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static bool IsAll(string value) => string.IsNullOrEmpty(value) || value == AllOption;

    public void Dispose()
    {
        _timer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum ExportScope
{
    Filtered,
    All,
    DateRange
}
