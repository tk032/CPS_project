namespace SmartFactoryCPS.Services;

/// <summary>
/// XG5000 PLC Modbus TCP 주소 맵 (cpslogic.xgwx + cps_01.factoryio 기준)
///
/// [아키텍처]
///   Factory I/O ←(LSIMOD Ethernet)→ XG5000 PLC ←(Modbus TCP)→ WPF
///
/// [LSIMOD DI 비트 맵 — FC2 ReadDiscreteInputs]
///   DI0  : FACTORY I/O Paused
///   DI1  : FACTORY I/O Reset
///   DI2  : FACTORY I/O Running
///   DI3  : write00 — 입구 박스 감지 센서 (BoxEntered 트리거)
///   DI4  : write01
///   DI5  : Diffuse Sensor 1 — 분류지점1 박스 감지
///   DI6  : Diffuse Sensor 2 — 분류지점2 박스 감지
///   DI7  : Diffuse Sensor 3 — 분류지점3 박스 감지
///   DI8  : Diffuse Sensor 4 — 분류지점4 박스 감지
///   DI9  : Diffuse Sensor 5 — 분류함1(서울) 박스 감지
///   DI10 : Diffuse Sensor 6 — 분류함2(대전) 박스 감지
///   DI11 : Diffuse Sensor 7 — 분류함3(대구) 박스 감지
///   DI12 : Diffuse Sensor 8 — 분류함4(부산) 박스 감지
///
/// [LSIMOD 입력 레지스터 맵 — FC3 ReadHoldingRegisters]
///   MW0~7   : RFID Reader 1~8 Command ID
///   MW8~15  : RFID Reader 1~8 Status
///   MW15    : RFID Reader 1 Read Data (지역코드: 1=서울,2=대전,3=대구,4=부산)
///   MW20~23 : RFID Reader 5~8 Read Data (개별 박스 무게 kg)
///   MW24    : 수하물 총 처리량
///   MW101~104 : WeightSum1~4 (지역별 누적 무게합)
/// </summary>
public static class PlcAddr
{
    /// <summary>XG5000 PLC IP (Factory I/O PC IP 아님)</summary>
    public const string PlcIp = "192.168.200.137";
    public const int    Port  = 502;

    // ── FC2 ReadDiscreteInputs — LSIMOD DI 주소 ──────────────────────

    /// <summary>DI3 : write00 — 입구 박스 감지 센서 (BoxEntered 트리거)</summary>
    public const int DiWrite00   = 3;

    /// <summary>DI5  : Diffuse Sensor 1 — 분류지점1 박스 감지 (BoxEntered 트리거)</summary>
    public const int DiDiffuse1  = 5;
    /// <summary>DI6  : Diffuse Sensor 2 — 분류지점2 박스 감지</summary>
    public const int DiDiffuse2  = 6;
    /// <summary>DI7  : Diffuse Sensor 3 — 분류지점3 박스 감지</summary>
    public const int DiDiffuse3  = 7;
    /// <summary>DI8  : Diffuse Sensor 4 — 분류지점4 박스 감지</summary>
    public const int DiDiffuse4  = 8;

    /// <summary>DI9  : Diffuse Sensor 5 — 분류함1(서울) 내 박스 감지</summary>
    public const int DiDiffuse5  = 9;
    /// <summary>DI10 : Diffuse Sensor 6 — 분류함2(대전) 내 박스 감지</summary>
    public const int DiDiffuse6  = 10;
    /// <summary>DI11 : Diffuse Sensor 7 — 분류함3(대구) 내 박스 감지</summary>
    public const int DiDiffuse7  = 11;
    /// <summary>DI12 : Diffuse Sensor 8 — 분류함4(부산) 내 박스 감지</summary>
    public const int DiDiffuse8  = 12;

    /// <summary>ReadDiscreteInputs 읽기 개수 (DI0~DI12, 총 13비트)</summary>
    public const int DiReadCount = 13;

    // ── FC3 ReadHoldingRegisters — %MW 주소 ──────────────────────────

    /// <summary>MW15 : RFID Reader 1 Read Data — 지역코드 (1=서울,2=대전,3=대구,4=부산)</summary>
    public const int MwRfid1ReadData = 15;

    /// <summary>MW20 : RFID Reader 5 Read Data — 서울 분류함 개별 박스 무게(kg)</summary>
    public const int MwBoxWeight1 = 20;
    /// <summary>MW21 : RFID Reader 6 Read Data — 대전 분류함 개별 박스 무게(kg)</summary>
    public const int MwBoxWeight2 = 21;
    /// <summary>MW22 : RFID Reader 7 Read Data — 대구 분류함 개별 박스 무게(kg)</summary>
    public const int MwBoxWeight3 = 22;
    /// <summary>MW23 : RFID Reader 8 Read Data — 부산 분류함 개별 박스 무게(kg)</summary>
    public const int MwBoxWeight4 = 23;

    /// <summary>MW24 : 수하물 총 처리량</summary>
    public const int MwTotalProcessed = 24;

    /// <summary>MW101 : WeightSum1 — 서울 누적 무게합</summary>
    public const int MwWeightSum1 = 101;
    /// <summary>MW102 : WeightSum2 — 대전 누적 무게합</summary>
    public const int MwWeightSum2 = 102;
    /// <summary>MW103 : WeightSum3 — 대구 누적 무게합</summary>
    public const int MwWeightSum3 = 103;
    /// <summary>MW104 : WeightSum4 — 부산 누적 무게합</summary>
    public const int MwWeightSum4 = 104;
}
