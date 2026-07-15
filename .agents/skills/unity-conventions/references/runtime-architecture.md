# 런타임 아키텍처 패턴

기준 버전: Unity 6000.x (6.5까지 반영) · 최종 확인: 2026-07

목차
- 1. 비동기 실행 모델
- 2. 취소와 수명 관리
- 3. UnityEngine.Object null 함정
- 4. 이벤트 메커니즘
- 5. ScriptableObject 런타임 아키텍처
- 6. 의존성 주입과 싱글톤 대안
- 7. MonoBehaviour 수명주기와 실행 순서
- 8. Update 최소화
- 9. 시간·프레임·물리 틱
- 10. 도메인/씬 리로드와 정적 상태
- 11. 참조 획득과 연결

Unity 6.x(6000.x LTS) 런타임 코드를 쓸 때 참조한다. 비동기 실행, 수명·취소, 이벤트, ScriptableObject, DI, MonoBehaviour 수명주기, 시간 틱, 도메인 리로드, 참조 연결의 실전 규약을 다룬다. 버전 의존 사실에는 도입 버전을 표기한다.

## 1. 비동기 실행 모델

세 축 중 하나를 고른다: `UnityEngine.Awaitable`(2023.1+), UniTask(외부 패키지), 코루틴.

- **프로젝트에 설치된 것을 쓴다.** UniTask가 있으면 UniTask, 없으면 Awaitable로 통일한다. 앱 코드 안에서 두 축을 섞지 않는다 — 유일한 예외는 아래 라이브러리 경계 변환이다. 미설치 프로젝트에 UniTask를 임의로 추가하지 않는다 — 의존성 추가는 승인 사항이다.
- **UniTask의 강점**: WhenAll/WhenAny, WaitUntil/WaitWhile, DelayFrame, PlayerLoopTiming 세밀 지정, 누수 추적 창. 할당이 없고 PlayerLoop만 쓰므로 WebGL에서 동작하고 데드락 위험이 없다. 병렬 합류·조건 대기가 잦아지면 UniTask 도입을 검토한다(승인 필요).
- **라이브러리/재사용 코드는 반환 타입으로 Awaitable을 노출한다.** 외부 의존을 강요하지 않기 위해서다. 내부 구현은 `AsUniTask()`로 편의 기능을 써도 된다 — 이 경계 변환이 혼용 금지의 허용된 예외다. UniTask는 Awaitable의 상위집합이고 `AsUniTask()` / `AsTask()`로 상호 변환한다.

Awaitable 정적 메서드(매뉴얼 6000.3 확인): `NextFrameAsync()`(`yield return null` 등가), `WaitForSecondsAsync(float)`(scaled time), `EndOfFrameAsync()`, `FixedUpdateAsync()`, `BackgroundThreadAsync()`(ThreadPool 전환), `MainThreadAsync()`(메인 스레드 복귀), `FromAsyncOperation(...)`. 값 반환은 `Awaitable<T>`, 외부 완료 신호는 `AwaitableCompletionSource<T>`(TaskCompletionSource 등가).

**Awaitable 인스턴스는 같은 메서드에서 두 번 await 하지 않는다.** 인스턴스가 풀링·재사용되므로 매번 새로 생성한다.

코루틴은 다음일 때만 유지한다:
- GameObject 비활성화 시 자동 중단되는 수명 연동이 이점일 때.
- 에디터 호환이나 기존 코드베이스 일관성이 우선일 때.

신규 코드는 2023.1+에서 async/await를 선호한다(예외 처리, 값 반환, 취소 토큰, 라이브러리 연동에서 우위). 코루틴 안에서 `yield return Awaitable.WaitForSecondsAsync(...)`로 혼용할 수 있다.

### async 오용 차단

**`async void`를 쓰지 않는다**(불가피한 이벤트 핸들러 제외). 호출자가 완료를 알 수 없고, 예외를 catch할 수 없으며, 미처리 예외가 앱을 종료시킨다. Unity 수명주기 메시지(`Start`, `OnEnable` 등)를 async로 선언하면 async void가 되는데, 엔진이 호출하는 이벤트 핸들러라 이 예외에 해당한다 — 단 본문 전체에서 예외를 처리한다(§2 예시). fire-and-forget이 필요하면 UniTask 사용 프로젝트에서는 `UniTaskVoid`를, Awaitable만 쓰는 프로젝트에서는 내부에서 예외를 처리하는 `async Awaitable` 메서드를 쓴다.

**`.Result` / `.Wait()`로 블로킹하지 않는다.** 메인 스레드 블로킹은 데드락을 부른다. WebGL은 단일 스레드라 영원히 미완료된다. Addressables의 `WaitForCompletion`도 WebGL에서 동작하지 않는다.

**`Task.Run`(스레드풀)은 WebGL에서 동작하지 않는다.** Unity 오브젝트는 백그라운드 스레드에서 접근할 수 없으므로 `Awaitable.MainThreadAsync()` 또는 UniTask `SwitchToMainThread`로 메인 스레드에 복귀한 뒤 접근한다.

## 2. 취소와 수명 관리

`MonoBehaviour.destroyCancellationToken`(read-only, 2022.2+)은 컴포넌트 파괴 시 취소된다. `Application.exitCancellationToken`(2022.2+)은 앱 종료 시 취소된다. 취소되면 대기 중이던 async 메서드는 `OperationCanceledException`을 던진다.

**토큰은 메서드 진입 시 로컬 변수로 캐싱한다.** await 재개 이후에 `this.destroyCancellationToken`을 다시 읽으면 이미 파괴됐을 수 있다.

```csharp
async void Start() { // 수명주기 메시지는 async void 예외 영역(§1)
    var ct = destroyCancellationToken; // 진입 시 캐싱
    try {
        await Awaitable.WaitForSecondsAsync(10f, ct);
        DoSomething(); // 파괴됐으면 도달하지 않음
    } catch (OperationCanceledException) { /* 정상 취소 */ }
}
```

**CancellationToken은 async 메서드 체인 끝까지 마지막 인자로 전파한다.** 취소는 정상 흐름이므로 상류로 전파하고 로직 중간에서 삼키지 않는다. 삼키면 리소스 정리가 누락되고 부분 실행 버그가 생긴다. fire-and-forget 최상단(`UniTaskVoid` 등)에서만 catch해 조용히 종료한다.

**await 재개 후 파괴 가능성을 방어한다.** 우선순위대로:
1. ct를 전달해 애초에 재개되지 않게 한다(가장 견고).
2. `if (this == null) return;` 또는 `if (!this) return;`(Unity 오버로드 == 로 파괴 검사).

**순수 C# 객체(비 MonoBehaviour)는 destroyCancellationToken이 없다.** 직접 `CancellationTokenSource`를 소유·Dispose하거나 상위 스코프의 토큰을 링크드 토큰으로 전달받는다. CTS를 만든 쪽이 Dispose 책임을 진다. 여러 취소 소스는 `CancellationTokenSource.CreateLinkedTokenSource(a, b)`로 결합하고, 링크드 CTS도 Dispose한다. VContainer는 LifetimeScope 파괴 시 취소되는 토큰을 제공한다.

## 3. UnityEngine.Object null 함정

`UnityEngine.Object` 파생 타입(GameObject, Transform, MonoBehaviour 등)은 `==`/`!=`를 오버로드한다. Destroy 후 C# 래퍼는 살아 있지만 네이티브 객체는 파괴된 "fake null" 상태가 된다. 오버로드된 `==`는 네이티브 lifetime을 검사해 파괴됐으면 `== null`을 `true`로 반환한다.

**`?.` `??` `??=`를 UnityEngine.Object 파생 타입에 쓰지 않는다.** 이 연산자들은 오버로드된 `==`를 호출하지 않아 lifetime 검사를 우회하고, 파괴된 객체를 살아 있는 것으로 오판한다. 컴파일러가 CS8073로 경고하고 Rider/ReSharper는 NoNullPropagation으로 경고한다.

```csharp
// Don't: fake null을 놓친다
if (_target?.gameObject != null) _target.Fire();
_cached ??= GetComponent<Rigidbody>();

// Do: 오버로드 == 또는 암시적 bool 변환
if (_target != null) _target.Fire();
if (_target) _target.Fire();
```

진짜 참조 null만 검사하려면 `object.ReferenceEquals(obj, null)`을 쓴다. 풀링·지연 초기화에서 미할당(진짜 null)과 파괴를 구분해야 할 때 이걸로 판별한다.

**`#nullable enable`과 `[SerializeField]`는 충돌한다.** 인스펙터가 주입하는 필드는 선언부에서 초기화할 수 없어 CS8618("non-nullable field uninitialized")이 발생한다. 해결:
- `[SerializeField] private Image _image = null!;`(null-forgiving), 또는 구역 `#nullable disable`.
- **Microsoft.Unity.Analyzers를 추가하면 `USP0016`이 SerializeField/Unity 메시지 초기화를 감지해 CS8618을 억제한다.** nullable은 신규·순수 로직 어셈블리부터 점진 도입한다.

참고: `==` 오버로드 폐기는 논의 단계이며 6.x에서 실제로 제거되지 않았다. 오버로드는 유효하다.

## 4. 이벤트 메커니즘

- **C# `event`/`Action`을 기본으로 쓴다.** 호출 비용이 낮고 Invoke 시 할당이 없다. 컴파일 타임 타입 검사로 리팩토링에 안전하다. 단점은 인스펙터에 노출되지 않는다.
- **UnityEvent는 인스펙터 바인딩이 필요할 때만 쓴다.** C# event 대비 약 10배 느리고(리플렉션), Invoke마다 GC가 발생한다. 인스펙터 바인딩은 메서드 리네이밍·시그니처 변경 시 문자열 기반이라 조용히 깨진다.
- **ScriptableObject 이벤트 채널은 씬 간·시스템 간 디커플 통신에 쓴다.** 발행자와 구독자가 서로 모른다. 결합도가 최소고 리팩토링에 강하다. 비용은 자산 관리 오버헤드라 소규모엔 과설계다.

**구독은 대칭으로 해제한다.** 비활성-재활성을 반복하는 오브젝트(풀링 포함)는 `OnEnable` 구독 / `OnDisable` 해제 쌍을 쓴다. 수명이 1회면 Awake·Start 구독 / OnDestroy 해제를 쓴다. 해제하지 않으면 발행자가 구독자보다 오래 살 때 구독자가 GC되지 않는다(누수).

**해제하려면 명명된 메서드나 필드에 저장한 델리게이트를 쓴다.** 익명 람다는 참조를 보관하지 못해 `-=`가 실패한다.

```csharp
// Don't: 해제 불가
evt.OnHit += () => TakeDamage();

// Do: 명명 메서드로 해제 가능
void OnEnable()  { evt.OnHit += HandleHit; }
void OnDisable() { evt.OnHit -= HandleHit; }
```

**발행 중(Invoke 순회 중) 구독자 목록을 수정하면 "collection modified"나 누락이 난다.** 순회 전 스냅샷을 복사하거나 지연 발행한다. 핸들러가 같은 이벤트를 다시 발행하는 재진입은 플래그 가드나 큐잉으로 막는다.

## 5. ScriptableObject 런타임 아키텍처

- **Config**: 불변 데이터(스탯, 밸런스). 디자이너가 코드 없이 편집한다. 안전한 기본 용도다.
- **Runtime Set**: 런타임 컬렉션 참조(예: 씬 내 특정 컴포넌트 전부). 오브젝트가 `OnEnable`에 자기를 등록, `OnDisable`에 제거한다. 싱글톤 없이 전역 접근을 얻는다.
- **Event Channel**: 섹션 4 참조.

**가변 런타임 상태를 SO에 저장하지 않는다.** 에디터는 SO 자산을 디스크에 다시 써서 플레이 중 변경이 세션 간 남지만, 빌드는 SO가 읽기전용 리소스라 앱 재시작 시 초기값으로 돌아간다. 에디터에서 테스트한 상태가 빌드와 달라 재현이 어려운 버그가 된다. 방어:
1. 가변 상태는 런타임 인스턴스나 일반 클래스에 둔다.
2. 굳이 SO를 상태 컨테이너로 쓰면 게임 시작 시 초기값을 명시적으로 복사한다.
3. `Instantiate()`로 SO를 복제해 원본 자산 오염을 막는다.

**씬 오브젝트를 SO에 직렬화로 참조하지 않는다.** SO는 프로젝트 레벨이라 씬 오브젝트를 참조하지 못한다. 런타임 등록(Runtime Set) 방식으로 연결한다. SO끼리나 SO→프리팹 참조는 직렬화된 에셋 참조라 가능하다.

도메인 리로드를 끄면 SO의 내부 상태가 플레이 세션 간 잔존한다. 필드 초기화자만 의존하지 말고 `OnEnable`이나 `RuntimeInitializeOnLoadMethod`로 재초기화한다(섹션 10).

## 6. 의존성 주입과 싱글톤 대안

싱글톤/static은 테스트가 곤란하고(대체 불가, 전역 상태), 초기화 순서가 불명확하며, 도메인 리로드를 끄면 상태가 잔존한다(섹션 10).

DI 컨테이너 도입은 다음 신호에서 정당하다: 프로젝트 규모 확대, 디자이너·다수 프로그래머 협업, 테스트 필요, 서비스(오디오·세이브 등) 다수. 소규모·프로토타입에는 과하다. 인스펙터 참조와 소수 매니저로 충분하다.

VContainer 사실:
- Zenject 대비 약 5~10배 빠르고 spawned instance가 없을 때 Resolve가 zero 할당이다.
- 등록마다 `Lifetime`을 명시한다: `Singleton`, `Scoped`, `Transient`.
- LifetimeScope는 계층을 이룬다. 자식 스코프를 동적 생성해 비동기 리소스 로딩에 대응한다.

스코프 설계: Project 스코프(DontDestroyOnLoad, 앱 전역 서비스) → Scene 스코프(씬별 서비스, Project를 부모로) → 동적 자식 스코프.

**MonoBehaviour는 생성자 주입이 불가능하다**(Unity가 생성). 메서드 주입(`[Inject] void Construct(...)`)을 쓴다(가장 명시적). **로직은 순수 C# 서비스로 분리해 생성자 주입하고, MonoBehaviour는 뷰·입력만 담당한다.** 동적 생성 MonoBehaviour 주입은 참조 관리가 복잡하므로 가능하면 피한다.

## 7. MonoBehaviour 수명주기와 실행 순서

오브젝트당 순서는 `Awake` → `OnEnable` → `Start`로 보장된다. 모든 Awake와 OnEnable이 끝난 뒤에 Start들이 실행된다. **서로 다른 스크립트 간 Awake/OnEnable 순서는 보장되지 않는다.** 한 스크립트의 OnEnable이 다른 오브젝트의 Awake보다 먼저 실행될 수 있다.

- **Awake에서는 자기 자신만 초기화한다**: GetComponent 캐싱, 필드 초기화. 타 객체를 참조하지 않는다.
- **타 객체 참조는 Start에서 한다.** 모든 Awake 완료가 보장되는 지점이다.
- OnEnable에서 타 객체를 참조·구독하면 대상이 아직 Awake되지 않았을 수 있다.

**Project Settings의 Script Execution Order로 순서를 강제하지 않는다.** 전역 설정이고 숨은 결합을 만들어 확장 시 취약하다. 대신 부트스트랩이 순서대로 Init을 호출하거나, DI로 의존을 명시하거나, Start 단계를 활용한다.

`OnEnable`(구독/등록)과 `OnDisable`(해제)은 대칭으로 유지한다. 비대칭이면 비활성 후에도 콜백이 남고 재활성 시 중복 구독된다. 풀링·토글 반복 오브젝트에서 특히 중요하다.

## 8. Update 최소화

매 `Update`/`LateUpdate` 호출은 네이티브 C++ → 관리 C# interop 전환이다. 빈 Update 제거, GetComponent/Find/LINQ/할당 배제, 대량 오브젝트용 Update Manager 패턴은 performance.md.

주기 작업 선택:
- 정확한 프레임 타이밍이 필요하면 Update.
- N초 간격이면 `InvokeRepeating`(단순), 코루틴 `WaitForSeconds`, 또는 Awaitable 딜레이 루프.
- 취소·async 연동이 필요하면 Awaitable/UniTask 루프에 ct.

매 프레임 조건 검사(폴링) 대신 이벤트로 상태 변화를 통지한다(섹션 4).

## 9. 시간·프레임·물리 틱

- `Time.deltaTime`: 마지막 프레임 이후 시간(timeScale 반영). Update의 프레임 종속 이동·보간에 곱한다.
- `Time.fixedDeltaTime`: 고정 스텝(기본 0.02s = 50Hz). FixedUpdate의 물리에 쓴다. FixedUpdate 안에서 `deltaTime`이 자동으로 fixedDeltaTime과 같아지지만, 의도를 명확히 하려면 `fixedDeltaTime`을 명시한다.
- `Time.unscaledDeltaTime`: timeScale 무관. UI·연출·일시정지 중 동작해야 하는 것에 쓴다.

**deltaTime 곱셈을 빠뜨리지 않는다.** 프레임레이트에 따라 속도가 달라진다.

```csharp
// Don't: 프레임레이트 종속
transform.position += speed * dir;
// Do
transform.position += speed * dir * Time.deltaTime;
```

배치:
- 입력 폴링(`GetKeyDown` 등), 카메라, 비물리 이동은 Update에서 한다. `GetKeyDown`은 Update에서만 신뢰할 수 있다.
- 물리(`AddForce`, velocity, `MovePosition`)는 FixedUpdate에서 한다.
- 입력을 Update에서 읽어 플래그에 저장하고 FixedUpdate에서 물리에 적용한다.

**일시정지는 `Time.timeScale = 0`으로 한다.** Update는 계속 실행되지만 deltaTime이 0이고, FixedUpdate는 완전 중단되며, `WaitForSeconds` 코루틴과 Normal 모드 Animator가 정지한다. 일시정지 중에도 동작해야 하는 것은 `unscaledDeltaTime`/`realtimeSinceStartup`을 읽고 `WaitForSecondsRealtime`을 쓴다. UI 애니메이션은 Animator Update Mode를 "Unscaled Time"으로 설정한다.

**FixedUpdate(50Hz)로 움직이는 Rigidbody는 인스펙터에서 Interpolate를 켠다.** 60+FPS 렌더에서 렌더러가 물리 스텝 사이를 보간해 서브스텝 지터를 없앤다. 게임플레이 비용은 없다.

참고: Unity 6의 물리 틱/기본 타임스텝 변경은 확인되지 않았다. 기본 Fixed Timestep 0.02s(50Hz)로 서술한다.

## 10. 도메인/씬 리로드와 정적 상태

Project Settings > Editor > Enter Play Mode Settings에서 도메인 리로드와 씬 리로드를 개별로 끌 수 있다. 플레이 진입 시간이 단축된다.

**도메인 리로드를 끄면 static 상태가 플레이 세션 간 잔존한다.** static 변수 값이 지속되고, static 이벤트 핸들러가 등록된 채 남으며, 싱글톤 인스턴스가 살아남는다. 결과로 두 번째 플레이부터만 재현되는 버그가 생긴다: static 카운터 누적, 중복 이벤트 구독(콜백 2회), 싱글톤 "이미 존재" 오류. 첫 플레이는 도메인이 갓 로드돼 깨끗하므로 재현이 어렵다.

**리로드가 켜져 있다고 가정하지 않는다.** static 필드 초기화자(`static int x = 0;`)는 도메인 리로드 시에만 실행되므로, 리로드를 끄면 재실행되지 않아 이전 값이 남는다.

**진입 시 static 상태를 명시적으로 리셋한다.** `SubsystemRegistration`이 가장 이른 시점이다.

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void ResetStatics() {
    _counter = 0;
    OnSomeStaticEvent = null; // 이벤트 핸들러 해제
    _instance = null;         // 싱글톤 리셋
}
```

`RuntimeInitializeLoadType` 옵션: `SubsystemRegistration`(가장 이름), `AfterAssembliesLoaded`, `BeforeSplashScreen`, `BeforeSceneLoad`, `AfterSceneLoad`. 상태 리셋에는 `SubsystemRegistration`을 관례로 쓴다.

씬 리로드까지 끄면 씬 오브젝트가 재생성되지 않아 Awake/OnEnable이 재실행되지 않을 수 있다. 오브젝트 재생성에 기대는 초기화도 이 경우를 가정한다.

## 11. 참조 획득과 연결

연결 수단은 셋 중 목적에 맞게 고른다.

- **인스펙터 직렬화 참조(`[SerializeField] private T _ref;`)**: 컴파일 타임 연결, 런타임 검색 비용 0. 프리팹/씬 내부 참조에 최선.
- **`GetComponent`/`InParent`/`InChildren`**: 같은·근처 오브젝트의 컴포넌트. 캐싱 전략은 performance.md.
- **DI 주입**: 결합 최소, 테스트 용이(섹션 6).

매 프레임 안티패턴: `GameObject.Find`·`FindObjectsByType`류(deprecated·마이그레이션은 deprecated-api.md), `SendMessage`/`BroadcastMessage`(대체는 deprecated-api.md). 허용 예외는 에디터 툴, 1회성 부트스트랩 배선, 진짜 동적 미지의 참조뿐이다.

### 풀 객체의 의존성 재주입

`UnityEngine.Pool`과 Get/Release 상태 리셋 규약은 memory-gc.md.

아키텍처 관점의 함정: 풀 객체는 Instantiate되지 않아 생성자 주입이 없다. Get 시점에 `Init(deps)` 같은 초기화 메서드로 의존성과 데이터를 주입한다(DI 사용 시 팩토리로 주입). Awake에 캐싱한 자기 컴포넌트 참조는 재사용 간 유지되지만, 외부 대상 참조와 이벤트 구독은 Get/Release마다 재설정한다.
