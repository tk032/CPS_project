using MySqlConnector;

namespace SmartFactoryCPS.Services;

/// <summary>
/// MySQL CRUD 서비스
/// - 연결 실패 시 예외를 던지지 않고 false/null 반환 (PLC 제어에 영향 없음)
/// </summary>
public class DbService
{
    private readonly string _cs = DbConfig.ConnectionString;

    // ── 마지막 박스 ID 조회 ─────────────────────────────────────────
    public async Task<int> GetLastBoxIdAsync()
    {
        try
        {
            await using var conn = new MySqlConnection(_cs);
            await conn.OpenAsync();
            var cmd    = new MySqlCommand("SELECT COALESCE(MAX(box_id), 0) FROM parcel", conn);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch { return 0; }
    }

    // ── 연결 테스트 ─────────────────────────────────────────────────
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            await using var conn = new MySqlConnection(_cs);
            await conn.OpenAsync();
            return true;
        }
        catch { return false; }
    }

    // ── 박스 분류 완료 저장 ─────────────────────────────────────────
    /// <returns>생성된 parcel.id (실패 시 -1)</returns>
    public async Task<int> SaveParcelAsync(
        int boxId, int regionCode, string regionName,
        double weightKg, string sortResult = "OK")
    {
        try
        {
            await using var conn = new MySqlConnection(_cs);
            await conn.OpenAsync();

            var sql = @"INSERT INTO parcel
                            (box_id, region_code, region_name, weight_kg, sort_result)
                        VALUES (@boxId, @regionCode, @regionName, @weight, @result);
                        SELECT LAST_INSERT_ID();";

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@boxId",       boxId);
            cmd.Parameters.AddWithValue("@regionCode",  regionCode);
            cmd.Parameters.AddWithValue("@regionName",  regionName);
            cmd.Parameters.AddWithValue("@weight",      weightKg);
            cmd.Parameters.AddWithValue("@result",      sortResult);

            var id = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(id);
        }
        catch { return -1; }
    }

    // ── 분류 이벤트 로그 저장 ───────────────────────────────────────
    public async Task SaveSortEventAsync(
        int parcelId, string eventType, string? detail = null)
    {
        try
        {
            await using var conn = new MySqlConnection(_cs);
            await conn.OpenAsync();

            var sql = @"INSERT INTO sort_event (parcel_id, event_type, event_detail)
                        VALUES (@pid, @type, @detail)";

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@pid",    parcelId);
            cmd.Parameters.AddWithValue("@type",   eventType);
            cmd.Parameters.AddWithValue("@detail", (object?)detail ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* 로그 저장 실패는 무시 */ }
    }

    // ── 알람 저장 ───────────────────────────────────────────────────
    /// <returns>생성된 alarm_history.id (실패 시 -1)</returns>
    public async Task<int> SaveAlarmAsync(int alarmCode, string message)
    {
        try
        {
            await using var conn = new MySqlConnection(_cs);
            await conn.OpenAsync();

            var sql = @"INSERT INTO alarm_history (alarm_code, alarm_message)
                        VALUES (@code, @msg);
                        SELECT LAST_INSERT_ID();";

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@code", alarmCode);
            cmd.Parameters.AddWithValue("@msg",  message);

            var id = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(id);
        }
        catch { return -1; }
    }

    // ── 알람 해제 시간 기록 ─────────────────────────────────────────
    public async Task ResolveAlarmAsync(int alarmId)
    {
        try
        {
            await using var conn = new MySqlConnection(_cs);
            await conn.OpenAsync();

            var sql = "UPDATE alarm_history SET resolved_at=NOW() WHERE id=@id";
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", alarmId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }

    // ── 라인 상태 스냅샷 저장 ───────────────────────────────────────
    public async Task LogLineStatusAsync(
        bool isRunning, int totalProcessed, double throughput)
    {
        try
        {
            await using var conn = new MySqlConnection(_cs);
            await conn.OpenAsync();

            var sql = @"INSERT INTO line_status_log
                            (is_running, total_processed, throughput_per_min)
                        VALUES (@run, @total, @tput)";

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@run",   isRunning ? 1 : 0);
            cmd.Parameters.AddWithValue("@total", totalProcessed);
            cmd.Parameters.AddWithValue("@tput",  throughput);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }

    // ── 분류 이력 조회 (키워드 + 지역 필터) ────────────────────────
    public async Task<List<ParcelAdminRow>> GetParcelsAsync(
        string? search = null, string? region = null, int limit = 200)
    {
        var result = new List<ParcelAdminRow>();
        try
        {
            await using var conn = new MySqlConnection(_cs);
            await conn.OpenAsync();

            var conditions = new List<string>();
            if (!string.IsNullOrWhiteSpace(search))
                conditions.Add("(region_name LIKE @s OR CAST(box_id AS CHAR) LIKE @s)");
            if (!string.IsNullOrWhiteSpace(region))
                conditions.Add("region_name = @region");

            var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

            var sql = $@"SELECT id, box_id, region_name, weight_kg, sort_result, sorted_at
                         FROM parcel {where} ORDER BY id DESC LIMIT @limit";

            await using var cmd = new MySqlCommand(sql, conn);
            if (!string.IsNullOrWhiteSpace(search))
                cmd.Parameters.AddWithValue("@s",      $"%{search}%");
            if (!string.IsNullOrWhiteSpace(region))
                cmd.Parameters.AddWithValue("@region", region);
            cmd.Parameters.AddWithValue("@limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(new ParcelAdminRow
                {
                    Id         = reader.GetInt32(0),
                    BoxId      = reader.GetInt32(1),
                    RegionName = reader.GetString(2),
                    WeightKg   = reader.GetDouble(3),
                    SortResult = reader.GetString(4),
                    SortedAt   = reader.GetDateTime(5),
                });
        }
        catch { }
        return result;
    }

    // ── 분류 이력 단건 삭제 ─────────────────────────────────────────
    public async Task<bool> DeleteParcelAsync(int id)
    {
        try
        {
            await using var conn = new MySqlConnection(_cs);
            await conn.OpenAsync();
            await new MySqlCommand($"DELETE FROM sort_event WHERE parcel_id={id}", conn).ExecuteNonQueryAsync();
            await new MySqlCommand($"DELETE FROM parcel WHERE id={id}", conn).ExecuteNonQueryAsync();
            return true;
        }
        catch { return false; }
    }

    // ── 분류 이력 다건 삭제 ─────────────────────────────────────────
    public async Task<bool> DeleteParcelsAsync(IEnumerable<int> ids)
    {
        var idList = string.Join(",", ids);
        if (string.IsNullOrEmpty(idList)) return false;
        try
        {
            await using var conn = new MySqlConnection(_cs);
            await conn.OpenAsync();
            await new MySqlCommand($"DELETE FROM sort_event WHERE parcel_id IN ({idList})", conn).ExecuteNonQueryAsync();
            await new MySqlCommand($"DELETE FROM parcel WHERE id IN ({idList})", conn).ExecuteNonQueryAsync();
            return true;
        }
        catch { return false; }
    }

    // ── DB 전체 초기화 ──────────────────────────────────────────────
    public async Task<bool> ClearAllDataAsync()
    {
        try
        {
            await using var conn = new MySqlConnection(_cs);
            await conn.OpenAsync();
            await new MySqlCommand("DELETE FROM sort_event",      conn).ExecuteNonQueryAsync();
            await new MySqlCommand("DELETE FROM parcel",          conn).ExecuteNonQueryAsync();
            await new MySqlCommand("DELETE FROM alarm_history",   conn).ExecuteNonQueryAsync();
            await new MySqlCommand("DELETE FROM line_status_log", conn).ExecuteNonQueryAsync();
            // AUTO_INCREMENT 초기화 (ID를 1부터 다시 시작)
            await new MySqlCommand("ALTER TABLE sort_event      AUTO_INCREMENT = 1", conn).ExecuteNonQueryAsync();
            await new MySqlCommand("ALTER TABLE parcel          AUTO_INCREMENT = 1", conn).ExecuteNonQueryAsync();
            await new MySqlCommand("ALTER TABLE alarm_history   AUTO_INCREMENT = 1", conn).ExecuteNonQueryAsync();
            await new MySqlCommand("ALTER TABLE line_status_log AUTO_INCREMENT = 1", conn).ExecuteNonQueryAsync();
            return true;
        }
        catch { return false; }
    }

    // ── 지역 코드 전체 조회 ─────────────────────────────────────────
    public async Task<List<RegionRuleRow>> GetRegionRulesAsync()
    {
        var result = new List<RegionRuleRow>();
        try
        {
            await using var conn = new MySqlConnection(_cs);
            await conn.OpenAsync();
            var sql = "SELECT region_code, region_name, is_active, created_at FROM region_rule ORDER BY region_code";
            await using var cmd    = new MySqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(new RegionRuleRow
                {
                    RegionCode = reader.GetInt32(0),
                    RegionName = reader.GetString(1),
                    IsActive   = reader.GetBoolean(2),
                    CreatedAt  = reader.GetDateTime(3),
                });
        }
        catch { }
        return result;
    }

    // ── 지역 코드 추가 ──────────────────────────────────────────────
    public async Task<bool> AddRegionRuleAsync(int code, string name)
    {
        try
        {
            await using var conn = new MySqlConnection(_cs);
            await conn.OpenAsync();
            var sql = "INSERT INTO region_rule (region_code, region_name) VALUES (@c, @n)";
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@c", code);
            cmd.Parameters.AddWithValue("@n", name);
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch { return false; }
    }

    // ── 지역 코드 수정 ──────────────────────────────────────────────
    public async Task<bool> UpdateRegionRuleAsync(int code, string name, bool isActive)
    {
        try
        {
            await using var conn = new MySqlConnection(_cs);
            await conn.OpenAsync();
            var sql = "UPDATE region_rule SET region_name=@n, is_active=@a WHERE region_code=@c";
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@n", name);
            cmd.Parameters.AddWithValue("@a", isActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@c", code);
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch { return false; }
    }

    // ── 지역 코드 삭제 ──────────────────────────────────────────────
    public async Task<bool> DeleteRegionRuleAsync(int code)
    {
        try
        {
            await using var conn = new MySqlConnection(_cs);
            await conn.OpenAsync();
            await new MySqlCommand($"DELETE FROM region_rule WHERE region_code={code}", conn).ExecuteNonQueryAsync();
            return true;
        }
        catch { return false; }
    }
}

// ── 모델 레코드 ──────────────────────────────────────────────────────
public class ParcelAdminRow : System.ComponentModel.INotifyPropertyChanged
{
    public int      Id         { get; init; }
    public int      BoxId      { get; init; }
    public string   RegionName { get; init; } = "";
    public double   WeightKg   { get; init; }
    public string   SortResult { get; init; } = "";
    public DateTime SortedAt   { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
}

public class RegionRuleRow : System.ComponentModel.INotifyPropertyChanged
{
    public int      RegionCode { get; set; }
    private string  _regionName = "";
    public string   RegionName
    {
        get => _regionName;
        set { _regionName = value; OnPropertyChanged(); }
    }
    private bool _isActive = true;
    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }
    public DateTime CreatedAt { get; set; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
}
