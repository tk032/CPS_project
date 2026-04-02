using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SmartFactoryCPS.Services;

namespace SmartFactoryCPS.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private static readonly double[] SortPointX = { 208.0, 383.0, 558.0, 733.0 };
    private const double BoxExitX         = 940.0;
    private const double BoxSpeedPerFrame = 0.41;

    private readonly DispatcherTimer  _animTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DbService        _db  = new();
    private readonly FactoryIoService _fio = new();

    private int      _nextBoxId;
    private DateTime _startTime;
    private readonly int[] _sessionCounts = new int[6];
    private readonly Dictionary<int, int> _pendingSortedCodes = new();

    public ObservableCollection<ConveyorBoxVm> ConveyorBoxes { get; } = new();

    private bool _push1Active, _push2Active, _push3Active, _push4Active;
    public bool Push1Active { get => _push1Active; set { _push1Active = value; OnPropertyChanged(); } }
    public bool Push2Active { get => _push2Active; set { _push2Active = value; OnPropertyChanged(); } }
    public bool Push3Active { get => _push3Active; set { _push3Active = value; OnPropertyChanged(); } }
    public bool Push4Active { get => _push4Active; set { _push4Active = value; OnPropertyChanged(); } }

    private string _plcIpAddress = PlcAddr.PlcIp;
    public string PlcIpAddress
    {
        get => _plcIpAddress;
        set { _plcIpAddress = value; OnPropertyChanged(); }
    }

    private string _plcPort = PlcAddr.Port.ToString();
    public string PlcPort
    {
        get => _plcPort;
        set { _plcPort = value; OnPropertyChanged(); }
    }
    private int PlcPortInt => int.TryParse(_plcPort, out var p) ? p : PlcAddr.Port;

    private string _locId = "1";
    public string LocId
    {
        get => _locId;
        set { _locId = value; OnPropertyChanged(); }
    }
    private int LocIdInt => int.TryParse(_locId, out var l) && l >= 1 ? l : 1;

    public bool   CanEditIp     => !_isRunning;
    public string FioStatusText => _fio.IsConnected ? "PLC 연결됨" : "PLC 연결 안됨";
    public bool   FioConnected  => _fio.IsConnected;

    private static readonly Dictionary<int, (string Name, Brush Color)> Regions = new()
    {
        { 1, ("서울", new SolidColorBrush(Color.FromRgb(0x2B, 0x7F, 0xFF))) },
        { 2, ("대전", new SolidColorBrush(Color.FromRgb(0xAD, 0x46, 0xFF))) },
        { 3, ("대구", new SolidColorBrush(Color.FromRgb(0xFF, 0x7B, 0x00))) },
        { 4, ("부산", new SolidColorBrush(Color.FromRgb(0x00, 0xC9, 0x50))) },
        { 5, ("기타", new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8))) },
    };

    public MainViewModel()
    {
        RegionStats = new ObservableCollection<RegionStat>(
            Regions.Select(kv => new RegionStat
            {
                RegionCode = kv.Key,
                RegionName = kv.Value.Name,
                DotColor   = kv.Value.Color,
            }));
        EventLogs    = new ObservableCollection<EventLogEntry>();
        _currentView = this;

        _animTimer.Tick += OnAnimTick;
        _ = CheckDbAsync();

        StartCommand         = new RelayCommand(_ => { _ = StartAsync(); }, _ => !_isRunning);
        StopCommand          = new RelayCommand(_ => Stop(),  _ => _isRunning);
        ResetCommand         = new RelayCommand(_ => Reset());
        ShowDashboardCommand = new RelayCommand(_ => CurrentView = this);
        ShowAdminCommand     = new RelayCommand(_ =>
        {
            _adminVm ??= new AdminViewModel(() => LocIdInt);
            CurrentView = _adminVm;
        });

        _fio.ConnectionChanged += _ =>
        {
            OnPropertyChanged(nameof(FioStatusText));
            OnPropertyChanged(nameof(FioConnected));
        };

        _fio.DiffuseSensorsChanged += (s1, s2, s3, s4) =>
        {
            IsRfid1Scanning = s1;
            IsRfid2Scanning = s2;
            IsRfid3Scanning = s3;
            IsRfid4Scanning = s4;
        };

        _fio.BoxEntered += (regionCode, regionName) =>
        {
            if (_pendingSortedCodes.TryGetValue(regionCode, out int cnt) && cnt > 0)
            {
                _pendingSortedCodes[regionCode] = cnt - 1;
                return;
            }
            CurrentSortTarget = regionName;
            CurrentBox = new BoxInfo
            {
                BoxId = _nextBoxId + 1, RegionCode = regionCode,
                RegionName = regionName, Weight = 0, Status = "분류 중",
            };
            ConveyorBoxes.Add(new ConveyorBoxVm
            {
                BoxId      = _nextBoxId + 1,
                RegionCode = regionCode,
                RegionName = regionName,
                LabelColor = Regions.GetValueOrDefault(regionCode, (regionName, Brushes.Gray)).Color,
                X          = SortPointX[0],
            });
        };

        _fio.BoxSorted += (regionCode, regionName, boxWeight, cumWeight, paletteCount) =>
        {
            if (boxWeight <= 0) return;
            _nextBoxId++;  // 실제 처리 완료 시 1회만 증가

            double spX   = SortPointX[regionCode - 1];
            var toRemove = ConveyorBoxes
                .Where(b => Math.Abs(b.X - spX) <= 150)
                .OrderBy(b => Math.Abs(b.X - spX))
                .FirstOrDefault();
            if (toRemove != null)
                ConveyorBoxes.Remove(toRemove);
            else
            {
                _pendingSortedCodes.TryGetValue(regionCode, out int c);
                _pendingSortedCodes[regionCode] = c + 1;
            }
            _ = FlashPusherAsync(regionCode);

            var stat = RegionStats.FirstOrDefault(s => s.RegionCode == regionCode);
            if (stat != null)
            {
                stat.Count++;
                stat.TotalWeight  = cumWeight;
                stat.PaletteCount = paletteCount;
            }

            _sessionCounts[regionCode]++;
            TotalProcessed++;
            UpdateThroughput();

            CurrentSortTarget = regionName;
            CurrentBox = new BoxInfo
            {
                BoxId = _nextBoxId, RegionCode = regionCode,
                RegionName = regionName, Weight = boxWeight, Status = "분류 완료",
            };

            if (!_hasActiveAlarm && _sessionCounts[regionCode] >= 30)
            {
                HasActiveAlarm = true;
                AlarmCode      = regionCode;
                AlarmMessage   = $"경고: {regionName} 누적 수량 임계치 초과 (세션 {_sessionCounts[regionCode]}건)";
            }

            if (EventLogs.Count >= 50) EventLogs.RemoveAt(EventLogs.Count - 1);
            EventLogs.Insert(0, new EventLogEntry
            {
                Timestamp = DateTime.Now, BoxId = _nextBoxId,
                RegionName = regionName, Weight = boxWeight, Status = "OK",
            });
            OnPropertyChanged(nameof(HasEvents));
            _ = SaveParcelToDbAsync(_nextBoxId, regionCode, regionName, boxWeight, LocIdInt);
        };
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MachineStatus));
            OnPropertyChanged(nameof(ConveyorStatus));
            OnPropertyChanged(nameof(CanEditIp));
            OnPropertyChanged(nameof(PlcPort));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public string MachineStatus  => _isRunning ? "Running" : "Stopped";
    public string ConveyorStatus => _isRunning ? "운전"    : "정지";

    private string _currentSortTarget = "-";
    public string CurrentSortTarget
    {
        get => _currentSortTarget;
        set { _currentSortTarget = value; OnPropertyChanged(); }
    }

    private BoxInfo? _currentBox;
    public BoxInfo? CurrentBox
    {
        get => _currentBox;
        set { _currentBox = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCurrentBox)); }
    }
    public bool HasCurrentBox => CurrentBox != null;

    private int _alarmCode;
    public int AlarmCode
    {
        get => _alarmCode;
        set { _alarmCode = value; OnPropertyChanged(); }
    }

    private bool _hasActiveAlarm;
    public bool HasActiveAlarm
    {
        get => _hasActiveAlarm;
        set { _hasActiveAlarm = value; OnPropertyChanged(); }
    }

    private string _alarmMessage = "활성 알람 없음";
    public string AlarmMessage
    {
        get => _alarmMessage;
        set { _alarmMessage = value; OnPropertyChanged(); }
    }

    private int _totalProcessed;
    public int TotalProcessed
    {
        get => _totalProcessed;
        set { _totalProcessed = value; OnPropertyChanged(); }
    }

    private double _throughputPerMinute;
    public double ThroughputPerMinute
    {
        get => _throughputPerMinute;
        set { _throughputPerMinute = value; OnPropertyChanged(); }
    }

    public ObservableCollection<RegionStat>    RegionStats { get; }
    public ObservableCollection<EventLogEntry> EventLogs   { get; }
    public bool HasEvents => EventLogs.Count > 0;

    private bool _isDbConnected;
    public bool IsDbConnected
    {
        get => _isDbConnected;
        set { _isDbConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(DbStatusText)); }
    }
    public string DbStatusText => _isDbConnected ? "DB 연결됨" : "DB 연결 안됨";

    private string _dbLastError = "";
    public string DbLastError
    {
        get => _dbLastError;
        set { _dbLastError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDbError)); }
    }
    public bool HasDbError => !string.IsNullOrEmpty(_dbLastError);

    private bool _isRfid1Scanning;
    public bool IsRfid1Scanning { get => _isRfid1Scanning; set { _isRfid1Scanning = value; OnPropertyChanged(); } }
    private bool _isRfid2Scanning;
    public bool IsRfid2Scanning { get => _isRfid2Scanning; set { _isRfid2Scanning = value; OnPropertyChanged(); } }
    private bool _isRfid3Scanning;
    public bool IsRfid3Scanning { get => _isRfid3Scanning; set { _isRfid3Scanning = value; OnPropertyChanged(); } }
    private bool _isRfid4Scanning;
    public bool IsRfid4Scanning { get => _isRfid4Scanning; set { _isRfid4Scanning = value; OnPropertyChanged(); } }

    public ICommand StartCommand         { get; }
    public ICommand StopCommand          { get; }
    public ICommand ResetCommand         { get; }
    public ICommand ShowDashboardCommand { get; }
    public ICommand ShowAdminCommand     { get; }

    private object _currentView;
    public object CurrentView
    {
        get => _currentView;
        set
        {
            _currentView = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDashboardActive));
            OnPropertyChanged(nameof(IsAdminActive));
        }
    }
    public bool IsDashboardActive => CurrentView is MainViewModel;
    public bool IsAdminActive     => CurrentView is AdminViewModel;

    private AdminViewModel? _adminVm;

    private async Task CheckDbAsync() => IsDbConnected = await _db.TestConnectionAsync();

    private async Task StartAsync()
    {
        _nextBoxId = await _db.GetLastBoxIdAsync(LocIdInt);
        _startTime = DateTime.Now;
        ConveyorBoxes.Clear();
        IsRunning = true;
        _fio.Connect(_plcIpAddress, PlcPortInt);
        _animTimer.Start();
    }

    private void Stop()
    {
        _animTimer.Stop();
        _fio.Disconnect();
        IsRunning         = false;
        IsRfid1Scanning   = false;
        IsRfid2Scanning   = false;
        IsRfid3Scanning   = false;
        IsRfid4Scanning   = false;
        CurrentSortTarget = "-";
        CurrentBox        = null;
        ConveyorBoxes.Clear();
        _pendingSortedCodes.Clear();
        Array.Clear(_sessionCounts, 0, _sessionCounts.Length);
    }

    private void Reset()
    {
        Stop();
        foreach (var s in RegionStats) { s.Count = 0; s.TotalWeight = 0; s.PaletteCount = 0; }
        EventLogs.Clear();
        OnPropertyChanged(nameof(HasEvents));
        TotalProcessed      = 0;
        ThroughputPerMinute = 0;
        HasActiveAlarm      = false;
        AlarmCode           = 0;
        AlarmMessage        = "활성 알람 없음";
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        for (int i = ConveyorBoxes.Count - 1; i >= 0; i--)
        {
            var box = ConveyorBoxes[i];
            box.X += BoxSpeedPerFrame;
            if (box.X > BoxExitX)
            {
                ConveyorBoxes.RemoveAt(i);
                if (_isRunning) CompleteGita();
            }
        }
    }

    private void CompleteGita()
    {
        var stat = RegionStats.FirstOrDefault(s => s.RegionCode == 5);
        if (stat != null) stat.Count++;
        TotalProcessed++;
        UpdateThroughput();
        if (EventLogs.Count >= 50) EventLogs.RemoveAt(EventLogs.Count - 1);
        EventLogs.Insert(0, new EventLogEntry
        {
            Timestamp = DateTime.Now, BoxId = _nextBoxId,
            RegionName = "기타", Weight = 0, Status = "기타",
        });
        OnPropertyChanged(nameof(HasEvents));
    }

    private async Task FlashPusherAsync(int regionCode)
    {
        SetPusher(regionCode, true);
        await Task.Delay(1200);
        SetPusher(regionCode, false);
    }

    private void SetPusher(int regionCode, bool active)
    {
        switch (regionCode)
        {
            case 1: Push1Active = active; break;
            case 2: Push2Active = active; break;
            case 3: Push3Active = active; break;
            case 4: Push4Active = active; break;
        }
    }

    private void UpdateThroughput()
    {
        var elapsed = (DateTime.Now - _startTime).TotalMinutes;
        ThroughputPerMinute = elapsed > 0
            ? Math.Round(TotalProcessed / elapsed, 1)
            : TotalProcessed;
    }

    private async Task SaveParcelToDbAsync(
        int boxId, int regionCode, string regionName, double weight, int locId)
    {
        var (_, error) = await _db.SaveParcelAsync(boxId, regionCode, regionName, weight, locId);
        if (string.IsNullOrEmpty(error))
        {
            if (!_isDbConnected) IsDbConnected = true;
            DbLastError = "";
        }
        else
        {
            if (error.Contains("stream", StringComparison.OrdinalIgnoreCase)) return;
            DbLastError   = $"DB 오류: {error}";
            IsDbConnected = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add    => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
    public bool CanExecute(object? p) => canExecute?.Invoke(p) ?? true;
    public void Execute(object? p)    => execute(p);
}
