using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SmartFactoryCPS.Services;

namespace SmartFactoryCPS.ViewModels;

public class AdminViewModel : INotifyPropertyChanged
{
    private readonly DbService _db = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Func<int> _getLocId;
    private int _locId => _getLocId();

    public string LocLabel => $"LOC-{_locId}";

    // 지역 필터 옵션 항목
    public record RegionFilterOption(string Label, string? Value);

    public AdminViewModel(Func<int> getLocId)
    {
        _getLocId = getLocId;
        Parcels = new ObservableCollection<ParcelAdminRow>();

        RegionFilterOptions = new List<RegionFilterOption>
        {
            new("전체", null),
            new("서울", "서울"),
            new("대전", "대전"),
            new("대구", "대구"),
            new("부산", "부산"),
            new("기타", "기타"),
        };
        _selectedRegionFilter = RegionFilterOptions[0];

        LoadParcelsCommand           = new RelayCommand(async _ => await LoadParcelsAsync());
        DeleteSelectedParcelsCommand = new RelayCommand(async _ => await DeleteSelectedParcelsAsync(),
                                                        _ => Parcels.Any(p => p.IsSelected));
        SelectAllCommand             = new RelayCommand(_ => SetAllSelected(true));
        DeselectAllCommand           = new RelayCommand(_ => SetAllSelected(false));
        ClearDbCommand               = new RelayCommand(async _ => await ClearDbAsync());

        _ = LoadParcelsAsync();

        _refreshTimer.Tick += async (_, _) => await LoadParcelsAsync();
        _refreshTimer.Start();
    }

    // ════════════════════ 분류 이력 ════════════════════

    public ObservableCollection<ParcelAdminRow> Parcels { get; }
    public List<RegionFilterOption> RegionFilterOptions { get; }

    private RegionFilterOption _selectedRegionFilter;
    public RegionFilterOption SelectedRegionFilter
    {
        get => _selectedRegionFilter;
        set { _selectedRegionFilter = value; OnPropertyChanged(); _ = LoadParcelsAsync(); }
    }

    private int _parcelCount;
    public int ParcelCount
    {
        get => _parcelCount;
        set { _parcelCount = value; OnPropertyChanged(); }
    }

    private string _parcelSearch = "";
    public string ParcelSearch
    {
        get => _parcelSearch;
        set { _parcelSearch = value; OnPropertyChanged(); _ = LoadParcelsAsync(); }
    }

    private ParcelAdminRow? _selectedParcel;
    public ParcelAdminRow? SelectedParcel
    {
        get => _selectedParcel;
        set { _selectedParcel = value; OnPropertyChanged(); }
    }

    public ICommand LoadParcelsCommand           { get; }
    public ICommand DeleteSelectedParcelsCommand { get; }
    public ICommand SelectAllCommand             { get; }
    public ICommand DeselectAllCommand           { get; }
    public ICommand ClearDbCommand               { get; }

    public int  SelectedCount    => Parcels.Count(p => p.IsSelected);
    public bool HasSelectedItems => Parcels.Any(p => p.IsSelected);

    private async Task LoadParcelsAsync()
    {
        OnPropertyChanged(nameof(LocLabel));
        var region = _selectedRegionFilter?.Value;
        var rows   = await _db.GetParcelsAsync(_parcelSearch, region, locId: _locId);
        Parcels.Clear();
        foreach (var r in rows)
        {
            r.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(SelectedCount));
                CommandManager.InvalidateRequerySuggested();
            };
            Parcels.Add(r);
        }
        ParcelCount = Parcels.Count;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelectedItems));
    }

    private async Task DeleteSelectedParcelsAsync()
    {
        var ids = Parcels.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        if (ids.Count == 0) return;
        var ok = await _db.DeleteParcelsAsync(ids);
        if (ok) await LoadParcelsAsync();
        else ErrorMessage = "다중 삭제 실패: 연결을 확인하세요.";
    }

    private void SetAllSelected(bool value)
    {
        foreach (var p in Parcels) p.IsSelected = value;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelectedItems));
        CommandManager.InvalidateRequerySuggested();
    }

    // ════════════════════ DB 초기화 ════════════════════

    private async Task ClearDbAsync()
    {
        var result = MessageBox.Show(
            "분류 이력, 이벤트 로그, 알람 기록을 모두 삭제합니다.\n계속하시겠습니까?",
            "DB 초기화 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var ok = await _db.ClearAllDataAsync(_locId);
        if (ok)
        {
            ErrorMessage = "";
            await LoadParcelsAsync();
        }
        else
        {
            ErrorMessage = "DB 초기화 실패: 연결을 확인하세요.";
        }
    }

    // ════════════════════ 오류 메시지 ════════════════════

    private string _errorMessage = "";
    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
