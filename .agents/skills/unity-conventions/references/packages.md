# Unity 공식 패키지 지형

기준 버전: Unity 6000.x (6.5까지 반영) · 최종 확인: 2026-07

목차
- 버전과 릴리스 구조
- 패키지 성숙도와 버전 선택
- 신규 프로젝트 기본 패키지
- Package Manager 워크플로
- Assembly Definitions (asmdef / asmref)
- Input System
- Cinemachine 3.x
- Addressables
- Localization
- 직렬화
- 비동기: Awaitable / UniTask / Coroutine / Task
- ECS / DOTS
- UI: UI Toolkit vs uGUI
- 멀티플레이어 / 네트워킹
- 서드파티 라이브러리 위상 (2026)

Unity 6.x에서 코드를 쓸 때 참조하는 패키지 레퍼런스다. 어떤 패키지를 언제 쓰고, 어느 버전에 어떤 API가 있으며, AI가 흔히 저지르는 구버전 API·수명 관리 실수를 어떻게 피하는지 다룬다. 패키지 하나를 추가하거나, 세대 교체된 라이브러리(Input System, Cinemachine 3, R3 등) 코드를 짤 때 먼저 본다.

## 버전과 릴리스 구조

- Unity 6부터 릴리스는 Update 릴리스(연 수회)와 LTS(연 1회) 두 종류다. Update 릴리스도 다음 릴리스가 나오기 전까지 LTS와 동급으로 버그픽스·플랫폼 크리티컬 패치를 받는다.
- 타임라인: 6.0(첫 Unity 6, LTS) → 6.1 → 6.2 → 6.3(LTS) → 6.4(2026-03) → 6.5(2026-06). LTS 목표 버전은 6.0, 6.3, 6.6 식으로 연 1회다.
- 신 API가 필요하면 에디터를 올린다. 6.4 이후 ECS·수학 스택이 에디터 버전에 고정되므로(아래 참조) 패키지만 최신으로 올려 신 API를 쓰는 선택지가 줄었다.

## 패키지 성숙도와 버전 선택

### 성숙도 상태 (2021.1 체계, Unity 6 유지)
- **Released**: 검증 완료. 프로덕션 코드의 기본 선택.
- **Pre-release**: 부분 검증. 현재 에디터에서 안전하게 쓰도록 검증됐고 Unity가 공식 지원한다. 해당 연도 LTS 시점까지 프로덕션 검증이 완료된다. API가 바뀔 수 있으므로 LTS 목표 프로젝트에서만 신중히 쓴다.
- **Experimental**: 미래 지원 보장이 없다. 프로덕션에 쓰지 않는다. 영영 Released가 안 될 수 있다.
- **Deprecated**: 쓰지 않는다.
- "Verified"는 2019~2020 구 용어다. 지금은 Released + 에디터 번들 개념으로 대체됐다.

### Core packages는 에디터 버전에 고정된다
Core package는 에디터 각 버전과 함께 배포되며 Package Manager로 다른 버전을 선택할 수 없다. 버전을 바꾸려면 에디터를 바꾼다.

- **6.4**: ECS 스택(Entities, Collections, Entities Graphics)이 core로 편입돼 에디터와 함께 배포된다. 6.4 core 목록에는 URP, HDRP, SRP Core, Shader Graph, Visual Effect Graph, 2D Sprite, 2D Tilemap Editor, uGUI, Test Framework, UI Test Framework, Adaptive Performance, Multiplayer Center, Unity Denoising 등이 포함된다.
- **6.5**: `Unity.Mathematics`가 built-in module로 승격돼 설치 없이 기본 제공된다. Serialization 패키지가 core로 편입된다.
- 효과: ECS·수학 API 버전이 에디터 버전과 동기화돼 이식·온보딩 시 버전 불일치가 준다. AI는 "이 에디터 버전에 이 API가 있나"를 에디터 버전 기준으로 판단한다.

### 코드 작성 전 실제 버전 확인
네임스페이스·클래스 존재 여부를 기억이 아니라 프로젝트 파일로 확인한다.

- `Packages/manifest.json`: 프로젝트가 요청한 직접 의존성 버전.
- `Packages/packages-lock.json`: 해결된 실제 버전(전체 트리).
- API 존재는 `docs.unity3d.com/Packages/<pkg>@<major.minor>/api/`로 교차 확인한다.
- 세대 교체가 잦은 Input System, Cinemachine, Addressables, Entities는 major/minor를 반드시 확인한다.

## 신규 프로젝트 기본 패키지

장르 무관 사실상 표준:
- **Input System** (`com.unity.inputsystem`): 신규 입력. 구 Input Manager 대체. Released.
- **URP 또는 HDRP**: SRP가 기본 권장. 2D·모바일·대부분은 URP.
- **Test Framework** (core): 테스트.
- **Cinemachine 3.x** (`com.unity.cinemachine`): 카메라. 6.5부터 2.x 지원 종료.

상황별:
- **Addressables**: 에셋 로딩·메모리·DLC. Resources 대체. Released.
- **Localization** (`com.unity.localization`): 다국어. Released.
- **com.unity.nuget.newtonsoft-json**: 복잡한 JSON.
- **Burst / Jobs / Collections / Mathematics**: 성능 크리티컬 시. 상당수 6.4 core.
- **Netcode for GameObjects / Netcode for Entities + Unity Transport**: 멀티플레이어.

TextMeshPro의 6.4/6.5 정확한 패키지·통합 상태는 미확인이다. 실제 프로젝트 manifest로 확인한다.

## Package Manager 워크플로

### 파일 역할
- `Packages/manifest.json`: 요청한 직접 의존성 + scoped registry + lock/resolution 설정.
- `Packages/packages-lock.json`: 해결 결과(정확한 전체 의존성 트리). npm/yarn lock과 같은 개념.
- `Packages/` 폴더: embedded 패키지(소스 직접 포함) 위치.

### lock 파일은 반드시 커밋한다
`packages-lock.json`을 소스 관리에 커밋해야 시간·머신 간 동일 패키지 세트를 재현한다. lock이 없으면 registry의 새 패치가 해결돼 빌드가 달라진다. 팀·CI 재현성의 핵심이다.

### 의존성 방식
- **Git URL** (2019.3+): 간편하지만 버전 고정이 약하다. `#<tag>` 또는 커밋 해시로 고정하지 않으면 최신을 끌어온다. Git 의존성을 가진 커스텀 패키지는 지원되지 않는다.
- **Scoped registry** (OpenUPM 등): manifest에 scope를 지정한다. 버전 고정이 되지만 registry 가용성에 의존한다.
- **로컬 tarball / 경로**: 오프라인·포크용. 재현성은 좋으나 팀 공유 시 경로 문제가 있다.

### registry 패키지는 수정하지 않는다
registry 패키지는 `Library/PackageCache`의 read-only 캐시에 있다. 직접 편집하면 다음 resolve에서 덮어써지고 캐시가 오염된다.

```
Don't: Library/PackageCache/com.unity.foo@1.2.3/... 을 직접 편집
Do:    Packages/ 아래로 복사(embedded)한 뒤 수정, 또는 포크
```

## Assembly Definitions (asmdef / asmref)

asmdef 구조 설계(단방향 의존, 순환 끊기, `autoReferenced`/`noEngineReferences`)와 에디터 코드 격리는 build-project.md. 패키지 API를 쓸 때 자주 겪는 함정만 여기 둔다.

- **참조 누락**: `using`을 추가해도 asmdef의 References에 대상 어셈블리가 없으면 타입이 안 보인다("missing assembly reference"). 새 파일을 다른 asmdef 경계에 추가하거나 패키지 어셈블리를 처음 쓸 때 그 폴더 asmdef의 References를 확인한다.
- **Version Defines**: 특정 패키지가 특정 버전 이상일 때만 API를 쓰는 조건부 컴파일은 asmdef Inspector의 Version Defines로 관리한다. 세대 교체가 잦은 패키지의 신·구 API 분기에 쓴다.

## Input System

`com.unity.inputsystem`. Released. `UnityEngine.Input`(구 Input Manager) 대체.

구 API(`Input.GetKey`)와 혼용하려면 Project Settings > Player > Active Input Handling을 확인한다. 신 시스템 코드는 "Input System Package (New)" 또는 "Both"에서만 동작한다. 레거시 Input의 설정 종속과 Both 혼용 함정은 deprecated-api.md.

### 사용 방식과 트레이드오프
- **생성된 C# 래퍼 클래스** (Actions 에셋 > Generate C# Class): 타입 안전, 리팩토링 친화. 에셋 변경 시 재생성 필요.
- **InputActionReference 직접 바인딩**: Inspector 바인딩, 에셋과 느슨한 결합.
- **PlayerInput 컴포넌트**: Unity Events(Inspector 노출 편의) 또는 C# Events. 로컬 멀티플레이어(디바이스 페어링)·control scheme 전환 자동화에 유리하다.
- 성능 크리티컬 입력은 Unity Events보다 C# Events를 쓴다.

### 이벤트 vs 폴링
- 콜백: `action.started` / `action.performed` / `action.canceled`.
- 폴링: `action.ReadValue<T>()` (Update에서 매 프레임).
- 이산 트리거(점프·발사)는 이벤트(performed). 연속 값(이동·조준)은 폴링(ReadValue) 또는 performed에서 값 갱신 + canceled에서 리셋.
- **흔한 실수**: performed만 구독하고 canceled를 빠뜨려 버튼을 뗀 상태를 못 잡는다. 연속 이동 값이 0으로 안 돌아온다.

### 누수 방지
`OnEnable`에서 Enable·구독, `OnDisable`에서 Disable·구독 해제를 대칭으로 맞춘다. 빠뜨리면 파괴된 오브젝트 콜백 호출·입력 누수가 생긴다.

```csharp
void OnEnable()  { input.Enable();  input.Player.Jump.performed += OnJump; }
void OnDisable() { input.Disable(); input.Player.Jump.performed -= OnJump; }
```

## Cinemachine 3.x

2.x에서 API가 크게 바뀌었다. AI는 2.x 튜토리얼 코드를 그대로 쓰지 않는다.

### 2.x → 3.x 변경
- 네임스페이스: `Cinemachine` → `Unity.Cinemachine`.
- `CinemachineVirtualCamera` → `CinemachineCamera`.
- 필드 `m_` 접두사 제거 (예: `m_Lens` → `Lens`).
- 베이스 클래스: `CinemachineVirtualCameraBase` (`Unity.Cinemachine`).
- 커스텀 확장은 여전히 `CinemachineExtension` 상속(3.x 네임스페이스).

```csharp
// 2.x
using Cinemachine;
var cam = GetComponent<CinemachineVirtualCamera>();

// 3.x
using Unity.Cinemachine;
var cam = GetComponent<CinemachineCamera>();
```

### 마이그레이션과 판별
- 코드 자동 마이그레이션은 없다. 프로젝트 데이터 변환 툴(Cinemachine Upgrader)은 있으나 코드는 수동이다. Unity API updater가 전부 잡지 못한다.
- **6.5부터 Cinemachine 2 지원 종료** (6.3 LTS까지만 2 지원). 2는 공개 repo에 남아 포크는 가능하나 6.5+ 보증·버그픽스가 없다.
- 판별: `using Cinemachine;` + `CinemachineVirtualCamera`는 2.x, `using Unity.Cinemachine;` + `CinemachineCamera`는 3.x. 패키지 버전을 먼저 확인한다.
- 런타임 카메라 전환은 우선순위(Priority)나 `Follow`/`LookAt` 타깃을 스크립트로 설정한다. 3.x 런타임 제어 API 세부(우선순위·블렌드·타깃 프로퍼티명)는 설치 버전의 `com.unity.cinemachine@3.1` API 문서로 확인한다.

## Addressables

최신은 2.x 라인. Resources는 시작 시 전부 빌드되고 메모리 관리가 불리하므로 지연 로드·원격 배포·DLC가 필요하면 Addressables를 쓴다. 소규모·항상 로드되는 소량 에셋은 직접 참조 또는 `AssetReference`가 낫다.

### 핸들 수명 규칙 (핵심 버그원)
- 핸들을 결과 사용 기간 동안 필드에 보관한다. 미보관 시 결과 접근 불가·조기 GC가 발생한다.
- `LoadAssetAsync` → `Addressables.Release(handle)`로 해제한다.
- `InstantiateAsync` → `Addressables.ReleaseInstance(go)`로 해제한다(인스턴스 파괴 + 카운트 감소). 해제 책임이 LoadAsset과 다르다.
- 같은 키를 여러 번 로드하면 참조 카운트가 오른다. 로드 횟수만큼 Release한다.

```csharp
// Don't: 핸들 미보관 + 로드 완료 전 결과 접근
Addressables.LoadAssetAsync<Sprite>(key); // 핸들 버림 → 조기 GC
var s = handle.Result; // IsDone 확인 없이 접근 → null

// Do
AsyncOperationHandle<Sprite> handle;
async void Load() {
    handle = Addressables.LoadAssetAsync<Sprite>(key);
    var sprite = await handle.Task;
}
void OnDestroy() { if (handle.IsValid()) Addressables.Release(handle); }
```

- **흔한 실수**: 조기 해제(현재 인스턴스가 쓰는 데이터까지 언로드), 이중 해제(이미 해제한 핸들 재해제 오류), `Completed` 콜백과 `await` 혼용, `InstantiateAsync`에 GameObject 아닌 컴포넌트 제네릭 사용.
- content update는 빌드 후 `addressables_content_state.bin`을 보존해야 차등 업데이트가 된다. 코드보다 빌드 파이프라인 이슈다.

## Localization

`com.unity.localization`. Released. 구조: Locale, String/Asset Table, Table Collection. UI 문자열은 하드코딩하지 않고 테이블을 경유한다.

- 문자열 조회: `LocalizedString` 또는 `GetLocalizedStringAsync`(`AsyncOperationHandle<string>` 반환). Preload 모드거나 이미 로드됐으면 즉시 사용 가능하니 `IsDone`을 확인한다.
- `LocalizedString.StringChanged` 이벤트는 Locale 변경 시 자동 호출된다. UI 갱신에 쓰고 구독/해제를 대칭으로 맞춘다.
- 언어 전환은 `LocalizationSettings.SelectedLocale`을 바꿔 트리거한다.
- Smart String으로 플레이스홀더·복수형·성별을 처리하고 코드에서 변수를 바인딩한다.
- **흔한 실수**: `WaitForCompletion` 남용으로 히칭(가급적 async/이벤트), 다국어 글리프 폰트·폴백 누락, StringChanged 구독 해제 누락.

## 직렬화

### Unity 내장 (JsonUtility / SerializeField)
- public 필드 또는 `[SerializeField] private`만 직렬화한다. 프로퍼티는 미지원.
- Dictionary 직렬화 불가. top-level `List<T>`도 불가(컨테이너 클래스로 감싼다).
- 다형성(base/interface + 파생) 미지원.
- `[SerializeReference]` (2021 LTS+)는 참조로 직렬화해 다형성·null·순환 참조를 지원한다.
- `ISerializationCallbackReceiver`의 `OnBeforeSerialize`/`OnAfterDeserialize`로 Dictionary 같은 미지원 타입을 직렬화 가능한 형태로 변환한다. 호출 타이밍·스레드 문제가 있으니 남용하지 않는다.

### com.unity.nuget.newtonsoft-json
Dictionary·다형성·프로퍼티·`[JsonProperty]` 등이 필요하면 Unity 공식 배포 Newtonsoft를 쓴다. Addressables 등 다른 패키지가 이미 의존으로 끌어오는 경우가 있으니 별도 DLL을 또 넣지 않는다(중복 어셈블리·버전 충돌). 프로젝트에 있으면 그 버전을 재사용한다.

### 선택 기준
- **JsonUtility**: 단순·성능·내장. Unity 타입 친화. Dictionary·다형성이 필요하면 부적합.
- **Newtonsoft**: 유연·강력. 할당·성능 부담이 상대적으로 크다.
- **바이너리 / MessagePack**: 세이브·네트워크 대용량·성능 크리티컬. 사람이 읽지 못한다.
- **ScriptableObject**: 에디터 저작 데이터(밸런스·설정). Inspector 편집·참조 공유용. 런타임 생성·외부 교환·세이브는 JSON.

## 비동기: Awaitable / UniTask / Coroutine / Task

async 실행 모델, 취소 토큰 전파, `async void` 금지, 스레드 복귀 등 코드 규칙은 runtime-architecture.md. 여기서는 패키지 지형만 다룬다.

- **Awaitable**(Unity 6 내장): 의존성 없이 단순 async(프레임·초·작업 대기). 한계: `WhenAll`/`WhenAny`가 없어 병렬 합류에 .NET Task 래핑이 필요하고 그때 할당이 생긴다. 취소 예외 억제 수단이 없다.
- **UniTask**(Cysharp, 외부 패키지): 할당 없는 async/await. `WhenAll`/`WhenAny`, `PlayerLoopTiming`, `CancellationToken` 1급, `SuppressCancellationThrow` 제공. 성능·조합·세밀한 취소가 중요한 게임 코드에서 선호된다.
- **Coroutine**: 간단한 시퀀스, 반환값 불필요, 레거시. **System.Task**: 순수 C#·스레드풀 상호운용. Unity 프레임 타이밍에는 비친화.
- 프로젝트에 UniTask가 있으면 UniTask로, 없으면 Awaitable로 통일한다. 혼용하지 않는다.

## ECS / DOTS

6.4에서 Entities·Collections·Entities Graphics가 core로 편입돼 에디터와 함께 배포된다. 6.5에서 `EntityId`(64bit)가 `InstanceID`(32bit)를 대체한다(마이그레이션은 deprecated-api.md). 편입은 온보딩·이식 마찰을 줄이지만 도입 판단을 바꾸지는 않는다.

- Jobs/Burst/DOTS 도입 판단과 Burst 제약은 performance.md. 요약하면 대부분의 프로젝트는 GameObject/MonoBehaviour를 유지하고, 전체 ECS 없이 Job System·Burst만 핫패스에 선택적으로 쓰는 편이 맞다.
- 하이브리드 저작 경계(패키지 관점): SubScene에서 MonoBehaviour로 저작 → baking으로 엔티티 생성 → 플레이모드는 ECS. 저작은 GameObject, 런타임 시뮬레이션은 ECS.

## UI: UI Toolkit vs uGUI

둘 다 공식이고 deprecated가 아니다. uGUI(`com.unity.ugui`, 6.4 core)는 안정적·검증됐고 업데이트가 드물다. UI Toolkit은 활발히 개발되며 신기능이 잦다.

- 다양한 해상도의 스크린 오버레이 UI, 많은 엘리먼트의 성능은 UI Toolkit이 유리하다.
- 월드 스페이스 UI(VR/AR 등)는 UI Toolkit이 워크어라운드가 필요하므로 uGUI를 쓴다. 성숙한 에셋 생태계·기존 자산도 uGUI를 계속 쓰는 정당한 이유다.

### 구성 차이
- **uGUI**: GameObject/컴포넌트(Canvas, Image, Button). 코드에서 컴포넌트 참조·이벤트.
- **UI Toolkit**: UXML(구조) + USS(스타일, CSS 유사) + C#. UQuery로 요소를 조회하고 데이터 바인딩한다. VisualElement는 GameObject가 아니다.

```csharp
// Don't: uGUI 사고방식으로 UI Toolkit 접근
var btn = GetComponentInChildren<Button>();

// Do: UQuery로 조회 (UI Toolkit)
var btn = root.Q<Button>("play-button");
```
- **흔한 실수**: `GetComponent` 대신 `rootVisualElement.Q<>()`를 쓴다. 스타일은 인라인이 아니라 USS로 둔다. 매 프레임 폴링 대신 이벤트/바인딩을 쓴다.
- 판별: `.uxml`/`.uss`/UIDocument가 있으면 UI Toolkit, Canvas + prefab UI면 uGUI. 혼용 시 경계를 명확히 하고 프로젝트 관례를 따른다.

## 멀티플레이어 / 네트워킹

- **Netcode for GameObjects (NGO)** `com.unity.netcode.gameobjects`: GameObject/MonoBehaviour 고수준 네트워킹. Unity 6은 2.x 라인.
- **Netcode for Entities**: ECS용 별도.
- **Unity Transport**: 저수준 트랜스포트.

### 기본 제약 (코드가 따른다)
- 서버 권위(Server-Authoritative): 서버가 source of truth로 시뮬레이션·물리·판정을 하고, 클라는 입력 전송·상태 수신을 한다.
- 네트워크 인식 오브젝트는 `NetworkObject` + `NetworkBehaviour`(1개 이상) 를 갖는다.
- `NetworkVariable`: 지속 상태 동기화(HP·점수). 늦게 접속한 클라도 현재값을 받는다. 기본 쓰기 권한은 서버만, 타입은 unmanaged 또는 `INetworkSerializable`.
- RPC: 일회성 이벤트(발사·사운드). 호출해도 로컬 실행이 아니라 네트워크를 경유한다.

### RPC 구/신 (혼동 지점)
```csharp
// 구(레거시): 메서드명 접미사 규칙
[ServerRpc] void FireServerRpc() { }
[ClientRpc] void PlaySoundClientRpc() { }

// 신(1.8.0+/2.x 권장): 통합 [Rpc] attribute + SendTo
[Rpc(SendTo.Server)] void Fire() { }
[Rpc(SendTo.ClientsAndHost)] void PlaySound() { }
```
- 레거시 `[ServerRpc]`/`[ClientRpc]`도 동작하지만 신규는 `[Rpc(SendTo...)]`를 쓴다. 두 규칙을 섞지 않는다.
- 신 `[Rpc]`의 ownership 관련 파라미터명(`InvokePermission` vs `RequireOwnership`)은 버전별로 다를 수 있으니 설치 버전 API로 확인한다(미확정).
- 네트워킹은 나중에 붙이기 어렵다. 상태를 NetworkVariable/RPC 경계로 재구성해야 하므로 초기 설계에 반영한다.

## 서드파티 라이브러리 위상 (2026)

공식 패키지로 대체되지 않은 영역에서 사실상 표준이 된 라이브러리. 프로젝트에 이미 있으면 그 관례를 따르고(일관성), 없으면 의존성 최소화와 저울질한다.

| 영역 | 라이브러리 | 세대 교체 대상 |
|---|---|---|
| DI | VContainer (1.17.0, 2026-07) | Zenject (유지보수 둔화) |
| 리액티브 | R3 (`com.cysharp.r3`) | UniRx (레거시) |
| 트위닝 | DOTween (Unity 6+ 호환) | 없음 |
| 인스펙터·직렬화 확장 | Odin Inspector (상용, Unity 6.6 호환 패치) | 없음 |
| 고성능 async | UniTask | 코루틴·단순 Task |

### 구버전 API 방지
세대 교체된 라이브러리는 네임스페이스·API가 다르다: UniRx → R3, Cinemachine 2 → 3, Input Manager → Input System, Zenject → VContainer. 프로젝트에 실제 설치된 패키지·네임스페이스를 확인한 뒤 그 세대 API로 짠다. 구버전 튜토리얼을 그대로 복붙하지 않는다.

- 공식으로 대체된 영역: 코루틴·단순 async는 내장 Awaitable(조합·성능은 UniTask 우위), 일부 직렬화는 6.5 Serialization core.
- 여전히 서드파티 우위: DI(공식 DI 없음), 리액티브, 고급 트위닝, 인스펙터/직렬화 확장, 고성능 async.
