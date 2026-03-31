namespace SmartFactoryCPS.Models;

public class BoxInfo
{
    public int BoxId { get; set; }
    public int RegionCode { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public double Weight { get; set; }
    public string Status { get; set; } = "대기";
}
