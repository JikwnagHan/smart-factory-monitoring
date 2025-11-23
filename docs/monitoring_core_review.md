# Monitoring.Core 서비스 구조 검토

본 문서는 `docs/스마트제조_장비모니터링_시스템_설계서_v0.1.docx`의 2.2 모듈 구성과 3.2 기능 요구사항을 근거로 Monitoring.Core 서비스 계층의 구성을 점검하고, 필요한 개선 사항을 제안한다.

## 현재 설계와의 정합성 평가

- **핵심 서비스 세분화**: 설계서에서는 Core 계층에 `DeviceService`, `TagService`, `DataLogService`, `AlarmService`, `ControlCommandService`를 명시한다. 이들은 각각 장치/태그 설정, 실시간·이력 수집, 알람 처리, 제어 명령 처리 책임을 갖도록 분리하는 것이 적절하다. 데이터·알람·제어 흐름을 서비스별로 구분하면 플러그인형 어댑터와 UI/API 계층 변경에 대한 영향 범위가 최소화된다.
- **백그라운드 워커**: 3.2.1에 언급된 “수집 워커”, 3.2.4의 알람 처리, 3.2.3의 명령 큐 처리를 위한 워커를 Core에서 호스팅하는 구조가 적합하다. `IHostedService` 기반 워커로 구현하고, 각 워커는 서비스 계층만 의존하도록 하여 UI·API와 분리된 스케줄링이 가능하도록 해야 한다.
- **데이터 흐름 정렬**: 설계서의 단계별 흐름(① 설정 로딩 → ② 어댑터 수집 → ③ 실시간/이력 저장 → ④ 알람 평가 → ⑤ 명령 처리 → ⑥ Open API 제공)에 맞춰 서비스 간 의존성을 단방향으로 유지하는 것이 바람직하다. 예를 들어 `DataCollectionWorker` → `TagService`/`DataLogService` → `AlarmService` → `ControlCommandService` 순으로 호출 경로를 제한하면 순환 의존을 방지할 수 있다.

## 권장 서비스 클래스 구성

| 범주 | 클래스/컴포넌트 | 주요 책임 |
| --- | --- | --- |
| 설정/메타데이터 | `DeviceService` | 장치 등록·조회·상태 플래그 관리, 어댑터 인스턴스 생성용 메타데이터 제공 |
|  | `TagService` | 태그 정의 로딩/캐시, 데이터 품질 규칙 제공 |
| 수집/저장 | `DataCollectionWorker` | 활성 장치/태그 스케줄링, 어댑터를 통한 읽기, 결과를 `DataLogService`에 전달 |
|  | `DataLogService` | 실시간 테이블 최신값 반영, 이력 Append, 보존 정책 적용 |
| 알람 | `AlarmService` | 알람 조건 평가, 발생/해제 기록, Ack 처리 |
| 제어 | `ControlCommandService` | Pending 명령 조회, 어댑터 쓰기/응답 처리, 스케줄 명령 생성기와 연계 |
| 지원/공통 | `AdapterFactory` | 설정 기반으로 `IDeviceAdapter` 구현체 생성 및 수명 관리 |
|  | `EventLogService` | 시스템·설정 변경 이벤트 기록 (3.2.4 기타 이벤트) |

### 설계 원칙 반영

- **모듈성**: 서비스 간 의존성은 인터페이스 기반으로 주입하여, 새로운 통신 프로토콜 추가 시 `AdapterFactory`와 `IDeviceAdapter` 구현만 확장한다.
- **설정 기반**: `DeviceService`/`TagService`는 DB·설정에서 최신 구성을 로딩하고 캐싱하며, 변경 시 워커가 재로딩할 수 있는 이벤트/토큰을 제공한다.
- **테스트 용이성**: 각 서비스는 어댑터·DB 저장소를 인터페이스로 주입받아 시뮬레이터/Mock으로 단위 테스트가 가능하도록 한다.

## 개선 제안

1. **AdapterFactory와 Core 서비스 연결 명확화**: 수집·제어 워커가 어댑터 인스턴스를 직접 생성하지 않고 `AdapterFactory`를 통해 요청하도록 명세한다.
2. **데이터 품질/알람 기준 단일화**: `TagService`가 태그별 품질 기준(Timeout, Retry, 범위)을 제공하고 `AlarmService`가 이를 활용하도록 계약을 정의한다.
3. **명령 스케줄러 분리**: 3.2.3의 배치 제어 요구에 따라 `ScheduledCommandWorker`를 별도 두어 `ControlCommandService`에 명령을 추가하도록 한다.
4. **상태 모니터링**: 모든 워커와 어댑터에서 상태/진단 이벤트를 `EventLogService`로 기록하도록 표준화한다.
