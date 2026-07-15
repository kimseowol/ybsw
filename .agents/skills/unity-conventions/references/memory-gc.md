# 메모리·GC·풀링

기준 버전: Unity 6000.x (6.5까지 반영) · 최종 확인: 2026-07

목차
- 1. 핫 패스에서 할당을 만드는 언어 패턴
- 2. foreach와 코루틴 yield 할당
- 3. 인크리멘탈 GC와 수동 제어
- 4. UnityEngine.Pool 내장 API (Unity 2021+ / Unity 6)
- 5. GameObject·컴포넌트 풀링
- 6. 문자열
- 7. 컬렉션 재사용과 버퍼 API
- 8. struct vs class
- 9. Span·stackalloc·NativeArray
- 10. 메모리 누수
- 11. Addressables·Resources
- 12. Awaitable과 async (Unity 6)
- 13. Unity 객체 특유의 함정
- 14. 진단 워크플로우
- 미확인

Unity 6(6000.x) 런타임 코드에서 관리 힙 할당(GC Alloc)과 메모리 누수를 줄이는 실전 규칙. 매 프레임 도는 코드를 쓰거나, 오브젝트를 생성·파괴하거나, 에셋·이벤트·네이티브 리소스를 다룰 때 참조한다. 버전 의존 항목에는 버전을 표기했고, 확정하지 못한 항목은 문서 끝 "미확인"에 모았다.

핫 패스 = Update/FixedUpdate/LateUpdate, 물리 콜백, 코루틴 루프, 매 프레임 도는 이벤트 핸들러. 아래 "핫 패스 금지"는 이 범위에만 적용하고, 초기화·로딩·에디터 코드에는 적용하지 않는다.

---

## 1. 핫 패스에서 할당을 만드는 언어 패턴

### 박싱

값 타입을 `object`로 감싸면 힙에 할당된다. Unity에서 가장 흔한 의도치 않은 임시 할당원이다.

- struct를 인터페이스로 캐스팅하면 박싱된다. 제네릭 제약 `where T : struct, IFoo`로 박싱 없이 다형성을 얻는다.
- enum이나 primitive를 `object` 파라미터로 넘기면 박싱된다. `Debug.Log`, `string.Format`, 문자열 보간의 인자가 대표적이다.
- `Dictionary`의 키가 enum이나 커스텀 struct면 `EqualityComparer<T>.Default` 비교가 박싱을 유발한다. 커스텀 `IEqualityComparer<T>`를 넘겨 회피한다.

```csharp
// Don't: enum 키 → 프레임마다 박싱
var map = new Dictionary<MyEnum, int>();
// Do: 커스텀 비교자로 박싱 제거
var map = new Dictionary<MyEnum, int>(new MyEnumComparer());
```

### LINQ

`Where/Select/OrderBy/ToList` 등은 이터레이터, 델리게이트, 결과 컬렉션을 매번 할당한다. 핫 패스에서 금지한다. 초기화·로딩·에디터 코드에서는 허용한다. `OrderBy`는 내부 버퍼와 비교 델리게이트로 특히 무겁다.

### 람다·클로저

지역 변수나 `this`를 캡처하는 람다는 컴파일러가 참조 타입(display class)을 만들어 힙에 할당하고, 델리게이트 캐싱도 못 한다. 캡처가 없는 람다는 static 델리게이트로 1회만 캐싱되어 이후 무할당이다.

- 재사용할 델리게이트는 필드에 미리 할당해 둔다.
- 캡처를 막으려면 C# 9 `static` 람다(`static () => ...`)를 쓴다. 실수로 캡처하면 컴파일 에러가 난다. Unity 6 전 버전이 C# 9를 지원한다.

### params

`params` 배열은 인자가 1개 이상이면 호출마다 배열을 할당한다. 고정 인자 오버로드(2~4개)를 제공하거나 `Span<T>` 기반 API로 대체한다.

---

## 2. foreach와 코루틴 yield 할당

### foreach

구체 타입(`List<T>`, 배열, `Dictionary`, `HashSet`)을 직접 `foreach`하면 struct 열거자를 반환하므로 할당이 없다. 반면 인터페이스 타입(`IEnumerable<T>`, `IList<T>`, `ICollection<T>`)으로 받아 `foreach`하면 열거자가 박싱되어 약 40B 할당된다.

```csharp
void Loop(IList<int> items)   // Don't: 인터페이스 → 열거자 박싱
{ foreach (var x in items) { } }

void Loop(List<int> items)    // Do: 구체 타입 → 무할당
{ foreach (var x in items) { } }
```

컬렉션을 인터페이스로 노출하는 API라면 `for` 루프를 쓴다. 호출자가 타입을 바꿔도 안전하다.

### yield

- `yield return null`은 무할당이다.
- `yield return new WaitForSeconds(t)`는 매번 할당한다. `readonly WaitForSeconds _wait = new(1f)`로 필드에 캐싱해 재사용한다. 불변이라 여러 오브젝트가 한 인스턴스를 공유해도 된다.
- `WaitForSecondsRealtime`은 상태가 있으므로 재사용 시 리셋에 주의한다.
- `StartCoroutine(Iter())`는 호출마다 이터레이터 상태머신 객체를 만든다. 자주 시작·정지하는 로직에는 부담이 된다.

이터레이터로 결과를 흘려보내는 대신, 호출자가 버퍼를 넘기는 `void Fill(List<T> results)` 형태로 바꾸면 할당이 사라진다. Unity 내장 API가 이 패턴을 쓴다(`GetComponents(List)` 등, 7절).

---

## 3. 인크리멘탈 GC와 수동 제어

- 기본값은 켜짐이다(6000.1 매뉴얼). 마킹 작업을 여러 프레임에 쪼개 중단 시간을 줄인다. VSync 또는 `Application.targetFrameRate`를 설정하면 프레임 말미 유휴 시간을 GC에 쓴다.
- 상시 비용으로 write barrier가 있다. 참조를 바꿀 때마다 GC에 알리는 코드가 추가된다.
- 참조 변경이 지나치게 많으면 마킹이 끝나지 않고 full 컬렉션으로 폴백해 스파이크가 난다. 참조가 많은 큰 힙에서 발생한다.
- 웹 플랫폼은 인크리멘탈 GC를 지원하지 않는다.

### 수동 제어 (`Scripting.GarbageCollector.Mode`)

- `Disabled`: GC 완전 정지. 이 모드에서는 `System.GC.Collect`도 효과가 없고 힙이 단조 증가해 OOM 위험이 있다.
- `Manual`: 자동 GC만 끈다. `System.GC.Collect()`로 수동 full 수거, `GarbageCollector.CollectIncremental`로 수동 인크리멘탈 수거가 가능하다.
- `Enabled`(기본): 자동 인크리멘탈.

끄기 전에 필요한 메모리를 모두 할당하고, 끈 동안 추가 할당을 피한다(매뉴얼). 로딩 화면 같은 비핵심 구간에서만 껐다 켠다. CPU 바운드 프로젝트에서 인크리멘탈을 끄면 프레임당 최대 1ms를 아낄 수 있다(매뉴얼).

`System.GC.Collect()`는 씬 전환 직후나 로딩 화면처럼 히치가 허용되는 구간에서만 호출한다. 게임플레이 중 매 프레임이나 주기적 호출은 금지한다. 강제 stop-the-world를 부른다.

---

## 4. UnityEngine.Pool 내장 API (Unity 2021+ / Unity 6)

네임스페이스 `UnityEngine.Pool`. `ObjectPool<T>`, `ListPool<T>`, `HashSetPool<T>`, `DictionaryPool<K,V>`, `GenericPool<T>`, `LinkedPool<T>` 등을 제공한다.

### ObjectPool<T> 생성자

- `createFunc` (`Func<T>`): 풀이 비었을 때 `Get()`에서 새 인스턴스 생성.
- `actionOnGet` (`Action<T>`): 대여 시 콜백(활성화·초기화).
- `actionOnRelease` (`Action<T>`): 반환 시 콜백(비활성화·정리).
- `actionOnDestroy` (`Action<T>`): `maxSize` 초과로 폐기될 때 정리.
- `collectionCheck` (bool): 이미 풀에 있는 객체를 다시 Release하면 예외를 던져 더블 릴리스를 잡는다. 오버헤드가 있으니 개발 중에 켠다.
- `defaultCapacity` / `maxSize`: 내부 스택 초기 용량 / 최대 보관 수. 초과분은 Release 시 `actionOnDestroy`로 폐기된다.
- 프로퍼티: `CountActive`, `CountInactive`, `CountAll`.

### 컬렉션 풀은 using 스코프로 강제

`ListPool/HashSetPool/DictionaryPool`은 `Get(out var x)`가 `PooledObject`를 반환한다. `using` 스코프를 벗어나면 자동으로 Release되고 내부에서 `Clear`까지 해 준다. 메서드 안에서 임시 컬렉션을 `new` 하는 대신 이 형태를 쓴다. Release 누락을 구조적으로 막는다.

```csharp
// Don't: 임시 컬렉션 매번 할당
var tmp = new List<Vector2>();
// Do: 풀에서 빌리고 스코프 끝에 자동 반환
using (ListPool<Vector2>.Get(out var tmp))
{
    tmp.Add(...);
}
```

### 상태 초기화 책임

커스텀 `ObjectPool`은 `actionOnRelease`에서 상태를 리셋하는 편이 안전하다. 반환 시점에 깨끗이 해 두면 다음 `Get`이 항상 clean 상태를 받는다. 흔한 오용: `maxSize`를 너무 작게 잡아 잦은 Destroy로 풀이 무의미해짐, `createFunc`에서 무거운 작업, Release 누락으로 풀이 고갈됨.

---

## 5. GameObject·컴포넌트 풀링

`Instantiate`/`Destroy`는 역직렬화, `Awake`/`OnEnable` 호출, 계층 삽입·재계산, GC 대상 생성을 수반한다. 풀링은 이를 `SetActive` 토글로 대체한다. 같은 오브젝트를 자주 생성·파괴하거나 생성 비용이 클 때 이득이다. 드물게 생성하는 경량 오브젝트에는 불필요하다.

내장 `ObjectPool<T>`로 감싸는 정석:

```csharp
_pool = new ObjectPool<Foo>(
    createFunc:      () => Instantiate(prefab),
    actionOnGet:     f => { f.gameObject.SetActive(true); f.Reset(); },
    actionOnRelease: f => f.gameObject.SetActive(false),
    actionOnDestroy: f => Destroy(f.gameObject),
    collectionCheck: true);   // 개발 빌드
```

`SetActive(false)` 기반이므로 `OnEnable`을 재초기화 훅, `OnDisable`을 정리 훅으로 쓴다. 함정:

- `OnDisable`에서 실행 중이던 코루틴이 자동 중단된다. 재사용 시 다시 시작한다.
- `OnEnable`에서 `+=` 구독하면 `OnDisable`에서 `-=`로 대칭 해제한다. 안 하면 재사용 때 중복 구독된다.
- `actionOnRelease`에서 `SetActive(false)`를 빠뜨리면 반환된 오브젝트가 화면에 남는다. Destroy를 직접 호출해 풀을 우회하지 않는다.

유형별 리셋:

- Rigidbody: `linearVelocity`/`angularVelocity`를 0으로(`velocity`는 폐기 명칭 — deprecated-api.md). 잔존 관성 버그를 막는다.
- ParticleSystem: `Clear`/`Stop` 후 재생. 잔상 방지.
- TrailRenderer/LineRenderer: `Clear` 또는 positions 리셋. 이전 궤적 잔상 방지.
- AudioSource: `Stop` 후 재생 위치 리셋.
- Transform: 위치·회전·부모 리셋(`worldPositionStays` 유의).

씬 언로드 시 풀 오브젝트가 파괴되면 풀이 dangling 참조를 갖는다. `DontDestroyOnLoad` 풀은 씬 종속 참조를 잡지 않게 한다.

---

## 6. 문자열

`string`은 불변이라 모든 연결·보간이 새 string을 할당한다.

- 반복 연결은 `StringBuilder`로 한다. 용량을 미리 지정하고(`new StringBuilder(capacity)`) 재사용 시 `.Clear()`를 부른다. 다만 최종 `.ToString()`은 여전히 string 1개를 할당한다.
- 매 프레임 갱신하는 UI 텍스트는 TMP `SetText("{0:F1}", value)` 오버로드를 쓴다. 인자를 내부 char 버퍼로 포맷해 string 할당을 피한다. `text = value.ToString()`은 ToString 할당이 생긴다.
- 값이 안 바뀌면 이전 값과 비교해 갱신을 건너뛴다.
- `int`/`float.ToString()`도 문화권 포맷 경로에서 할당한다. 자주 쓰는 소수의 값은 문자열 테이블로 캐싱한다.
- 완전 무할당 포맷이 필요하면 서드파티 ZString을 검토한다.

### Debug.Log

`Debug.Log`의 인자 string은 릴리스 빌드에서도 생성·할당된다. `[Conditional]` 래퍼로 호출 자체(인자 평가 포함)를 컴파일 단계에서 제거하는 패턴은 performance.md.

---

## 7. 컬렉션 재사용과 버퍼 API

### List를 받는 오버로드 (무할당)

배열을 반환하는 API 대신, 미리 할당한 `List`/배열을 채워 주는 오버로드를 재사용 버퍼로 쓴다.

- `GetComponents<T>(List<T>)`, `GetComponentsInChildren<T>(List<T>)` (배열 반환판은 할당).
- `Mesh.GetVertices(List<Vector3>)` 등.
- `Scene.GetRootGameObjects(List<GameObject>)`.
- `ParticleSystem.GetParticles(array)`.

### Physics 레이캐스트

- Physics(3D)의 `RaycastNonAlloc`/`SphereCastNonAlloc` 등은 현재도 존재하고, 미리 할당한 `RaycastHit[]` 버퍼를 채운다. `RaycastAll`은 배열을 새로 할당한다.
- Physics2D의 `~NonAlloc` 계열은 Deprecated다. `List`/버퍼를 받는 일반 오버로드로 대체한다.
- Unity 6 권장 대체는 `RaycastCommand`다. 다수 레이캐스트를 Job으로 배치 스케줄해 워커 스레드에서 병렬 처리하고 결과를 `NativeArray`에 기록한다.
- NonAlloc 계열은 버퍼가 꽉 차면 조용히 잘린다. 반환된 개수를 항상 확인한다.

### Clear-and-reuse

필드로 캐싱한 컬렉션을 `.Clear()` 후 재사용해 재할당을 피한다. `Dictionary`/`List`는 초기 `Capacity`를 지정해 내부 배열 증설을 막는다. 두 가지 함정:

- 재사용 컬렉션을 외부에 반환하면 다음 프레임에 덮어써져 데이터가 오염된다. 노출할 때는 복사본이나 ReadOnly 래핑을 반환한다.
- 재진입·중첩 호출에서 같은 버퍼를 두 곳이 동시에 쓰면 오염된다. 코루틴·이벤트 재진입과 Job 공유에 주의한다.

---

## 8. struct vs class

struct는 작고(Vector3/Vector4 수준, 대략 16~24B 이하), 수명이 짧고, 불변이고, 컬렉션에 대량 저장할 때 쓴다. 크거나 공유 참조·다형성·긴 수명이 필요하면 class를 쓴다.

- 가변 struct는 값 복사 때문에 버그가 난다. `list[i].field = x`는 복사본만 바꾸거나 컴파일 에러다. `readonly struct`로 선언한다.
- 큰 struct의 복사를 줄이려면 `in` 파라미터(읽기 전용 참조)를 쓴다. 방어적 복사를 막으려면 `readonly struct`와 병용한다. `ref return`/`ref local`로 in-place 접근도 가능하다.
- struct를 잘못 쓰면(큰 struct를 자주 복사, 컬렉션에서 값 복사 반복) 할당은 없어도 CPU가 역효과를 낸다.

---

## 9. Span·stackalloc·NativeArray

- `Span<T>`/`ReadOnlySpan<T>`은 Unity 6에서 IL2CPP·Mono 모두 지원한다. 관리 배열이나 stackalloc을 무할당 슬라이스로 다룬다. `ref struct`라서 필드 저장, async/이터레이터 사용, 힙 캡처가 불가능하다.
- `stackalloc`은 소규모 스크래치 버퍼를 스택에 잡는다(무 GC). 크면 스택 오버플로가 나고 async/yield에서 못 쓴다. `stackalloc int[64]` 정도의 소량만 쓴다.
- Collections 패키지의 `NativeArray`/`NativeList` 등은 네이티브 메모리로 Job/Burst와 호환된다. Allocator를 골라야 한다.
  - `Temp`: 매우 짧은 수명, 할당한 스레드 안에서만 안전. 메인스레드 Temp는 Job에 못 넘긴다. 가장 빠르다.
  - `TempJob`: Job에 전달 가능, 4프레임 안에 Dispose 필수. 초과하면 누수 경고가 난다.
  - `Persistent`: 장기 보관, 수동 Dispose 필수.
- Dispose를 빠뜨리면 "A Native Collection has not been disposed" 누수 경고가 난다. `using` 블록이나 `OnDestroy`에서 Dispose한다. Job이 오래 걸려 `TempJob` 4프레임을 넘길 것 같으면 `Persistent`+수동 관리로 간다.
- 일반 게임플레이 코드에서 `NativeArray`/`Span`을 남용하면 복잡도와 unsafe 위험만 는다. Job/Burst나 대량 데이터 처리에 한정한다. 소량 임시엔 stackalloc이나 풀링된 관리 컬렉션을 쓴다.

---

## 10. 메모리 누수

### 이벤트·델리게이트 미해제

`A.OnX += handler` 후 `-=`를 빠뜨리면 발행자가 핸들러 소유 객체를 계속 참조해 GC가 못 한다. 오래 사는 발행자(싱글턴·매니저)가 짧게 사는 구독자를 붙잡으면 구독자가 씬 언로드 후에도 산다. `OnEnable`에서 `+=`, `OnDisable`에서 `-=`로 대칭을 맞추거나 `OnDestroy`에서 해제한다.

### static 필드·싱글턴

앱 수명 내내 상주하므로 참조된 객체가 GC되지 않고 씬 언로드 후에도 산다. static 캐시는 명시적으로 clear한다. Enter Play Mode Options로 도메인 리로드를 끈 경우 static 필드가 플레이 종료 후 리셋되지 않는다. `[RuntimeInitializeOnLoadMethod]`로 명시 초기화한다.

### 런타임 생성 UnityEngine.Object

`new Texture2D()`, `new Material()`, `renderer.material` 접근으로 생긴 인스턴스, `new Mesh()`는 네이티브 리소스라 GC로 회수되지 않는다. 명시적으로 `Destroy()`한다. RenderTexture는 `Release()`한다. 인스턴스화가 필요 없으면 `sharedMaterial`을 쓴다. Texture → Material → Renderer 참조 사슬 중 하나라도 잡고 있으면 `UnloadUnusedAssets`가 못 지운다.

정적 탐지 규칙: `+=`가 있는데 대응 `-=`가 없음, `OnEnable`/`OnDisable` 비대칭, `new Texture2D/Material/Mesh` 후 `Destroy` 없음.

---

## 11. Addressables·Resources

- Addressables는 참조 카운팅을 쓴다. Load마다 ref-count가 오르고, 0이 되면 언로드 대상이 된다. 모든 operation handle은 사용 후 Release해야 한다. 누락하면 누수·크래시가 난다.
  - 로드 에셋: `Addressables.Release(handle)`.
  - 인스턴스화한 GameObject: `Addressables.ReleaseInstance(go)`.
  - `AssetReference`: `reference.ReleaseAsset()` / `reference.ReleaseInstance(go)`.
- 핸들을 캐싱하면 캐싱한 쪽이 소유한다. 씬·화면 단위로 로드와 Release를 묶는다(진입 시 로드, 퇴장 시 일괄 Release). 이중 로드는 ref-count만 올리므로 Release 수를 대칭으로 맞춘다.
- Release해도 즉시 메모리가 풀리지 않는다. 실제 해제는 에셋을 담은 AssetBundle이 언로드될 때 일어난다(ref-count 0 + 번들 언로드).
- `AssetBundle.Unload(true)`는 번들과 거기서 로드된 모든 오브젝트를 강제 언로드한다. 사용 중이면 dangling 참조·깨진 텍스처가 생긴다. `Unload(false)`는 헤더만 내리고 오브젝트는 남긴다(이후 재로드 시 중복 위험). 개별 에셋만 부분 언로드하는 방법은 없다.
- Resources는 소규모·레거시에만 쓴다. `Resources.UnloadUnusedAssets()`는 느리고 메인스레드를 블록하므로 로딩 화면에서만 부른다. 신규 코드는 Addressables를 쓴다.
- `Instantiate`한 인스턴스는 원본 에셋(메시·텍스처·머티리얼)을 공유 참조하므로 원본은 로드된 채 유지된다. 동일 에셋은 한 번 로드해 공유하고 `sharedMaterial`을 쓴다.

---

## 12. Awaitable과 async (Unity 6)

- `Awaitable`은 Unity 6 신규 타입으로 `Task`를 대체한다. 내부적으로 풀링되어 대개 무할당이다. `Task`는 호출마다 상태머신과 Task 객체를 힙에 할당한다.
- 풀링 때문에 한 `Awaitable` 인스턴스를 두 번 await하면 안 된다. 저장 후 재await는 금지다.
- 풀링은 Unity 내장 메서드(`NextFrameAsync`, `WaitForSecondsAsync`, `EndOfFrameAsync`, `FixedUpdateAsync`, `BackgroundThreadAsync`, `MainThreadAsync`)에만 적용된다. 커스텀 async 연산은 일반 할당이 생긴다.
- 파괴된 오브젝트에서 await가 계속되면 NRE·누수가 난다. 취소 토큰(`destroyCancellationToken`)과 수명 관리는 runtime-architecture.md.
- 순수 Unity에서 할당을 줄이려면 `Task`보다 `Awaitable`을 우선한다. 모든 오브젝트에서 대량으로 쓰면 성능 문제가 날 수 있으니 남용하지 않는다.

---

## 13. Unity 객체 특유의 함정

### 조회 비용

`GetComponent`·`Camera.main` 캐싱은 performance.md, `FindObjectsByType` 마이그레이션은 deprecated-api.md. 할당 관점만: Find류의 배열 반환판은 매 호출 할당하므로 핫패스에서 부르지 말고 직접 참조로 대체한다.

### fake null

fake null 의미와 `?.`·`??` 금지 규칙은 runtime-architecture.md. 메모리 관점의 함정만 여기 둔다.

- 파괴된 객체 참조를 필드로 계속 잡으면 관리 shell(C# 래퍼)이 GC되지 않는다. 파괴 후에는 참조를 null로 비운다.
- OnDestroy 순서는 비결정적이다. A가 B를 참조하는데 B가 먼저 파괴되면 fake null/NRE가 난다. 방어적으로 null 체크한다.

### Instantiate 오버로드

`Instantiate(prefab, parent, worldPositionStays)`로 부모를 지정하면 계층을 1회만 재계산한다. 생성 후 `SetParent`를 부르면 재계산이 추가된다. 로컬 좌표만 필요하면 `worldPositionStays=false`로 계산을 줄인다.

---

## 14. 진단 워크플로우

- Memory Profiler 패키지(`com.unity.memoryprofiler`)로 스냅샷을 캡처하고 Compare한다. 누수 절차: (1) 초기 상태 스냅샷 → (2) 대상 씬 진입·플레이 → (3) 씬 언로드 후 스냅샷 → 두 스냅샷을 비교해 언로드 후에도 남은 "New" 항목을 찾고, "Referenced By" 사슬로 붙잡는 주체를 추적한다.
- 내장 Profiler의 CPU Usage 모듈에서 GC Alloc 컬럼을 정렬해 프레임당 관리 할당 상위를 찾는다. Allocation Callstacks로 할당원을 본다. Deep Profile은 콜스택을 넓히지만 오버헤드가 커 수치를 왜곡한다.
- Project Auditor(`com.unity.project-auditor`, Unity 6.1+)는 정적 분석으로 과도한 GC 생성·비싼 스크립팅 호출을 리포트한다. CI/빌드에 통합해 새 이슈 유입을 막는 게이트로 쓴다.
- `ProfilerRecorder`로 "GC Allocated In Frame" 마커를 읽어, 특정 코드 경로 실행 후 할당 바이트가 0인지 assert하는 회귀 테스트를 만든다.
- 에디터 수치는 에디터 전용 할당과 Mono/IL2CPP 코드 생성 차이로 부풀려진다. 상대 비교·추세에만 쓰고, 최종 검증은 development build로 실기에서 한다.

---

## 미확인

아래는 조사 시점(2026-07)에 공식 문서로 확정하지 못한 항목이다. 코드에 반영하기 전에 재확인한다.

- 구체 타입 `foreach` 무할당은 실측(Unity 2020.3) 기반이며 Unity 6 공식 문서의 재확인 문구는 찾지 못했다. 규칙 자체는 유효한 것으로 보되, 인터페이스로 노출된 컬렉션에는 여전히 `for`를 권한다.
- `ObjectPool`의 `collectionCheck`가 Player 빌드에서 비활성(editor-only)이라는 커뮤니티 서술이 있으나 6000.3 스크립트 레퍼런스 원문에는 명시가 없다. 소스(`ObjectPools.cs`) 확인이 필요하다.
- Physics(3D) `RaycastNonAlloc`의 Unity 6 공식 Deprecated 여부는 미확정이다(Physics2D만 Deprecated 확정).
- C# 9 `static` 람다 문법의 정확한 최초 지원 6.x 마이너는 미확정이다(C# 9는 Unity 2021.2+이므로 Unity 6 전반 지원으로 추정).
