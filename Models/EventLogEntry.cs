namespace SmartFactoryCPS.Models;

public class EventLogEntry
{
    public DateTime Timestamp { get; set; }
    public int BoxId { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public double Weight { get; set; }
    public string Status { get; set; } = "OK";
}
