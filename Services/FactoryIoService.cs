using EasyModbus;
using System.Windows;

namespace SmartFactoryCPS.Services;

/// <summary>
/// Factory IO Modbus TCP 폴링 서비스 (100ms 주기)
///
/// [읽기 방식]
///   FC4 ReadInputRegisters  → %MW0~24  (CmdId·개별무게 등)
///   FC2 ReadDiscreteInputs  → %IX0~17  (Diffuse 센서 + DI14~17)
///   FC3 ReadHoldingRegisters→ %WW22~25 (XG5000 래더 누적무게)
///
/// [BoxSorted 트리거]
///   DI14 상승 에지 → 서울 분류 완료 → MW20(개별무게) + WW22(누적무게) 읽기
///   DI15 상승 에지 → 대전
///   DI16 상승 에지 → 대구
///   DI17 상승 에지 → 부산
///
/// [BoxEntered]
///   DI5(Diffuse1) 상승 에지 → MW0 CmdId 변화 → MW16 지역코드 읽기
/// </summary>
public class FactoryIoService : IDisposable
{
    // ── 분류 채널 정의 (서울~부산, index 0~3) ─────────────────────────
    private static readonly (int sortDi, int weightMw, int region, string name)[] Channels =
    {
        (PlcAddr.DiSort1, PlcAddr.MwBoxWeight1, 1, "서울"),
        (PlcAddr.DiSort2, PlcAddr.MwBoxWeight2, 2, "대전"),
        (PlcAddr.DiSort3, PlcAddr.MwBoxWeight3, 3, "대구"),
        (PlcAddr.DiSort4, PlcAddr.MwBoxWeight4, 4, "부산"),
    };

    private readonly ModbusClient    _client = new();
    private CancellationTokenSource? _cts;
    private string _plcIp = PlcAddr.PlcIp;

    public bool IsConnected { get; private set; }

    // ── 이벤트 ───────────────────────────────────────────────────────
    public event Action<bool>?                       ConnectionChanged;
    public event Action<int, string>?                BoxEntered;
    public event Action<int, string, int, int, int>? BoxSorted;        // (region, name, boxWeight, cumWeight, paletteCount)
    public event Action<bool, bool, bool, bool>?     DiffuseSensorsChanged;
    public event Action<int, string>?                BoxStuck;          // (alarmCode, message)
    public event Action<int>?                        BoxStuckCleared;   // (alarmCode)

    // ── BoxEntered 전용 상태 ─────────────────────────────────────────
    private bool _waitCmd1;
    private int  _baseCmd1, _prevCmd1;

    // ── 이전 DI 상태 (상승 에지 감지용) ─────────────────────────────
    private bool[] _prevDi = new bool[PlcAddr.DiReadCount];

    // ── BoxStuck 감지용 (sort: code 1~4 / pusher: code 11~14) ────────
    private readonly DateTime?[] _sortHighSince   = new DateTime?[4];
    private readonly bool[]      _sortStuckFired  = new bool[4];
    private readonly DateTime?[] _pushHighSince   = new DateTime?[4];
    private readonly bool[]      _pushStuckFired  = new bool[4];
    private const double         StuckSeconds     = 3.0;

    public FactoryIoService()
    {
        _client.ConnectionTimeout = 3000;
        _client.Port              = PlcAddr.Port;
    }

    // ── 공개 API ─────────────────────────────────────────────────────

    public void Connect(string plcIp, int port = PlcAddr.Port)
    {
        try
        {
            _plcIp = _client.IPAddress = plcIp;
            _client.Port = port;
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
        _waitCmd1 = false;
    }

    // ── 폴링 루프 ────────────────────────────────────────────────────

    private async Task PollAsync(CancellationToken token)
    {
        // 초기값 읽기 — 연결 직후 상승 에지 오감지 방지
        try
        {
            _prevDi    = _client.ReadDiscreteInputs(0, PlcAddr.DiReadCount);
            int[] init = _client.ReadInputRegisters(0, 1);
            _prevCmd1  = init[PlcAddr.MwRfid1CmdId];
        }
        catch { _prevDi = new bool[PlcAddr.DiReadCount]; }

        while (!token.IsCancellationRequested)
        {
            try
            {
                // FC4 %MW0~24 (CmdId·개별무게 등)
                int[]  mw = _client.ReadInputRegisters(0, 25);
                // FC2 %IX0~17 (Diffuse 센서 + DI14~17 분류 트리거)
                bool[] di = _client.ReadDiscreteInputs(0, PlcAddr.DiReadCount);
                // FC3 %WW22~29 (누적무게 WW22~25 + 팔레트 WW26~29)
                // XG5000이 WW를 업데이트하는 데 한 스캔 주기가 필요하므로 200ms 대기
                await Task.Delay(200, token);
                int[]  ww = _client.ReadHoldingRegisters(PlcAddr.WwWeightSum1, 8);

                // ── BoxEntered: Diffuse1 상승 에지 → CmdId 변화 → 지역코드 읽기 ──

                int cmd1 = mw[PlcAddr.MwRfid1CmdId];
                if (_waitCmd1 && cmd1 != _baseCmd1)
                {
                    _waitCmd1 = false;
                    int rc = mw[PlcAddr.MwRfid1ReadData];
                    if (rc is >= 1 and <= 4)
                        InvokeOnUi(() => BoxEntered?.Invoke(rc, ToName(rc)));
                }
                if (Rising(di, _prevDi, PlcAddr.DiDiffuse1))
                { _waitCmd1 = true; _baseCmd1 = _prevCmd1; }

                // ── BoxSorted: DI21~24 상승 에지 → 개별무게 + 누적무게 읽기 ────
                // ── BoxStuck:  DI21~24 HIGH 3초 이상 → 박스 걸림 알람 (code 1~4) ─

                for (int i = 0; i < Channels.Length; i++)
                {
                    int  diIdx  = Channels[i].sortDi;
                    bool isHigh = diIdx < di.Length && di[diIdx];
                    int  code   = Channels[i].region;
                    string name = Channels[i].name;

                    if (Rising(di, _prevDi, diIdx))
                    {
                        int w = mw[Channels[i].weightMw];
                        int s = ww[i];
                        int p = ww[i + 4];
                        InvokeOnUi(() => BoxSorted?.Invoke(code, name, w, s, p));
                        _sortHighSince[i]  = DateTime.Now;
                        _sortStuckFired[i] = false;
                    }
                    else if (isHigh && _sortHighSince[i] != null && !_sortStuckFired[i])
                    {
                        if ((DateTime.Now - _sortHighSince[i]!.Value).TotalSeconds >= StuckSeconds)
                        {
                            _sortStuckFired[i] = true;
                            string msg = $"{name} 분류 구간 박스 걸림 감지";
                            InvokeOnUi(() => BoxStuck?.Invoke(code, msg));
                        }
                    }
                    else if (!isHigh)
                    {
                        if (_sortStuckFired[i])
                            InvokeOnUi(() => BoxStuckCleared?.Invoke(code));
                        _sortHighSince[i]  = null;
                        _sortStuckFired[i] = false;
                    }
                }

                // ── PusherStuck: DI17~20 HIGH 3초 이상 → 푸셔 박스 걸림 (code 11~14) ─

                for (int i = 0; i < Channels.Length; i++)
                {
                    int  diIdx  = PlcAddr.DiPushers[i];
                    bool isHigh = diIdx < di.Length && di[diIdx];
                    int  code   = 10 + Channels[i].region;
                    string name = Channels[i].name;

                    if (!isHigh && _pushHighSince[i] == null) continue;

                    if (isHigh && _pushHighSince[i] == null)
                    {
                        _pushHighSince[i]  = DateTime.Now;
                        _pushStuckFired[i] = false;
                    }
                    else if (isHigh && !_pushStuckFired[i])
                    {
                        if ((DateTime.Now - _pushHighSince[i]!.Value).TotalSeconds >= StuckSeconds)
                        {
                            _pushStuckFired[i] = true;
                            string msg = $"{name} 푸셔 박스 걸림 감지";
                            InvokeOnUi(() => BoxStuck?.Invoke(code, msg));
                        }
                    }
                    else if (!isHigh)
                    {
                        if (_pushStuckFired[i])
                            InvokeOnUi(() => BoxStuckCleared?.Invoke(code));
                        _pushHighSince[i]  = null;
                        _pushStuckFired[i] = false;
                    }
                }

                // ── UI: Diffuse1~4 스캔 표시 ──────────────────────────
                InvokeOnUi(() => DiffuseSensorsChanged?.Invoke(
                    di[PlcAddr.DiDiffuse1], di[PlcAddr.DiDiffuse2],
                    di[PlcAddr.DiDiffuse3], di[PlcAddr.DiDiffuse4]));

                // ── prev 업데이트 ──────────────────────────────────────
                _prevDi   = di;
                _prevCmd1 = cmd1;
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
                    _prevDi = _client.ReadDiscreteInputs(0, PlcAddr.DiReadCount);
                    InvokeOnUi(() => ConnectionChanged?.Invoke(true));
                }
                catch { _prevDi = new bool[PlcAddr.DiReadCount]; }
                continue;
            }

            await Task.Delay(100, token);
        }
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────

    private static string ToName(int code) => code switch
    {
        1 => "서울", 2 => "대전", 3 => "대구", 4 => "부산", _ => "기타"
    };

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

/// <summary>
/// Factory IO / XG5000 Modbus TCP 주소 상수
/// </summary>
public static class PlcAddr
{
    public const string PlcIp = "192.168.200.137";
    public const int    Port  = 502;

    // ── FC2 %IX — Diffuse 스캔 표시용 ────────────────────────────────
    public const int DiDiffuse1  = 5;
    public const int DiDiffuse2  = 6;
    public const int DiDiffuse3  = 7;
    public const int DiDiffuse4  = 8;

    // ── FC2 %IX — 푸셔 감지 (Input 17~20) ───────────────────────────
    public static readonly int[] DiPushers = { 17, 18, 19, 20 }; // 서울~부산

    // ── FC2 %IX — BoxSorted 트리거 (Input 21~24) ─────────────────────
    public const int DiSort1     = 21; // 서울 분류 완료
    public const int DiSort2     = 22; // 대전 분류 완료
    public const int DiSort3     = 23; // 대구 분류 완료
    public const int DiSort4     = 24; // 부산 분류 완료
    public const int DiReadCount = 25; // DI0~24

    // ── FC4 %MW — CmdId (BoxEntered용) ───────────────────────────────
    public const int MwRfid1CmdId    = 0;  // MW0
    public const int MwRfid1ReadData = 16; // MW16 지역코드

    // ── FC4 %MW — 개별 박스 무게 ─────────────────────────────────────
    public const int MwBoxWeight1 = 20; // MW20 서울
    public const int MwBoxWeight2 = 21; // MW21 대전
    public const int MwBoxWeight3 = 22; // MW22 대구
    public const int MwBoxWeight4 = 23; // MW23 부산

    // ── FC3 %WW — 누적무게 ────────────────────────────────────────────
    public const int WwWeightSum1 = 22; // %WW22 서울 누적
    public const int WwWeightSum2 = 23; // %WW23 대전 누적
    public const int WwWeightSum3 = 24; // %WW24 대구 누적
    public const int WwWeightSum4 = 25; // %WW25 부산 누적

    // ── FC3 %WW — 팔레트 수 (누적무게 1000 초과 시 XG5000 래더에서 증가) ─
    public const int WwPalette1 = 26; // %WW26 서울 팔레트
    public const int WwPalette2 = 27; // %WW27 대전 팔레트
    public const int WwPalette3 = 28; // %WW28 대구 팔레트
    public const int WwPalette4 = 29; // %WW29 부산 팔레트
}
