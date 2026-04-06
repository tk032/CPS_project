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

### Scene 구성

![facio1](https://github.com/user-attachments/assets/fa289ac8-09c3-4c22-bbff-7367f8f74757)

- 우측 출발점에서 택배 상자를 생성

- 2개의 RFID리더를 통해 지역코드(1~4)와 무게(40~70kg)를 부여
- 지역코드에 따라 4갈래로 분류
- 분류 실패 또는 오류 발생시 분류되지 않고 메인레일 좌측 끝의 Reject로 이동
- 분류된 택배는 무게를 누적하여 트레일러의 적재량을 모니터링



### Sensor

![facio2](https://github.com/user-attachments/assets/24b40193-b7d6-4cde-859f-ca76f11c8da2)

- (좌) RFID Reader
  - 로직에 따라 개체(택배상자)에 데이터를 심거나 불러오는 기능 수행
- (우) Diffuse Sensor
  - 개체(택배상자)의 현 위치에 따라 로직을 실행하기 위한 로직 트리거 역할 수행



### Factory I/O Drivers 설정

![driver1](https://github.com/user-attachments/assets/bef8c1c8-8af6-4a93-8e07-4fccb0646d3b)

![driver2](https://github.com/user-attachments/assets/a32da454-2f67-4931-ba55-88f0fcbab7f6)



<br>

---

## XG5000 래더 로직

### 로직 구조

크게 RFID 쓰기, RFID 읽기, Pusher 제어, 무게합 계산 으로 나눌 수 있음.

#### RFID 쓰기

![rfid쓰기](https://github.com/user-attachments/assets/c54be60f-6951-4c23-bed2-e9189aefe5fb)

- RFID 리더에 3번 명령(write data) 및 Memory Index를 0번으로 지정
- 로직에 따라 처리되어 D100에 저장해둔 랜덤 지역코드를 해당 위치에 저장
- 무게 저장의 경우도 동일한 방식으로 처리



#### RFID 읽기

![rfid읽기](https://github.com/user-attachments/assets/53298fab-7793-4a07-9092-0e68bbb35095)

- RFID리더에 2번 명령(Read data) 및 Memory Index를 0번으로 지정
- 해당 값을 읽어 PLC메모리에 저장
- 무게 읽기의 경우에도 동일한 방식으로 처리



#### Pusher 제어

![푸셔로직](https://github.com/user-attachments/assets/d547998c-c407-482f-a0e8-a276940f3bf5)

- diffuse sensor에 신호가 잡히면 pusher가 limit 거리까지 작동 후 limit에 닿으면 신호를 off하여 원위치
- 'sensor에 신호가 잡히면 작동한다' 로 끝낼 경우 작은 택배가 지나가면 pusher가 완전히 동작하기 전에 신호가 꺼질 우려 있음



#### 무게합 모니터링

![무게읽기2](https://github.com/user-attachments/assets/ce2577d3-83d3-49a6-be06-91215ac0a0a9)

![무게읽기2](https://github.com/user-attachments/assets/4ecf054c-77c1-4614-901b-01994685176e)

- 무게합을 메모리에 누적하여 모니터링하다가 적재 제한 (1000kg)을 초과하면 한 팔레트를 채웠음으로 간주하고 팔레트(트레일러) 카운트를 +1, 무게합은 0으로 초기화

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
