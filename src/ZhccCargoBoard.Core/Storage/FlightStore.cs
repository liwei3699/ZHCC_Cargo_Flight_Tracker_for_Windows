using Microsoft.Data.Sqlite;
using ZhccCargoBoard.Core.Models;

namespace ZhccCargoBoard.Core.Storage;

public sealed record UpsertResult(int Inserted, int Changed, int Unchanged);

public sealed record StoreStatistics(int Total, string? EarliestDate, string? LatestDate, DateTime? LastRefreshUtc);

/// <summary>本地 SQLite 存储：累积保存抓取到的航班动态，并记录状态变化。</summary>
public sealed class FlightStore : IDisposable
{
    private static readonly (string Name, Func<FlightRecord, object?> Get)[] Columns =
    {
        ("ffid", r => r.Ffid),
        ("record_id", r => r.RecordId),
        ("flight_id", r => r.FlightId),
        ("flight_no", r => r.FlightNo),
        ("airline_code", r => r.AirlineCode),
        ("agency_cd", r => r.AgencyCd),
        ("origin_cn", r => r.OriginCn),
        ("dest_cn", r => r.DestCn),
        ("origin_iata", r => r.OriginIata),
        ("dest_iata", r => r.DestIata),
        ("airroute_cn", r => r.AirrouteCn),
        ("airroute_iata", r => r.AirrouteIata),
        ("airroute_icao", r => r.AirrouteIcao),
        ("exec_date", r => r.ExecDate),
        ("sche_dep", r => r.ScheDep),
        ("actl_dep", r => r.ActlDep),
        ("sche_arr", r => r.ScheArr),
        ("actl_arr", r => r.ActlArr),
        ("estm_dep", r => r.EstmDep),
        ("estm_arr", r => r.EstmArr),
        ("near_sche_time", r => r.NearScheTime),
        ("near_actl_time", r => r.NearActlTime),
        ("near_station_cn", r => r.NearStationCn),
        ("io_attr", r => r.IoAttr),
        ("status_code", r => r.StatusCode),
        ("status_cn", r => r.StatusCn),
        ("abnormal_state", r => r.AbnormalState),
        ("abnormal_state_cn", r => r.AbnormalStateCn),
        ("abnormal_reason", r => r.AbnormalReason),
        ("craft_type", r => r.CraftType),
        ("craft_no", r => r.CraftNo),
        ("stand", r => r.Stand),
        ("last_stand", r => r.LastStand),
        ("terminal", r => r.Terminal),
        ("gate", r => r.Gate),
        ("carousel", r => r.Carousel),
        ("agency", r => r.Agency),
        ("region_attr", r => r.RegionAttr),
        ("region_cn", r => r.RegionCn),
        ("flight_task", r => r.FlightTask),
        ("flight_task_cn", r => r.FlightTaskCn),
        ("share_flight", r => r.ShareFlight),
        ("alt_station_cn", r => r.AltStationCn),
        ("is_remote", r => r.IsRemote),
        ("is_ended", r => r.IsEnded),
        ("plan_batch_date", r => r.PlanBatchDate),
        ("record_time", r => r.RecordTime),
        ("raw_json", r => r.RawJson),
        ("content_hash", r => r.ContentFingerprint())
    };

    private static readonly string InsertSql = BuildInsertSql();
    private static readonly string SelectSql = BuildSelectSql();

    private SqliteConnection? _connection;

    public FlightStore(string? databasePath = null)
    {
        DatabasePath = databasePath ?? AppPaths.DatabaseFile;
    }

    public string DatabasePath { get; }

    public SqliteConnection Connection => _connection ?? throw new InvalidOperationException("数据库尚未初始化。");

    public void Initialize()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(DatabasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString());
        _connection.Open();

        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA synchronous=NORMAL;");
        Execute("""
            CREATE TABLE IF NOT EXISTS flight (
                ffid TEXT PRIMARY KEY,
                record_id TEXT, flight_id TEXT, flight_no TEXT, airline_code TEXT, agency_cd TEXT,
                origin_cn TEXT, dest_cn TEXT, origin_iata TEXT, dest_iata TEXT,
                airroute_cn TEXT, airroute_iata TEXT, airroute_icao TEXT,
                exec_date TEXT, sche_dep TEXT, actl_dep TEXT, sche_arr TEXT, actl_arr TEXT,
                estm_dep TEXT, estm_arr TEXT, near_sche_time TEXT, near_actl_time TEXT, near_station_cn TEXT,
                io_attr TEXT, status_code TEXT, status_cn TEXT,
                abnormal_state TEXT, abnormal_state_cn TEXT, abnormal_reason TEXT,
                craft_type TEXT, craft_no TEXT, stand TEXT, last_stand TEXT, terminal TEXT, gate TEXT, carousel TEXT,
                agency TEXT, region_attr TEXT, region_cn TEXT, flight_task TEXT, flight_task_cn TEXT,
                share_flight TEXT, alt_station_cn TEXT, is_remote TEXT, is_ended TEXT,
                plan_batch_date TEXT, record_time TEXT, raw_json TEXT,
                content_hash TEXT,
                first_seen TEXT NOT NULL, last_seen TEXT NOT NULL,
                status_changed_at TEXT, change_count INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS ix_flight_exec_date ON flight(exec_date);
            CREATE INDEX IF NOT EXISTS ix_flight_status ON flight(status_code);
            CREATE INDEX IF NOT EXISTS ix_flight_airline ON flight(airline_code);
            CREATE INDEX IF NOT EXISTS ix_flight_last_seen ON flight(last_seen);
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT);
            """);
    }

    public UpsertResult Upsert(IEnumerable<FlightRecord> records, DateTime? nowUtc = null)
    {
        var now = (nowUtc ?? DateTime.UtcNow).ToString("o");
        var batch = records
            .Where(r => !string.IsNullOrWhiteSpace(r.Ffid))
            .GroupBy(r => r.Ffid, StringComparer.Ordinal)
            .Select(g => g.Last())
            .ToList();
        if (batch.Count == 0)
        {
            return new UpsertResult(0, 0, 0);
        }

        var existing = LoadHashes(batch.Select(r => r.Ffid).ToList());
        var inserted = 0;
        var changed = 0;
        var unchanged = 0;
        foreach (var record in batch)
        {
            var hash = record.ContentFingerprint();
            if (!existing.TryGetValue(record.Ffid, out var oldHash))
            {
                inserted++;
            }
            else if (!string.Equals(oldHash, hash, StringComparison.Ordinal))
            {
                changed++;
            }
            else
            {
                unchanged++;
            }
        }

        using var transaction = Connection.BeginTransaction();
        using var command = Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = InsertSql;
        var parameters = new SqliteParameter[Columns.Length + 4];
        for (var i = 0; i < Columns.Length; i++)
        {
            parameters[i] = command.CreateParameter();
            parameters[i].ParameterName = "@c" + i;
            command.Parameters.Add(parameters[i]);
        }
        var firstSeen = command.CreateParameter();
        firstSeen.ParameterName = "@first_seen";
        command.Parameters.Add(firstSeen);
        var lastSeen = command.CreateParameter();
        lastSeen.ParameterName = "@last_seen";
        command.Parameters.Add(lastSeen);
        var changedAt = command.CreateParameter();
        changedAt.ParameterName = "@status_changed_at";
        command.Parameters.Add(changedAt);
        var changeInc = command.CreateParameter();
        changeInc.ParameterName = "@change_inc";
        command.Parameters.Add(changeInc);

        foreach (var record in batch)
        {
            for (var i = 0; i < Columns.Length; i++)
            {
                parameters[i].Value = Columns[i].Get(record) ?? "";
            }
            var isChange = existing.TryGetValue(record.Ffid, out var oldHash)
                           && !string.Equals(oldHash, record.ContentFingerprint(), StringComparison.Ordinal);
            firstSeen.Value = now;
            lastSeen.Value = now;
            changedAt.Value = isChange ? now : DBNull.Value;
            changeInc.Value = isChange ? 1 : 0;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        ApplySeenTimes(batch, now);

        return new UpsertResult(inserted, changed, unchanged);
    }

    private static void ApplySeenTimes(List<FlightRecord> batch, string now)
    {
        // 便于调用方在导出前知道时间；数据库里的 first_seen/last_seen 由 SQL 维护。
        var parsed = DateTime.Parse(now, null, System.Globalization.DateTimeStyles.RoundtripKind);
        foreach (var record in batch)
        {
            if (record.FirstSeen == default)
            {
                record.FirstSeen = parsed;
            }
            record.LastSeen = parsed;
        }
    }

    private Dictionary<string, string> LoadHashes(List<string> ffids)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        const int chunkSize = 400;
        for (var offset = 0; offset < ffids.Count; offset += chunkSize)
        {
            var chunk = ffids.Skip(offset).Take(chunkSize).ToList();
            using var command = Connection.CreateCommand();
            var names = new string[chunk.Count];
            for (var i = 0; i < chunk.Count; i++)
            {
                names[i] = "@p" + i;
                command.Parameters.AddWithValue(names[i], chunk[i]);
            }
            command.CommandText = $"SELECT ffid, content_hash FROM flight WHERE ffid IN ({string.Join(',', names)})";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result[reader.GetString(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1);
            }
        }
        return result;
    }

    /// <summary>读取本地已累积的记录；日期为空表示不限。</summary>
    public List<FlightRecord> LoadAll(string? fromDate = null, string? toDate = null)
    {
        var list = new List<FlightRecord>();
        using var command = Connection.CreateCommand();
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(fromDate))
        {
            where.Add("exec_date >= @from");
            command.Parameters.AddWithValue("@from", fromDate);
        }
        if (!string.IsNullOrWhiteSpace(toDate))
        {
            where.Add("exec_date <= @to");
            command.Parameters.AddWithValue("@to", toDate);
        }
        command.CommandText = SelectSql + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(ReadRecord(reader));
        }
        return list;
    }

    private static FlightRecord ReadRecord(SqliteDataReader r)
    {
        string S(string name)
        {
            var i = r.GetOrdinal(name);
            return r.IsDBNull(i) ? "" : r.GetString(i);
        }

        DateTime? D(string name)
        {
            var i = r.GetOrdinal(name);
            if (r.IsDBNull(i)) return null;
            return DateTime.TryParse(r.GetString(i), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
        }

        return new FlightRecord
        {
            Ffid = S("ffid"),
            RecordId = S("record_id"),
            FlightId = S("flight_id"),
            FlightNo = S("flight_no"),
            AirlineCode = S("airline_code"),
            AgencyCd = S("agency_cd"),
            OriginCn = S("origin_cn"),
            DestCn = S("dest_cn"),
            OriginIata = S("origin_iata"),
            DestIata = S("dest_iata"),
            AirrouteCn = S("airroute_cn"),
            AirrouteIata = S("airroute_iata"),
            AirrouteIcao = S("airroute_icao"),
            ExecDate = S("exec_date"),
            ScheDep = S("sche_dep"),
            ActlDep = S("actl_dep"),
            ScheArr = S("sche_arr"),
            ActlArr = S("actl_arr"),
            EstmDep = S("estm_dep"),
            EstmArr = S("estm_arr"),
            NearScheTime = S("near_sche_time"),
            NearActlTime = S("near_actl_time"),
            NearStationCn = S("near_station_cn"),
            IoAttr = S("io_attr"),
            StatusCode = S("status_code"),
            StatusCn = S("status_cn"),
            AbnormalState = S("abnormal_state"),
            AbnormalStateCn = S("abnormal_state_cn"),
            AbnormalReason = S("abnormal_reason"),
            CraftType = S("craft_type"),
            CraftNo = S("craft_no"),
            Stand = S("stand"),
            LastStand = S("last_stand"),
            Terminal = S("terminal"),
            Gate = S("gate"),
            Carousel = S("carousel"),
            Agency = S("agency"),
            RegionAttr = S("region_attr"),
            RegionCn = S("region_cn"),
            FlightTask = S("flight_task"),
            FlightTaskCn = S("flight_task_cn"),
            ShareFlight = S("share_flight"),
            AltStationCn = S("alt_station_cn"),
            IsRemote = S("is_remote"),
            IsEnded = S("is_ended"),
            PlanBatchDate = S("plan_batch_date"),
            RecordTime = S("record_time"),
            FirstSeen = D("first_seen") ?? default,
            LastSeen = D("last_seen") ?? default,
            StatusChangedAt = D("status_changed_at")
        };
    }

    public StoreStatistics GetStatistics()
    {
        using var command = Connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MIN(exec_date), MAX(exec_date) FROM flight";
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                var total = reader.GetInt32(0);
                var min = reader.IsDBNull(1) ? null : reader.GetString(1);
                var max = reader.IsDBNull(2) ? null : reader.GetString(2);
                return new StoreStatistics(total, min, max, GetLastRefreshUtc());
            }
        }
        return new StoreStatistics(0, null, null, GetLastRefreshUtc());
    }

    /// <summary>清理超过保留天数没有再次出现的记录，返回删除条数。</summary>
    public int Purge(int retentionDays, DateTime? nowUtc = null)
    {
        var cutoff = (nowUtc ?? DateTime.UtcNow).AddDays(-Math.Max(1, retentionDays)).ToString("o");
        using var command = Connection.CreateCommand();
        command.CommandText = "DELETE FROM flight WHERE last_seen < @cutoff";
        command.Parameters.AddWithValue("@cutoff", cutoff);
        return command.ExecuteNonQuery();
    }

    public DateTime? GetLastRefreshUtc()
    {
        var raw = GetMeta("last_refresh_utc");
        return DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var value) ? value : null;
    }

    public void SetLastRefreshUtc(DateTime utc)
    {
        SetMeta("last_refresh_utc", utc.ToUniversalTime().ToString("o"));
    }

    public string? GetMeta(string key)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = @k";
        command.Parameters.AddWithValue("@k", key);
        return command.ExecuteScalar() as string;
    }

    public void SetMeta(string key, string value)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = "INSERT INTO meta(key, value) VALUES(@k, @v) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        command.Parameters.AddWithValue("@k", key);
        command.Parameters.AddWithValue("@v", value);
        command.ExecuteNonQuery();
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string BuildInsertSql()
    {
        var names = Columns.Select(c => c.Name).ToList();
        var insertCols = string.Join(", ", names.Append("first_seen").Append("last_seen"));
        var insertValues = string.Join(", ", Enumerable.Range(0, Columns.Length).Select(i => "@c" + i)
            .Append("@first_seen").Append("@last_seen"));

        // 只更新除 first_seen 外的列；状态变化时记录时间并累加次数。
        var updates = new List<string>
        {
            "last_seen = excluded.last_seen",
            "change_count = change_count + @change_inc",
            "status_changed_at = COALESCE(@status_changed_at, status_changed_at)"
        };
        updates.AddRange(names.Where(n => n != "ffid").Select(n => $"{n} = excluded.{n}"));
        var updateSql = string.Join(",\n    ", updates);
        return $"""
            INSERT INTO flight ({insertCols})
            VALUES ({insertValues})
            ON CONFLICT(ffid) DO UPDATE SET
                {updateSql};
            """;
    }

    private static string BuildSelectSql()
    {
        var names = string.Join(", ", Columns.Select(c => c.Name));
        return $"SELECT {names}, first_seen, last_seen, status_changed_at FROM flight";
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }
}
