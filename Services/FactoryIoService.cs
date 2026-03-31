using EasyModbus;
using System.Windows;

namespace SmartFactoryCPS.Services;

/// <summary>
/// XG5000 PLC Modbus TCP 모니터링 서비스
///
/// [이벤트 흐름]
///   DiDiffuse1(DI5) 상승 → 600ms 대기 → MW15 읽기 → BoxEntered(regionCode, regionName)
///     - 모든 박스가 분류지점1을 통과하므로 BoxEntered 트리거로 사용
///     - 600ms = PLC 정렬대기(300ms) + RFID1 읽기(200ms) + 여유(100ms)
///
///   DiDiffuse5(DI9)  상승 → 500ms 대기 → MW20 + MW101 읽기 → BoxSorted(1, 서울,  weight, cumWeight)
///   DiDiffuse6(DI10) 상승 → 500ms 대기 → MW21 + MW102 읽기 → BoxSorted(2, 대전,  weight, cumWeight)
///   DiDiffuse7(DI11) 상승 → 500ms 대기 → MW22 + MW103 읽기 → BoxSorted(3, 대구,  weight, cumWeight)
///   DiDiffuse8(DI12) 상승 → 500ms 대기 → MW23 + MW104 읽기 → BoxSorted(4, 부산,  weight, cumWeight)
///     - 500ms = PLC RFID5~8 Execute 대기(300ms) + 읽기(200ms)
///     - MW20~23 = RFID5~8 Read Data (개별 박스 무게 kg)
///     - MW101~104 = WeightSum1~4 (지역별 누적 무게합, UI 표시용)
/// </summary>
public class FactoryIoService : IDisposable
{
    private readonly ModbusClient    _client = new();
    private readonly SemaphoreSlim   _lock   = new(1, 1);
    private CancellationTokenSource? _cts;

    // 이전 폴링 값 — 상승엣지 감지용
    private bool[] _prevDi = new bool[PlcAddr.DiReadCount];

    public bool IsConnected { get; private set; }

    // ── 이벤트 ───────────────────────────────────────────────────────
    public event Action<bool>?                   ConnectionChanged;
    /// <summary>DiDiffuse1 감지 후 RFID1 읽기 완료 시 발생 (regionCode, regionName)</summary>
    public event Action<int, string>?            BoxEntered;
    /// <summary>DiDiffuse5~8 감지 후 RFID5~8 읽기 완료 시 발생
    /// (regionCode, regionName, boxWeightKg, cumWeightKg)</summary>
    public event Action<int, string, int, int>?  BoxSorted;
    /// <summary>Diffuse Sensor 1~4 실시간 상태 (UI 센서 점등용)</summary>
    public event Action<bool, bool, bool, bool>? DiffuseSensorsChanged;

    // 지역코드 → 지역명 (MW16 기준)
    private static readonly Dictionary<int, string> RegionNames = new()
    {
        { 1, "서울" }, { 2, "대전" }, { 3, "대구" }, { 4, "부산" }, { 5, "기타" },
    };

    // 분류함별 고정 지역 정보 (Diffuse5~8 위치 기반)
    private static readonly (int code, string name, int mwWeight, int mwSum)[] SortedRegions =
    {
        (1, "서울", PlcAddr.MwBoxWeight1, PlcAddr.MwWeightSum1),
        (2, "대전", PlcAddr.MwBoxWeight2, PlcAddr.MwWeightSum2),
        (3, "대구", PlcAddr.MwBoxWeight3, PlcAddr.MwWeightSum3),
        (4, "부산", PlcAddr.MwBoxWeight4, PlcAddr.MwWeightSum4),
    };

    public FactoryIoService()
    {
        _client.ConnectionTimeout = 3000;
        _client.Port              = PlcAddr.Port;
    }

    // ── 공개 API ─────────────────────────────────────────────────────

    // 현재 연결 IP (재연결 시 사용)
    private string _plcIp = PlcAddr.PlcIp;

    public void Connect(string plcIp, int port = PlcAddr.Port)
    {
        try
        {
            _plcIp            = plcIp;
            _client.IPAddress = plcIp;
            _client.Port      = port;
            _client.Connect();

            IsConnected = true;
            InvokeOnUi(() => ConnectionChanged?.Invoke(true));

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => PollAsync(_cts.Token));
        }
        catch
        {
            IsConnected = false;
            InvokeOnUi(() => ConnectionChanged?.Invoke(false));
        }
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        _cts = null;
        try { _client.Disconnect(); } catch { }
        IsConnected = false;
        InvokeOnUi(() => ConnectionChanged?.Invoke(false));
    }

    // ── 폴링 루프 (100ms) ────────────────────────────────────────────

    private async Task PollAsync(CancellationToken token)
    {
        // 연결 직후 현재 DI 상태를 _prev에 세팅 (오감지 방지)
        try
        {
            await _lock.WaitAsync(token);
            try   { _prevDi = _client.ReadDiscreteInputs(0, PlcAddr.DiReadCount); }
            finally { _lock.Release(); }
        }
        catch { _prevDi = new bool[PlcAddr.DiReadCount]; }

        while (!token.IsCancellationRequested)
        {
            try
            {
                bool[] di;
                await _lock.WaitAsync(token);
                try   { di = _client.ReadDiscreteInputs(0, PlcAddr.DiReadCount); }
                finally { _lock.Release(); }

                OnStateChanged(di, token);
                _prevDi = di;

                // 분류지점 센서 상태를 UI에 전달
                bool d1 = di[PlcAddr.DiDiffuse1];
                bool d2 = di[PlcAddr.DiDiffuse2];
                bool d3 = di[PlcAddr.DiDiffuse3];
                bool d4 = di[PlcAddr.DiDiffuse4];
                InvokeOnUi(() => DiffuseSensorsChanged?.Invoke(d1, d2, d3, d4));
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                IsConnected = false;
                InvokeOnUi(() => ConnectionChanged?.Invoke(false));

                try { await Task.Delay(3000, token); } catch { break; }

                try
                {
                    _client.IPAddress = _plcIp;
                    _client.Connect();
                    IsConnected = true;
                    _prevDi     = _client.ReadDiscreteInputs(0, PlcAddr.DiReadCount);
                    InvokeOnUi(() => ConnectionChanged?.Invoke(true));
                }
                catch { _prevDi = new bool[PlcAddr.DiReadCount]; }
                continue;
            }

            await Task.Delay(100, token);
        }
    }

    // ── 상태 변화 처리 ────────────────────────────────────────────────

    private void OnStateChanged(bool[] di, CancellationToken token)
    {
        // Diffuse1(DI13) 상승 → 모든 박스 감지 → BoxEntered
        if (Rising(di, _prevDi, PlcAddr.DiDiffuse1))
            _ = NotifyBoxEnteredAsync(token);

        // Diffuse5~8(DI9~12) 상승 → 각 분류함 RFID 읽기 완료 → BoxSorted
        if (Rising(di, _prevDi, PlcAddr.DiDiffuse5)) _ = NotifyBoxSortedAsync(0, token);
        if (Rising(di, _prevDi, PlcAddr.DiDiffuse6)) _ = NotifyBoxSortedAsync(1, token);
        if (Rising(di, _prevDi, PlcAddr.DiDiffuse7)) _ = NotifyBoxSortedAsync(2, token);
        if (Rising(di, _prevDi, PlcAddr.DiDiffuse8)) _ = NotifyBoxSortedAsync(3, token);
    }

    // ── Diffuse1 감지 → BoxEntered ────────────────────────────────────

    private async Task NotifyBoxEnteredAsync(CancellationToken token)
    {
        // PLC: Diffuse1 → 300ms 정렬대기 → 200ms RFID1 Execute → MW16 업데이트
        // 여유 포함 600ms 대기 후 MW16 읽기
        await Task.Delay(600, token);

        int regionCode;
        await _lock.WaitAsync(token);
        try   { regionCode = _client.ReadHoldingRegisters(PlcAddr.MwRfid1ReadData, 1)[0]; }
        finally { _lock.Release(); }

        if (!RegionNames.TryGetValue(regionCode, out var regionName)) return;
        InvokeOnUi(() => BoxEntered?.Invoke(regionCode, regionName));
    }

    // ── Diffuse5~8 감지 → RFID5~8 읽기 완료 → BoxSorted ─────────────

    private async Task NotifyBoxSortedAsync(int regionIdx, CancellationToken token)
    {
        // PLC: Diffuse5~8 → RFID5~8 Execute(200~500ms) → MW20~23 업데이트
        // 500ms 대기 후 읽기
        await Task.Delay(500, token);

        var (regionCode, regionName, mwWeight, mwSum) = SortedRegions[regionIdx];

        int boxWeight, cumWeight;
        await _lock.WaitAsync(token);
        try
        {
            boxWeight = _client.ReadHoldingRegisters(mwWeight, 1)[0]; // MW20~23
            cumWeight = _client.ReadHoldingRegisters(mwSum,    1)[0]; // MW101~104
        }
        finally { _lock.Release(); }

        InvokeOnUi(() => BoxSorted?.Invoke(regionCode, regionName, boxWeight, cumWeight));
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────

    private static bool Rising(bool[] cur, bool[] prev, int idx)
        => idx < cur.Length && idx < prev.Length && cur[idx] && !prev[idx];

    private static void InvokeOnUi(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) action();
        else d.Invoke(action);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _client.Disconnect(); } catch { }
    }
}
