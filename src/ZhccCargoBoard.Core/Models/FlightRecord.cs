using System.Globalization;
using System.Text.Json;

namespace ZhccCargoBoard.Core.Models;

/// <summary>一条航班动态记录（对应网站 <c>flightDynamic/skipTo</c> 返回的 records 元素）。</summary>
public sealed class FlightRecord
{
    // ---- 航班标识 ----
    public string Ffid { get; set; } = "";          // CZ-6376-20261105005000-A
    public string RecordId { get; set; } = "";      // id
    public string FlightId { get; set; } = "";      // flightId / dataKey
    public string FlightNo { get; set; } = "";      // 6376
    public string AirlineCode { get; set; } = "";   // CZ
    public string AgencyCd { get; set; } = "";      // CZ

    // ---- 航线 ----
    public string OriginCn { get; set; } = "";      // 乌鲁木齐
    public string DestCn { get; set; } = "";        // 郑州
    public string OriginIata { get; set; } = "";    // URC
    public string DestIata { get; set; } = "";      // CGO
    public string AirrouteCn { get; set; } = "";    // 乌鲁木齐-郑州
    public string AirrouteIata { get; set; } = "";  // URC-CGO
    public string AirrouteIcao { get; set; } = "";  // ZWWW-ZHCC

    // ---- 时刻 ----
    public string ExecDate { get; set; } = "";      // 2026-11-05
    public string ScheDep { get; set; } = "";       // 21:00
    public string ActlDep { get; set; } = "";       // 21:12
    public string ScheArr { get; set; } = "";       // 00:50
    public string ActlArr { get; set; } = "";       // 00:40
    public string EstmDep { get; set; } = "";       // 2026-11-05 21:00
    public string EstmArr { get; set; } = "";       // 2026-11-05 00:35
    public string NearScheTime { get; set; } = "";  // 2026-11-04 21:00
    public string NearActlTime { get; set; } = "";  // 2026-11-04 21:12
    public string NearStationCn { get; set; } = ""; // 乌鲁木齐

    // ---- 状态 ----
    public string IoAttr { get; set; } = "";        // A=进港 D=出港
    public string StatusCode { get; set; } = "";    // ARR/DEP
    public string StatusCn { get; set; } = "";      // 到达/起飞/空
    public string AbnormalState { get; set; } = "";
    public string AbnormalStateCn { get; set; } = "";
    public string AbnormalReason { get; set; } = "";

    // ---- 机与位 ----
    public string CraftType { get; set; } = "";     // B738
    public string CraftNo { get; set; } = "";       // B1400
    public string Stand { get; set; } = "";         // 223
    public string LastStand { get; set; } = "";
    public string Terminal { get; set; } = "";      // T2
    public string Gate { get; set; } = "";
    public string Carousel { get; set; } = "";      // 行李转盘

    // ---- 业务属性 ----
    public string Agency { get; set; } = "";        // 南航 / 机场
    public string RegionAttr { get; set; } = "";    // DOM/INT/REG/MIX
    public string RegionCn { get; set; } = "";      // 国内/国际
    public string FlightTask { get; set; } = "";    // W/Z
    public string FlightTaskCn { get; set; } = "";  // 正班/货班
    public string ShareFlight { get; set; } = "";
    public string AltStationCn { get; set; } = "";
    public string IsRemote { get; set; } = "";
    public string IsEnded { get; set; } = "";
    public string PlanBatchDate { get; set; } = "";
    public string RecordTime { get; set; } = "";

    // ---- 元数据 ----
    public string RawJson { get; set; } = "";
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public DateTime? StatusChangedAt { get; set; }

    // ---- 派生显示 ----
    public string FlightInfo => AirlineCode + FlightNo;

    /// <summary>首次记录时间（本地时区，存储的是 UTC）。</summary>
    public DateTime FirstSeenLocal => ToLocal(FirstSeen);

    /// <summary>最近一次在数据源中出现的时间（本地时区）。</summary>
    public DateTime LastSeenLocal => ToLocal(LastSeen);

    private static DateTime ToLocal(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value.ToLocalTime()
        : DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime();
    public string RouteText => $"{OriginCn} → {DestCn}";
    public string StatusText => string.IsNullOrWhiteSpace(StatusCn) ? "计划" : StatusCn;
    public string DirectionText => IoAttr switch { "A" => "进港", "D" => "出港", _ => "" };
    public string RegionText => RegionAttr switch
    {
        "DOM" => "国内",
        "INT" => "国际",
        "REG" => "地区",
        "MIX" => "混合",
        _ => RegionCn
    };
    public string RouteAndStations => AirrouteCn.Length > 0 ? AirrouteCn : RouteText;

    public DateTime? ExecDateValue => DateTime.TryParseExact(
        ExecDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public TimeSpan? ScheDepTime => ParseTime(ScheDep);
    public TimeSpan? ActlDepTime => ParseTime(ActlDep);
    public TimeSpan? ScheArrTime => ParseTime(ScheArr);
    public TimeSpan? ActlArrTime => ParseTime(ActlArr);

    private static TimeSpan? ParseTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return TimeSpan.TryParse(raw.Trim(), CultureInfo.InvariantCulture, out var t) ? t : null;
    }

    /// <summary>用于判断一条记录是否有实质变化的指纹。</summary>
    public string ContentFingerprint()
    {
        return string.Join('|',
            StatusCode, StatusCn, ScheDep, ActlDep, ScheArr, ActlArr,
            EstmDep, EstmArr, Stand, Terminal, Gate, AbnormalStateCn, IsEnded);
    }

    public static FlightRecord FromJson(JsonElement e, DateTime now)
    {
        var r = new FlightRecord
        {
            Ffid = Str(e, "ffid"),
            RecordId = Str(e, "id"),
            FlightId = Str(e, "flightId"),
            FlightNo = Str(e, "flightNo"),
            AirlineCode = Str(e, "airlinesIataCd"),
            AgencyCd = Str(e, "agencyCd"),
            OriginCn = Str(e, "startStationCn"),
            DestCn = Str(e, "terminalStationCn"),
            OriginIata = Str(e, "startOpaIataCd"),
            DestIata = Str(e, "terminalOpaIataCd"),
            AirrouteCn = Str(e, "airrouteCn"),
            AirrouteIata = Str(e, "airrouteIataCd"),
            AirrouteIcao = Str(e, "airrouteIcaoCd"),
            ExecDate = Str(e, "execDate"),
            ScheDep = Str(e, "scheTakeOffTime"),
            ActlDep = Str(e, "actlTakeOffTime"),
            ScheArr = Str(e, "scheLandingTime"),
            ActlArr = Str(e, "actlLandingTime"),
            EstmDep = Str(e, "estmTakeOffTime"),
            EstmArr = Str(e, "estmLandingTime"),
            NearScheTime = Str(e, "nearSchetime"),
            NearActlTime = Str(e, "nearActltime"),
            NearStationCn = Str(e, "nearStationCn"),
            IoAttr = Str(e, "ioAttr"),
            StatusCode = Str(e, "normalState"),
            StatusCn = Str(e, "normalStateCn"),
            AbnormalState = Str(e, "abnormalState"),
            AbnormalStateCn = Str(e, "abnormalStateCn"),
            AbnormalReason = Str(e, "absreasonCn"),
            CraftType = Str(e, "craftType"),
            CraftNo = Str(e, "craftNo"),
            Stand = Str(e, "stand"),
            LastStand = Str(e, "lastStand"),
            Terminal = Str(e, "terminal"),
            Gate = Str(e, "gate"),
            Carousel = Str(e, "carousel"),
            Agency = Str(e, "agency"),
            RegionAttr = Str(e, "regionAttr"),
            RegionCn = Str(e, "attrForNodeTemplate"),
            FlightTask = Str(e, "flightTask"),
            FlightTaskCn = Str(e, "flightTaskCn"),
            ShareFlight = Str(e, "shareFlight"),
            AltStationCn = Str(e, "altStationCn"),
            IsRemote = Str(e, "isremote"),
            IsEnded = Str(e, "isEnded"),
            PlanBatchDate = Str(e, "planBatchDate"),
            RecordTime = Str(e, "recordTime"),
            RawJson = e.GetRawText(),
            FirstSeen = now,
            LastSeen = now
        };

        if (string.IsNullOrWhiteSpace(r.Ffid))
        {
            r.Ffid = string.IsNullOrWhiteSpace(r.RecordId)
                ? $"{r.FlightInfo}-{r.ExecDate}-{r.IoAttr}-{r.ScheDep}".Trim('-')
                : "id:" + r.RecordId;
        }
        return r;
    }

    private static string Str(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        };
    }

    public FlightRecord Clone() => (FlightRecord)MemberwiseClone();
}
