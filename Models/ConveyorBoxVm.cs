using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SmartFactoryCPS.Models;

/// <summary>컨베이어 위에서 이동 중인 박스 (UI 표현용)</summary>
public class ConveyorBoxVm : INotifyPropertyChanged
{
    private double _x;

    public int    BoxId      { get; init; }
    public int    RegionCode { get; init; }
    public string RegionName { get; init; } = "";
    public Brush  LabelColor { get; init; } = Brushes.Gray;

    /// <summary>Canvas 내 X 좌표 (애니메이션 타이머가 갱신)</summary>
    public double X
    {
        get => _x;
        set { _x = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
