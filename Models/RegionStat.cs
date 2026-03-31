using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SmartFactoryCPS.Models;

public class RegionStat : INotifyPropertyChanged
{
    private int _count;
    private double _totalWeight;

    public int RegionCode { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public Brush DotColor { get; set; } = Brushes.Gray;

    public int Count
    {
        get => _count;
        set { _count = value; OnPropertyChanged(); }
    }

    public double TotalWeight
    {
        get => _totalWeight;
        set { _totalWeight = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
