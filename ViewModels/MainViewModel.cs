using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SmartFactoryCPS.Models;
using SmartFactoryCPS.Services;

namespace SmartFactoryCPS.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    // ── 컨베이어 박스 위치 상수 (960px Canvas 기준) ──────────────────
    private static readonly double[] SortPointX = { 208.0, 383.0, 558.0, 733.0 };
    private const double BoxExitX         = 940.0;
    private const double BoxSpeedPerFrame = 0.41; // px/frame @16ms

    // ── 타이머 ───────────────────────────────────────────────────────
    private readonly DispatcherTimer _animTimer      = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _statusLogTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    // ── 서비스 ───────────────────────────────────────────────────────
    private readonly DbService        _db  = new();
    private readonly FactoryIoService _fio = new();

    private int      _nextBoxId;
    private int      _lastAlarmId = -1;
    private DateTime _startTime;

    // 이번 세션 동안 처리한 지역별 박스 수 (알람 판단용 — PLC 누적값 아님)
    private readonly int[] _sessionCounts = new int[6]; // index = regionCode (1-5)

    // BoxSorted가 BoxEntered보다 먼저 발생하는 경우 (서울 등) 해당 regionCode를 보류로 기록
    private readonly Dictionary<int, int> _pendingSortedCodes = new();

    // ── 컨베이어 다중 박스 ────────────────────────────────────────────
    public ObservableCollection<ConveyorBoxVm> ConveyorBoxes { get; } = new();

    // ── 푸셔 활성 상태 ───────────────────────────────────────────────
    private bool _push1Active, _push2Active, _push3Active, _push4Active;
    public bool Push1Active { get => _push1Active; set { _push1Active = value; OnPropertyChanged(); } }
    public bool Push2Active { get => _push2Active; set { _push2Active = value; OnPropertyChanged(); } }
    public bool Push3Active { get => _push3Active; set { _push3Active = value; OnPropertyChanged(); } }
    public bool Push4Active { get => _push4Active; set { _push4Active = value; OnPropertyChanged(); } }

    // ── PLC IP / Port ────────────────────────────────────────────────
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

    private int PlcPortInt =>
        int.TryParse(_plcPort, out var p) ? p : PlcAddr.Port;

    public bool CanEditIp => !_isRunning;

    // ── PLC 연결 상태 ────────────────────────────────────────────────
    public string FioStatusText => _fio.IsConnected ? "PLC 연결됨" : "PLC 연결 안됨";
    public bool   FioConnected  => _fio.IsConnected;

    // ── 지역 정보 ───────────────────────────────────────────────────
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
        EventLogs = new ObservableCollection<EventLogEntry>();

        _currentView = this;

        _animTimer.Tick      += OnAnimTick;
        _statusLogTimer.Tick += async (_, _) =>
            await _db.LogLineStatusAsync(_isRunning, _totalProcessed, _throughputPerMinute);

        _ = CheckDbAsync();

        StartCommand         = new RelayCommand(_ => { _ = StartAsync(); }, _ => !_isRunning);
        StopCommand          = new RelayCommand(_ => Stop(),  _ => _isRunning);
        ResetCommand         = new RelayCommand(_ => Reset());
        ShowDashboardCommand = new RelayCommand(_ => CurrentView = this);
        ShowAdminCommand     = new RelayCommand(_ =>
        {
            _adminVm ??= new AdminViewModel();
            CurrentView = _adminVm;
        });

        // ── Factory I/O 이벤트 구독 ──────────────────────────────────
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
            _nextBoxId++;

            // BoxSorted가 이미 먼저 발생한 경우(서울 등): 벨트에 추가하지 않음
            if (_pendingSortedCodes.TryGetValue(regionCode, out int cnt) && cnt > 0)
            {
                _pendingSortedCodes[regionCode] = cnt - 1;
                return;
            }

            CurrentSortTarget = regionName;
            CurrentBox = new BoxInfo
            {
                BoxId      = _nextBoxId,
                RegionCode = regionCode,
                RegionName = regionName,
                Weight     = 0,
                Status     = "분류 중",
            };
            ConveyorBoxes.Add(new ConveyorBoxVm
            {
                BoxId      = _nextBoxId,
                RegionCode = regionCode,
                RegionName = regionName,
                LabelColor = Regions.GetValueOrDefault(regionCode, (regionName, Brushes.Gray)).Color,
                X          = SortPointX[0],
            });
        };

        // BoxSorted: Push 코일 상승 + PLC 무게 읽기 완료 후 발생
        // 애니메이션(벨트 박스 제거+푸셔 플래시) + 통계(PLC 값 그대로) + DB 저장
        _fio.BoxSorted += (regionCode, regionName, boxWeight, cumWeight) =>
        {
            // ── 애니메이션 ── 분류지점 ±150px 이내 박스만 제거 (기타 박스 오제거 방지)
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

            // ── 통계 (PLC 값 그대로 반영, WPF 계산 없음) ──
            var stat = RegionStats.FirstOrDefault(s => s.RegionCode == regionCode);
            if (stat != null)
            {
                stat.Count++;
                stat.TotalWeight = cumWeight; // MW101~104 누적 무게합
            }

            _sessionCounts[regionCode]++;
            TotalProcessed++;
            UpdateThroughput();

            CurrentSortTarget = regionName;
            CurrentBox = new BoxInfo
            {
                BoxId      = _nextBoxId,
                RegionCode = regionCode,
                RegionName = regionName,
                Weight     = boxWeight,  // MW20~23 개별 박스 무게
                Status     = "분류 완료",
            };

            // ── 알람: 이번 세션 지역별 누적 수량 기준 (30건 임계치) ──
            if (!_hasActiveAlarm && _sessionCounts[regionCode] >= 30)
            {
                HasActiveAlarm = true;
                AlarmCode      = regionCode;
                AlarmMessage   = $"경고: {regionName} 누적 수량 임계치 초과 (세션 {_sessionCounts[regionCode]}건)";
                _ = SaveAlarmToDbAsync(regionCode, AlarmMessage);
            }

            // ── 이벤트 로그 + DB ──
            if (EventLogs.Count >= 50) EventLogs.RemoveAt(EventLogs.Count - 1);
            EventLogs.Insert(0, new EventLogEntry
            {
                Timestamp  = DateTime.Now,
                BoxId      = _nextBoxId,
                RegionName = regionName,
                Weight     = boxWeight,
                Status     = "OK",
            });
            OnPropertyChanged(nameof(HasEvents));
            _ = SaveParcelToDbAsync(_nextBoxId, regionCode, regionName, boxWeight);
        };
    }

    // ════════════════════════════════ 공정 상태 프로퍼티 ═════════════

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

    // ── RFID 스캔 상태 (PLC Diffuse 센서) ───────────────────────────
    private bool _isRfid1Scanning;
    public bool IsRfid1Scanning { get => _isRfid1Scanning; set { _isRfid1Scanning = value; OnPropertyChanged(); } }
    private bool _isRfid2Scanning;
    public bool IsRfid2Scanning { get => _isRfid2Scanning; set { _isRfid2Scanning = value; OnPropertyChanged(); } }
    private bool _isRfid3Scanning;
    public bool IsRfid3Scanning { get => _isRfid3Scanning; set { _isRfid3Scanning = value; OnPropertyChanged(); } }
    private bool _isRfid4Scanning;
    public bool IsRfid4Scanning { get => _isRfid4Scanning; set { _isRfid4Scanning = value; OnPropertyChanged(); } }

    // ════════════════════════════════ 커맨드 ══════════════════════════

    public ICommand StartCommand         { get; }
    public ICommand StopCommand          { get; }
    public ICommand ResetCommand         { get; }
    public ICommand ShowDashboardCommand { get; }
    public ICommand ShowAdminCommand     { get; }

    // ── 내비게이션 ───────────────────────────────────────────────────
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

    // ════════════════════════════════ 로직 ════════════════════════════

    private async Task CheckDbAsync()
    {
        IsDbConnected = await _db.TestConnectionAsync();
    }

    private async Task StartAsync()
    {
        _nextBoxId = await _db.GetLastBoxIdAsync();
        _startTime = DateTime.Now;
        ConveyorBoxes.Clear();
        IsRunning  = true;
        _fio.Connect(_plcIpAddress, PlcPortInt);
        _animTimer.Start();
        _statusLogTimer.Start();
    }

    private void Stop()
    {
        _animTimer.Stop();
        _statusLogTimer.Stop();
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
        foreach (var s in RegionStats) { s.Count = 0; s.TotalWeight = 0; }
        EventLogs.Clear();
        OnPropertyChanged(nameof(HasEvents));
        TotalProcessed      = 0;
        ThroughputPerMinute = 0;
        HasActiveAlarm      = false;
        AlarmCode           = 0;
        AlarmMessage        = "활성 알람 없음";
    }

    // ── 애니메이션 틱 (16ms) ────────────────────────────────────────
    private void OnAnimTick(object? sender, EventArgs e)
    {
        for (int i = ConveyorBoxes.Count - 1; i >= 0; i--)
        {
            var box = ConveyorBoxes[i];
            box.X += BoxSpeedPerFrame;
            if (box.X > BoxExitX)
            {
                int exitBoxId = box.BoxId;
                ConveyorBoxes.RemoveAt(i);
                if (_isRunning) CompleteGita(exitBoxId);
            }
        }
    }

    // ── 기타 분류 처리 (4개 분류지점 통과 박스) ─────────────────────
    private void CompleteGita(int boxId)
    {
        // 기타는 PLC에서 무게 제공 없음 → weight=0으로 저장
        var stat = RegionStats.FirstOrDefault(s => s.RegionCode == 5);
        if (stat != null) stat.Count++;
        TotalProcessed++;
        UpdateThroughput();

        if (EventLogs.Count >= 50) EventLogs.RemoveAt(EventLogs.Count - 1);
        EventLogs.Insert(0, new EventLogEntry
        {
            Timestamp  = DateTime.Now,
            BoxId      = boxId,
            RegionName = "기타",
            Weight     = 0,
            Status     = "기타",
        });
        OnPropertyChanged(nameof(HasEvents));
        _ = SaveParcelToDbAsync(boxId, 5, "기타", 0);
    }

    // ── 푸셔 플래시 ─────────────────────────────────────────────────
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
        int boxId, int regionCode, string regionName, double weight)
    {
        var parcelId = await _db.SaveParcelAsync(boxId, regionCode, regionName, weight);
        if (parcelId > 0)
        {
            await _db.SaveSortEventAsync(parcelId, "SORT_COMPLETE",
                $"{regionName} / {weight:F1}kg");
            if (!_isDbConnected) IsDbConnected = true;
        }
    }

    private async Task SaveAlarmToDbAsync(int code, string message)
    {
        _lastAlarmId = await _db.SaveAlarmAsync(code, message);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
