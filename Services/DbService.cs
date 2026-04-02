using Npgsql;

namespace SmartFactoryCPS.Services;

public class DbService
{
    private const string _cs = DbConfig.ConnectionString;

    private NpgsqlConnection Open() => new(_cs);

    public async Task<int> GetLastBoxIdAsync(int locId)
    {
        try
        {
            await using var conn = Open();
            await conn.OpenAsync();
            var cmd = new NpgsqlCommand("SELECT COALESCE(MAX(box_id), 0) FROM parcel WHERE loc_id=@loc", conn);
            cmd.Parameters.AddWithValue("@loc", locId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        catch { return 0; }
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            await using var conn = Open();
            await conn.OpenAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<(int id, string error)> SaveParcelAsync(
        int boxId, int regionCode, string regionName, double weightKg, int locId)
    {
        try
        {
            await using var conn = Open();
            await conn.OpenAsync();
            var sql = @"INSERT INTO parcel (box_id, region_code, region_name, weight_kg, sort_result, loc_id)
                        VALUES (@boxId, @rc, @rn, @w, 'OK', @loc) RETURNING id";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@boxId", boxId);
            cmd.Parameters.AddWithValue("@rc",    regionCode);
            cmd.Parameters.AddWithValue("@rn",    regionName);
            cmd.Parameters.AddWithValue("@w",     weightKg);
            cmd.Parameters.AddWithValue("@loc",   locId);
            return (Convert.ToInt32(await cmd.ExecuteScalarAsync()), "");
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    public async Task<List<ParcelAdminRow>> GetParcelsAsync(
        string? search, string? region, int locId, int limit = 200)
    {
        var result = new List<ParcelAdminRow>();
        try
        {
            await using var conn = Open();
            await conn.OpenAsync();

            var conditions = new List<string> { "loc_id=@loc" };
            if (!string.IsNullOrWhiteSpace(search))
                conditions.Add("(region_name LIKE @s OR CAST(box_id AS TEXT) LIKE @s)");
            if (!string.IsNullOrWhiteSpace(region))
                conditions.Add("region_name=@region");

            var sql = $"SELECT id,box_id,region_name,weight_kg,sort_result,sorted_at FROM parcel WHERE {string.Join(" AND ", conditions)} ORDER BY id DESC LIMIT @limit";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@loc",   locId);
            cmd.Parameters.AddWithValue("@limit", limit);
            if (!string.IsNullOrWhiteSpace(search))  cmd.Parameters.AddWithValue("@s",      $"%{search}%");
            if (!string.IsNullOrWhiteSpace(region))  cmd.Parameters.AddWithValue("@region", region);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                result.Add(new ParcelAdminRow
                {
                    Id         = r.GetInt32(0),
                    BoxId      = r.GetInt32(1),
                    RegionName = r.GetString(2),
                    WeightKg   = r.GetDouble(3),
                    SortResult = r.GetString(4),
                    SortedAt   = r.GetDateTime(5),
                });
        }
        catch { }
        return result;
    }

    public async Task<bool> DeleteParcelsAsync(IEnumerable<int> ids)
    {
        var idList = string.Join(",", ids);
        if (string.IsNullOrEmpty(idList)) return false;
        try
        {
            await using var conn = Open();
            await conn.OpenAsync();
            await new NpgsqlCommand($"DELETE FROM parcel WHERE id IN ({idList})", conn).ExecuteNonQueryAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<bool> ClearAllDataAsync(int locId)
    {
        try
        {
            await using var conn = Open();
            await conn.OpenAsync();
            await new NpgsqlCommand($"DELETE FROM parcel WHERE loc_id={locId}", conn).ExecuteNonQueryAsync();
            return true;
        }
        catch { return false; }
    }
}

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
