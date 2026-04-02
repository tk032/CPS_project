using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SmartFactoryCPS.ViewModels;

/// <summary>현재 처리 중인 박스 정보 (UI 상단 카드 표시용)</summary>
public class BoxInfo
{
    public int    BoxId      { get; set; }
    public int    RegionCode { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public double Weight     { get; set; }
    public string Status     { get; set; } = "대기";
}

/// <summary>이벤트 로그 한 줄 항목</summary>
public class EventLogEntry
{
    public DateTime Timestamp  { get; set; }
    public int      BoxId      { get; set; }
    public string   RegionName { get; set; } = string.Empty;
    public double   Weight     { get; set; }
    public string   Status     { get; set; } = "OK";
}

/// <summary>지역별 누적 통계 (수량·무게) — UI 카드에 바인딩</summary>
public class RegionStat : INotifyPropertyChanged
{
    private int    _count;
    private double _totalWeight;
    private int    _paletteCount;

    public int    RegionCode { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public Brush  DotColor   { get; set; } = Brushes.Gray;

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

    public int PaletteCount
    {
        get => _paletteCount;
        set { _paletteCount = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>컨베이어 위에서 이동 중인 박스 (애니메이션 Canvas 바인딩용)</summary>
public class ConveyorBoxVm : INotifyPropertyChanged
{
    private double _x;

    public int    BoxId      { get; init; }
    public int    RegionCode { get; init; }
    public string RegionName { get; init; } = "";
    public Brush  LabelColor { get; init; } = Brushes.Gray;

    /// <summary>Canvas 내 X 좌표 — 16ms 애니메이션 타이머가 갱신</summary>
    public double X
    {
        get => _x;
        set { _x = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
