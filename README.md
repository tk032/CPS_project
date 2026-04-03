# SmartFactory CPS

> 택배 자동 분류 시스템의 **디지털 트윈 CPS(Cyber-Physical System)** WPF 애플리케이션
>
> XG5000 PLC와 Factory IO를 Modbus TCP로 실시간 연동하고, 분류 결과를 Supabase(PostgreSQL) 클라우드 DB에 저장해 팀원 간 데이터를 공유한다.

<br>

---

## 목차

1. [시스템 구성](#시스템-구성)
2. [기술 스택](#기술-스택)
3. [시작하기](#시작하기)
4. [주요 기능](#주요-기능)
5. [폴더 구조](#폴더-구조)
6. [Supabase DB 구조](#supabase-db-구조)
7. [Modbus 주소 맵](#modbus-주소-맵)
8. [Factory IO 설정](#factory-io-설정)
9. [XG5000 래더 로직](#xg5000-래더-로직)
10. [화면 미리보기](#화면-미리보기)

<br>

---

## 시스템 구성

```
[ Factory IO ]  ──Modbus TCP──  [ XG5000 PLC ]  ──Modbus TCP──  [ WPF CPS ]  ──Npgsql──  [ Supabase ]
  가상 컨베이어                    래더 로직 실행                   디지털 트윈                클라우드 DB
  박스 생성·이동                   무게·누적·팔레트 계산              UI·통계·알람               분류 이력 저장
```

- **Factory IO** — 가상 컨베이어 환경. 박스 생성, 이동, 분류 동작을 시뮬레이션한다.
- **XG5000 PLC** — RFID로 박스 목적지를 읽고 푸셔를 제어한다. 누적무게·팔레트 수를 래더 로직으로 계산해 WW 레지스터에 저장한다.
- **WPF CPS** — PLC를 100ms 주기로 폴링해 컨베이어 상태를 실시간으로 화면에 표시하고 Supabase에 저장한다.
- **Supabase** — PostgreSQL 클라우드 DB. 팀원마다 LOC 번호로 데이터를 분리 관리한다.

<br>

---

## 기술 스택

| 구분 | 내용 |
|------|------|
| 플랫폼 | .NET 10 WPF |
| 아키텍처 | MVVM (INotifyPropertyChanged) |
| PLC 통신 | EasyModbusTCP 5.6.0 — Modbus TCP, 100ms 폴링 |
| DB | Supabase PostgreSQL — Npgsql 9.0.3 |

<br>

---

## 시작하기

### 사전 요구사항

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Factory IO (가상 컨베이어 실행용)
- XG5000 (PLC 래더 로직 작성·다운로드용)
- Supabase 계정 및 프로젝트

<br>

### 1. DbConfig.cs 생성

`Services/DbConfig.cs`는 비밀번호 보호를 위해 `.gitignore`에 등록되어 있으므로 **팀원 각자 직접 생성**해야 한다.

```csharp
namespace SmartFactoryCPS.Services;

public static class DbConfig
{
    public const string ConnectionString =
        "Host=<host>;Port=6543;Database=postgres;" +
        "Username=<user>;Password=<password>;SSL Mode=Require;";
}
```

> Supabase 대시보드 → Project Settings → Database → **Session Pooler** 연결 문자열 사용 (포트 6543)

<br>

### 2. LOC 번호 설정

앱 실행 후 헤더의 **LOC** 입력란에 팀원마다 고유한 번호(1~5)를 입력한다.
같은 Supabase DB를 사용하더라도 LOC 번호 기준으로 데이터가 분리되어 섞이지 않는다.

| LOC | 담당자 |
|-----|--------|
| 1 | 팀원 1 |
| 2 | 팀원 2 |
| 3 | 팀원 3 |
| 4 | 팀원 4 |
| 5 | 팀원 5 |

<br>

### 3. PLC 연결 및 실행

1. Factory IO 씬과 XG5000 래더 로직을 먼저 실행한다.
2. 앱 헤더의 **IP · Port** 입력란에 PLC 주소를 입력한다. (기본값: `192.168.200.137:502`)
3. **시작** 버튼을 누르면 Modbus TCP 연결 및 폴링이 시작된다.
4. **정지** 버튼으로 연결을 끊고, **리셋** 버튼으로 통계·알람을 초기화한다.

<br>

---

## 주요 기능

### 대시보드
- **컨베이어 애니메이션** — 박스가 진입해 서울·대전·대구·부산·기타 구간으로 이동하는 모습을 960px Canvas에 실시간 표시
- **지역별 누적 통계** — 분류 수량, 누적 무게(kg), 팔레트 수를 지역별로 집계 (기타는 수량만 표시)
- **이벤트 로그** — 최근 50건의 분류 이력(박스 ID, 지역, 무게, 상태)을 실시간으로 표시

### 관리자 페이지
- **분류 이력 조회** — 현재 LOC의 parcel 데이터를 최대 200건 조회
- **검색 및 필터** — 박스 ID·지역명 키워드 검색, 지역 필터
- **삭제** — 선택 항목 삭제 또는 현재 LOC 전체 초기화

### 알람
- 푸셔(DI17~20) 또는 분류 구간(DI21~24)에서 박스가 **3초 이상** 감지되면 헤더에 경고 메시지 표시
- 걸림이 해소(DI LOW 복귀)되면 자동 해제
- 여러 구간에서 동시에 발생 가능하며, 리셋 버튼으로 전체 초기화

### 연결 상태 모니터링
- 헤더 우측에 PLC 연결 상태 및 DB 연결 상태를 실시간 표시
- PLC 연결 끊김 시 3초 후 자동 재연결 시도
- DB 저장 실패 시 오류 메시지 표시

<br>

---

## 폴더 구조

```
project_CPS/
├── Services/
│   ├── DbConfig.cs          
│   ├── DbService.cs         ← Supabase CRUD (조회·저장·삭제)
│   └── FactoryIoService.cs  ← Modbus TCP 폴링 + 박스 감지 + 알람
├── ViewModels/
│   ├── MainViewModel.cs     ← 대시보드 상태·로직 전체 담당
│   ├── AdminViewModel.cs    ← 관리 페이지 (LOC 주입 방식)
│   ├── Models.cs            ← BoxInfo, RegionStat, EventLogEntry 등
│   └── AllConverters.cs     ← XAML 바인딩용 IValueConverter 모음
├── Views/
│   ├── DashboardView.xaml   ← 대시보드 UI
│   └── AdminView.xaml       ← 관리자 UI
├── App.xaml                 
├── MainWindow.xaml          ← 헤더·사이드바·ContentControl
└── SmartFactoryCPS.csproj
```

<br>

---

## Supabase DB 구조

```
loc          (id, name)                     ← LOC 마스터 (팀원 구분)
region_rule  (region_code, region_name)     ← 지역 코드 마스터
parcel       (id, box_id, region_code, region_name, weight_kg, sort_result, loc_id, sorted_at)
```

**뷰**: `parcel_loc1` ~ `parcel_loc5` — Supabase 대시보드에서 LOC별로 바로 확인 가능

<br>

---

## Modbus 주소 맵

| 구분 | FC | 주소 | 내용 |
|------|----|------|------|
| CmdId | FC4 | MW0 | RFID 명령 ID |
| 지역코드 | FC4 | MW16 | 박스 목적지 (1=서울, 2=대전, 3=대구, 4=부산) |
| 개별무게 | FC4 | MW20~23 | 서울~부산 박스 개별 무게 |
| Diffuse 센서 | FC2 | IX5~8 | 컨베이어 박스 진입 감지 |
| 푸셔 감지 | FC2 | IX17~20 | 서울~부산 푸셔 동작 감지 |
| 분류 완료 | FC2 | IX21~24 | 서울~부산 분류 완료 트리거 |
| 누적무게 | FC3 | WW22~25 | XG5000 래더가 계산한 지역별 누적무게 |
| 팔레트 수 | FC3 | WW26~29 | 누적무게 1000kg 초과 시 증가 |

<br>

---

## Factory IO 설정

> 추후 작성 예정

<!-- Factory IO 씬 구성, 사용한 센서·액추에이터, Modbus 드라이버 설정 등 -->

<br>

---

## XG5000 래더 로직

> 추후 작성 예정

<!-- 래더 로직 구조, RFID 읽기 로직, 푸셔 제어, 누적무게·팔레트 계산 로직 등 -->

<br>

---

## 화면 미리보기

### 대시보드

실시간 컨베이어 애니메이션, 지역별 누적 통계(수량·무게·팔레트), 이벤트 로그를 한 화면에서 확인

![대시보드](https://github.com/user-attachments/assets/8aa57fd3-73de-49be-847b-3ece52d7ab9a)

<br>

### 관리자 페이지

LOC별 분류 이력 조회·검색·삭제 및 전체 데이터 초기화

![관리자페이지](https://github.com/user-attachments/assets/4bf56c66-710c-4bd0-83f7-d9fe2ab30b90)

<br>

### 알람 경고

푸셔 또는 분류 구간에서 박스가 3초 이상 감지되면 헤더에 경고 표시, 해소 시 자동 해제

![알람경고](https://github.com/user-attachments/assets/15b66f07-4d83-4d9d-b03a-35b18b0a9610)
