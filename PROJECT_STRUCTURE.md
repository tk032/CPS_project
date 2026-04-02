# SmartFactoryCPS 프로젝트 구조

택배 자동 분류 시스템의 디지털 트윈 CPS (Cyber-Physical System) WPF 애플리케이션.

---

## 폴더 구조

```
project_CPS/
├── Services/
│   ├── DbConfig.cs
│   ├── DbService.cs
│   └── FactoryIoService.cs
├── ViewModels/
│   ├── MainViewModel.cs
│   ├── AdminViewModel.cs
│   ├── Models.cs
│   └── AllConverters.cs
├── Views/
│   ├── DashboardView.xaml / .cs
│   └── AdminView.xaml / .cs
├── App.xaml / .cs
├── MainWindow.xaml / .cs
└── SmartFactoryCPS.csproj
```

> **git 제외 파일**: `Services/DbConfig.cs` (Supabase 비밀번호 포함 — 팀원 각자 생성)

---

## Services/

### DbConfig.cs
Supabase PostgreSQL 접속 정보를 담은 상수 클래스.
`.gitignore`에 등록되어 있으므로 git에 올라가지 않음.
팀원은 이 파일을 직접 생성하고 비밀번호를 입력해야 한다.

```csharp
public static class DbConfig
{
    public const string ConnectionString = "Host=...;Password=...;";
}
```

### DbService.cs
Supabase(PostgreSQL) CRUD 서비스.
`Npgsql` 라이브러리를 사용하며, 모든 메서드는 예외를 throw하지 않고 `false`/`-1`을 반환한다.

| 메서드 | 설명 |
|--------|------|
| `GetLastBoxIdAsync(locId)` | 해당 LOC의 마지막 박스 ID 조회 (시작 시 이어서 카운트하기 위해 사용) |
| `TestConnectionAsync()` | DB 연결 상태 확인 |
| `SaveParcelAsync(...)` | 박스 분류 완료 시 parcel 테이블에 저장 |
| `GetParcelsAsync(...)` | 분류 이력 조회 (키워드·지역·LOC 필터) |
| `DeleteParcelsAsync(ids)` | 선택한 이력 다건 삭제 |
| `ClearAllDataAsync(locId)` | 해당 LOC의 parcel 데이터 전체 삭제 |

### FactoryIoService.cs
Factory IO Modbus TCP 폴링 서비스 (100ms 주기).
`EasyModbusTCP` 라이브러리를 사용하며, 연결 끊김 시 3초 후 자동 재연결한다.

**읽기 방식**
| FC | 주소 | 내용 |
|----|------|------|
| FC4 ReadInputRegisters | MW0~24 | CmdId, 개별 박스 무게 (MW20~23) |
| FC2 ReadDiscreteInputs | IX0~24 | Diffuse 센서 (IX5~8), 분류 트리거 (IX21~24) |
| FC3 ReadHoldingRegisters | WW22~29 | 누적무게 (WW22~25), 팔레트 수 (WW26~29) |

**이벤트**
| 이벤트 | 트리거 | 전달 데이터 |
|--------|--------|------------|
| `BoxEntered` | Diffuse1(IX5) 상승 에지 + CmdId 변화 | regionCode, regionName |
| `BoxSorted` | IX21~24 상승 에지 | regionCode, regionName, boxWeight, cumWeight, paletteCount |
| `DiffuseSensorsChanged` | 매 폴링 | Diffuse1~4 ON/OFF 상태 |
| `ConnectionChanged` | 연결/끊김 | bool |

---

## ViewModels/

### MainViewModel.cs
메인 화면의 모든 상태와 로직을 담당하는 ViewModel.
`INotifyPropertyChanged` 구현체.

**주요 기능**
- PLC IP / Port / LOC 번호 입력 및 시작·정지·리셋 커맨드
- `FactoryIoService` 이벤트 수신 → 컨베이어 애니메이션, 통계 업데이트, DB 저장
- 지역별 누적 통계 (`RegionStats`) 관리
- 이벤트 로그 최근 50건 유지
- 기타 박스 처리 (`CompleteGita`) — 컨베이어 끝까지 이동한 박스
- 알람 임계치 감지 (세션 누적 30건 초과 시 알람)

**LOC 분리**
`LocId` 입력값 기준으로 DB 저장/조회를 분리한다.
팀원 각자 다른 LOC 번호를 사용하면 Supabase에서 데이터가 섞이지 않는다.

### AdminViewModel.cs
관리자 페이지 ViewModel.
`Func<int> getLocId`를 주입받아 현재 LOC 번호를 실시간으로 참조한다.
(새로고침 버튼 클릭 시 MainWindow의 LOC 입력값을 즉시 반영)

**기능**
- 분류 이력 조회·삭제 (지역 필터, 키워드 검색)
- 전체선택 / 선택 삭제
- 현재 LOC 데이터 전체 초기화

### Models.cs
UI 바인딩에 사용되는 데이터 모델 클래스 모음.

| 클래스 | 설명 |
|--------|------|
| `BoxInfo` | 현재 처리 중인 박스 정보 (상단 카드 표시용) |
| `EventLogEntry` | 이벤트 로그 한 줄 항목 |
| `RegionStat` | 지역별 누적 통계 (수량·무게·팔레트) — `INotifyPropertyChanged` |
| `ConveyorBoxVm` | 컨베이어 위에서 이동 중인 박스 — X 좌표 애니메이션용 |

### AllConverters.cs
XAML 바인딩에 사용되는 `IValueConverter` 구현체 모음.
`App.xaml`에 전역 리소스로 등록되어 있다.

| 클래스 | Key | 용도 |
|--------|-----|------|
| `RunningToStatusDotColorConverter` | `RunningToColor` | bool → 상태 점 색상 (초록/회색) |
| `StatusToBadgeBackgroundConverter` | `BadgeBg` | 상태 문자열 → 배지 배경색 |
| `StatusToBadgeForegroundConverter` | `BadgeFg` | 상태 문자열 → 배지 글자색 |
| `InverseBoolToVisibilityConverter` | `InvBoolVis` | bool 반전 → Visibility |
| `AlarmToPanelBackgroundConverter` | `AlarmBg` | 알람 활성 여부 → 패널 배경색 |
| `EventStatusToColorConverter` | `EventStatusColor` | 이벤트 상태 → 전경색 |

---

## Views/

### DashboardView.xaml
메인 대시보드 화면. `MainViewModel`에 바인딩된다.

**구성 요소**
- 현재 박스 정보 카드 (BoxId, 지역, 무게, 상태)
- 공정 상태 카드 (Running/Stopped, 처리량/분, Diffuse 센서 스캔 표시)
- 알람 패널
- 컨베이어 벨트 애니메이션 (960px Canvas, 서울~부산 푸셔 + 기타 낙하)
- 지역별 누적 통계 카드 (서울·대전·대구·부산·기타) — 기타는 수량만 표시
- 이벤트 로그 (최근 50건)

### AdminView.xaml
관리자 페이지. `AdminViewModel`에 바인딩된다.

**구성 요소**
- 현재 LOC 번호 표시 (LOC-N 배지)
- 분류 이력 DataGrid (지역 필터, 키워드 검색, 전체선택/선택삭제)
- LOC 데이터 전체 초기화 버튼

---

## 루트 파일

### App.xaml / App.xaml.cs
WPF 애플리케이션 진입점.
전역 리소스(컨버터, 스타일, DataTemplate)를 정의한다.
`MainViewModel` → `DashboardView`, `AdminViewModel` → `AdminView` DataTemplate 매핑.

### MainWindow.xaml / MainWindow.xaml.cs
앱의 단일 Window. 헤더와 사이드 네비게이션을 포함한다.

**헤더**: 시작·정지·리셋 버튼 / PLC IP·Port·LOC 입력 / 상태 표시줄 (PLC 연결, DB 연결, 알람, 오류)
**사이드바**: 대시보드 / 관리 네비게이션 버튼
**바디**: `ContentControl`에 현재 View를 동적으로 렌더링

### SmartFactoryCPS.csproj
.NET 10 WPF 프로젝트 파일.

| 패키지 | 용도 |
|--------|------|
| `EasyModbusTCP 5.6.0` | Factory IO Modbus TCP 통신 |
| `Npgsql 9.0.3` | Supabase(PostgreSQL) DB 연결 |
| `System.IO.Ports 10.0.5` | (예비) 시리얼 포트 |

---

## Supabase DB 구조

```
loc          (id, name)                          ← LOC 마스터
region_rule  (region_code, region_name, ...)     ← 지역 코드 마스터
parcel       (id, box_id, region_code, region_name, weight_kg, sort_result, loc_id, sorted_at)
```

**뷰 (View)**
`parcel_loc1` ~ `parcel_loc5` — LOC별 parcel 필터 뷰 (Supabase 대시보드에서 LOC별로 바로 확인 가능)
