using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Collections;
using Avalonia.Threading;
using ZhccCargoBoard.Core;
using ZhccCargoBoard.Core.Models;
using ZhccCargoBoard.Core.Services;
using ZhccCargoBoard.Core.Storage;

namespace ZhccCargoBoard.Desktop.ViewModels;

/// <summary>板端只读查看器：抓取、筛选、排序交给 DataGrid，无导出/无托盘/无后台采集。</summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    public const string All = "全部";

    private readonly FlightStore _store;
    private readonly DispatcherTimer _timer;
    private List<FlightRecord> _all = new();

    private string _flightNo = "";
    private string _direction = All;
    private string _origin = All;
    private string _dest = All;
    private string _status = All;
    private string _craftType = All;
    private string _airline = All;
    private string _dateFilter = "全部日期";

    private bool _isBusy;
    private string _statusMessage = "就绪";
    private string? _errorMessage;
    private FlightRecord? _selectedFlight;
    private DateTime? _lastRefresh;
    private DateTime? _nextRefreshAt;
    private DataGridCollectionView _view;

    public MainViewModel(AppSettings settings, FlightStore store)
    {
        Settings = settings;
        _store = store;

        _view = CreateView(Array.Empty<FlightRecord>());

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnTick();
    }

    public AppSettings Settings { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DataGridCollectionView View
    {
        get => _view;
        private set
        {
            _view = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibleCount));
            OnPropertyChanged(nameof(CountText));
        }
    }

    public ObservableCollection<string> DirectionOptions { get; } = new() { All, "进港", "出港" };
    public ObservableCollection<string> OriginOptions { get; } = new() { All };
    public ObservableCollection<string> DestOptions { get; } = new() { All };
    public ObservableCollection<string> StatusOptions { get; } = new() { All };
    public ObservableCollection<string> CraftTypeOptions { get; } = new() { All };
    public ObservableCollection<string> AirlineOptions { get; } = new() { All };
    public ObservableCollection<string> DateOptions { get; } = new() { "全部日期", "今天", "明天", "后天" };

    // ---------------- 筛选条件 ----------------
    public string FlightNo
    {
        get => _flightNo;
        set => SetFilter(ref _flightNo, value);
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

    public string Status
    {
        get => _status;
        set => SetFilter(ref _status, value);
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

    public string DateFilter
    {
        get => _dateFilter;
        set => SetFilter(ref _dateFilter, value);
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
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public int TotalCount => _all.Count;

    public int VisibleCount => View.Count;

    public string CountText => $"共 {TotalCount} 条 · 显示 {VisibleCount} 条";

    public string LastRefreshText => _lastRefresh is { } t ? $"更新于 {t:HH:mm}" : "尚未刷新";

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

    // ---------------- 生命周期 ----------------
    public void Initialize()
    {
        LoadFromStore();
        _lastRefresh = _store.GetLastRefreshUtc()?.ToLocalTime();
        _nextRefreshAt = DateTime.Now.Add(Settings.RefreshInterval);
        _timer.Start();
        OnTick();
        OnPropertyChanged(nameof(LastRefreshText));

        // 只读查看器：打开即抓一次（历史只有运行期间才会有）
        _ = RefreshAsync();
    }

    public void Dispose()
    {
        _timer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void LoadFromStore()
    {
        _all = _store.LoadAll();
        RebuildOptions();
        View = CreateView(_all);
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(VisibleCount));
    }

    private DataGridCollectionView CreateView(IEnumerable<FlightRecord> source)
    {
        var view = new DataGridCollectionView(source)
        {
            Filter = o => o is FlightRecord r && Matches(r)
        };
        view.SortDescriptions.Add(DataGridSortDescription.FromPath(
            nameof(FlightRecord.ExecDate), ListSortDirection.Ascending));
        view.SortDescriptions.Add(DataGridSortDescription.FromPath(
            nameof(FlightRecord.ScheDep), ListSortDirection.Ascending));
        return view;
    }

    private void RebuildOptions()
    {
        var origin = Origin;
        var dest = Dest;
        var status = Status;
        var craftType = CraftType;
        var airline = Airline;

        Fill(OriginOptions, _all.Select(r => r.OriginCn));
        Fill(DestOptions, _all.Select(r => r.DestCn));
        Fill(StatusOptions, _all.Select(r => r.StatusText));
        Fill(CraftTypeOptions, _all.Select(r => r.CraftType));
        Fill(AirlineOptions, _all.Select(r => r.AirlineCode));

        // 列表被 Clear 时 ComboBox 的选中项会被清空，这里无条件重写回 VM 并通知，
        // 否则会出现"下拉框空白但筛选值其实是全部"的假象。
        Restore(ref _origin, OriginOptions, origin, nameof(Origin));
        Restore(ref _dest, DestOptions, dest, nameof(Dest));
        Restore(ref _status, StatusOptions, status, nameof(Status));
        Restore(ref _craftType, CraftTypeOptions, craftType, nameof(CraftType));
        Restore(ref _airline, AirlineOptions, airline, nameof(Airline));

        static void Fill(ObservableCollection<string> target, IEnumerable<string> values)
        {
            var desired = new List<string> { All };
            desired.AddRange(values.Where(x => !string.IsNullOrWhiteSpace(x))
                                   .Distinct()
                                   .OrderBy(x => x, StringComparer.Ordinal));

            // 增量同步，而不是 Clear()：Clear 会让 ComboBox 丢掉 SelectedItem，
            // 之后即使把集合补回来，绑定也不一定会重新选中。
            for (var i = target.Count - 1; i >= 0; i--)
            {
                if (!desired.Contains(target[i])) target.RemoveAt(i);
            }

            for (var i = 0; i < desired.Count; i++)
            {
                if (i < target.Count && string.Equals(target[i], desired[i], StringComparison.Ordinal))
                {
                    continue;
                }

                var existing = target.IndexOf(desired[i]);
                if (existing >= 0) target.Move(existing, i);
                else target.Insert(i, desired[i]);
            }
        }

        void Restore(ref string field, ObservableCollection<string> options, string? current, string name)
        {
            field = current is not null && options.Contains(current) ? current : All;
            OnPropertyChanged(name);
        }
    }

    private void SetFilter(ref string field, string? value, [CallerMemberName] string? propertyName = null)
    {
        var normalized = value ?? All;
        if (string.Equals(field, normalized, StringComparison.Ordinal)) return;
        field = normalized;
        OnPropertyChanged(propertyName);
        View.Refresh();
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(CountText));
    }

    private bool Matches(FlightRecord r)
    {
        if (!string.IsNullOrWhiteSpace(FlightNo))
        {
            var q = FlightNo.Trim();
            if (!Contains(r.FlightInfo, q) && !Contains(r.FlightNo, q)) return false;
        }

        if (!IsAll(Direction) && !string.Equals(r.DirectionText, Direction, StringComparison.Ordinal)) return false;
        if (!IsAll(Origin) && !string.Equals(r.OriginCn, Origin, StringComparison.Ordinal)) return false;
        if (!IsAll(Dest) && !string.Equals(r.DestCn, Dest, StringComparison.Ordinal)) return false;
        if (!IsAll(Status) && !string.Equals(r.StatusText, Status, StringComparison.Ordinal)) return false;
        if (!IsAll(CraftType) && !string.Equals(r.CraftType, CraftType, StringComparison.OrdinalIgnoreCase)) return false;
        if (!IsAll(Airline) && !string.Equals(r.AirlineCode, Airline, StringComparison.OrdinalIgnoreCase)) return false;

        if (DateFilter is "今天" or "明天" or "后天")
        {
            if (r.ExecDateValue is not { } date) return false;
            var offset = DateFilter switch { "今天" => 0, "明天" => 1, _ => 2 };
            if (date.Date != DateTime.Today.AddDays(offset)) return false;
        }

        return true;
    }

    public void ResetFilters()
    {
        _flightNo = "";
        _direction = All;
        _origin = All;
        _dest = All;
        _status = All;
        _craftType = All;
        _airline = All;
        _dateFilter = "全部日期";
        OnPropertyChanged(nameof(FlightNo));
        OnPropertyChanged(nameof(Direction));
        OnPropertyChanged(nameof(Origin));
        OnPropertyChanged(nameof(Dest));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(CraftType));
        OnPropertyChanged(nameof(Airline));
        OnPropertyChanged(nameof(DateFilter));
        View.Refresh();
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(CountText));
    }

    private bool IsAll(string value) => string.IsNullOrEmpty(value) || value == All;

    private static bool Contains(string source, string value) =>
        source.Contains(value, StringComparison.OrdinalIgnoreCase);

    // ---------------- 刷新 ----------------
    private CancellationTokenSource? _cts;

    public async Task RefreshAsync()
    {
        if (IsBusy) return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = "正在连接郑州机场数据源…";
        var started = DateTime.Now;

        try
        {
            using var client = new ZzcargoClient();
            var progress = new Progress<FetchProgress>(p => StatusMessage = p.Message);
            var result = await client.FetchAllAsync(progress, _cts.Token);

            StatusMessage = "正在写入本地数据库…";
            var upsert = _store.Upsert(result.Records);
            _store.SetLastRefreshUtc(DateTime.UtcNow);
            _store.Purge(Settings.RetentionDays);

            LoadFromStore();
            _lastRefresh = DateTime.Now;
            _nextRefreshAt = DateTime.Now.Add(Settings.RefreshInterval);
            OnPropertyChanged(nameof(LastRefreshText));
            OnPropertyChanged(nameof(NextRefreshText));

            StatusMessage = $"刷新完成：{result.Records.Count} 条，新增 {upsert.Inserted}，用时 {result.Elapsed.TotalSeconds:F1} 秒";
            Log.Info($"刷新完成：{result.Records.Count} 条 / {result.Pages} 页 / {result.Elapsed.TotalSeconds:F1}s / 新增 {upsert.Inserted}");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消本次刷新";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "刷新失败，保留上次数据";
            Log.Error("刷新失败", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnTick()
    {
        if (_nextRefreshAt is { } next)
        {
            if (DateTime.Now >= next && !IsBusy)
            {
                _ = RefreshAsync();
            }
            OnPropertyChanged(nameof(NextRefreshText));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
