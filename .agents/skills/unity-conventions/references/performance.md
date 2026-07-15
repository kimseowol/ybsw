# Unity 일반 최적화 레퍼런스

기준 버전: Unity 6000.x (6.5까지 반영) · 최종 확인: 2026-07

목차
- 1. Update 루프와 틱
- 2. 컴포넌트 참조 캐싱과 객체 조회
- 3. Transform 접근
- 4. 물리
- 5. 힙 할당과 GC
- 6. 오브젝트 풀링
- 7. 코루틴 vs async/Awaitable (Unity 6)
- 8. Jobs / Burst / DOTS 도입 판단
- 9. 렌더링 CPU 비용과 배칭
- 10. UI 최적화
- 11. 애니메이션과 오디오
- 12. 프로파일링 규율
- 13. 횡단 안티패턴과 프로젝트 설정

Unity 6 (URP) 프로젝트에서 런타임 CPU/GC 비용을 줄이는 코드 레벨 규칙 모음이다. 게임플레이 스크립트, 물리, UI, 비동기 코드를 쓰거나 리뷰하는 순간 참조한다. 기준은 Unity 6.0 LTS ~ 6.5이며, 버전에 따라 달라지는 항목은 버전을 표기했다.

---

## 1. Update 루프와 틱

MonoBehaviour의 `Update`/`LateUpdate`/`FixedUpdate`는 매 프레임 네이티브 C++에서 매니지드 C#으로 호출 경계를 넘고, 본문이 비어 있어도 이 비용은 발생한다.

- 빈 콜백을 남기지 않는다. 스텁 `void Update() {}` 도 등록 오버헤드를 유발한다. 안 쓰면 지운다.
- 같은 로직을 가진 오브젝트가 수백~수천 개 규모면 개별 Update 대신 매니저 하나가 단일 Update에서 리스트를 순회한다. 경계 비용을 1회로 줄인다. UI나 소규모, 개별 분기 로직에는 적용하지 않는다.
- `Time.frameCount`로 N프레임마다만 도는 틱 분할을 쓴다. 매 프레임 필요 없는 로직의 상시 비용을 나눈다.

Time API:
- `Update`/`LateUpdate`에서는 `Time.deltaTime`, `FixedUpdate`에서는 `Time.fixedDeltaTime`을 쓴다. (`FixedUpdate` 안에서 `Time.deltaTime`도 자동으로 fixedDeltaTime을 반환하지만, 의도를 드러내려 fixedDeltaTime을 명시한다.)
- 힘/속도/rigidbody 이동은 `Update`가 아니라 `FixedUpdate`에서 한다. 4번 참조.

폴링을 이벤트/델리게이트 구독으로 바꾸면 매 프레임 검사가 사라진다. 대신 구독 해제를 빠뜨리면 메모리 릭과 파괴된 객체 null 참조가 생긴다.

```csharp
// Do: OnEnable/OnDisable 쌍으로 짝을 맞춘다. 풀링과도 호환된다.
void OnEnable()  => target.OnDied += HandleDied;
void OnDisable() => target.OnDied -= HandleDied;
```

---

## 2. 컴포넌트 참조 캐싱과 객체 조회

`GetComponent` 계열은 비싸다. 매 프레임 호출하지 않고 `Awake`/`Start`에서 1회 캐싱해 필드로 재사용한다.

- 같은 오브젝트 내 참조는 `Awake`, 다른 오브젝트 참조는 `Start`에서 캐싱한다. 다른 객체의 Awake 완료를 보장받는다.
- `GetComponentInChildren`/`GetComponentInParent`는 계층을 순회해 더 비싸다. 결과를 반드시 캐싱한다.
- 존재가 불확실하면 `TryGetComponent`를 쓴다. 부재 시 null 반환 경로에서 생기던 할당을 피한다.

```csharp
// Don't
void Update() { GetComponent<Rigidbody>().AddForce(f); }

// Do
Rigidbody _rb;
void Awake() { _rb = GetComponent<Rigidbody>(); }
void FixedUpdate() { _rb.AddForce(f); }
```

Find 계열:
- `FindObjectOfType`/`FindObjectsOfType` deprecated와 `FindObjectsByType`(sort mode·inactive) 마이그레이션은 deprecated-api.md.
- 성능 규율: 모든 Find 계열과 `GameObject.Find`(이름)·`FindWithTag`는 씬 전체를 순회하므로 매 프레임/핫패스에서 금지한다. 초기화 시 1회만 허용하고 이후 캐싱한다. 대안은 인스펙터 직접 참조, 싱글턴/서비스 로케이터, 이벤트다.

Camera.main:
- 2019.4.9+부터 내부적으로 "MainCamera" 태그 캐시를 유지해 과거보다 빠르다. Unity 6도 이 캐시가 있다.
- 그래도 매 프레임 접근(Update 내)은 필드에 캐싱한다. 프로퍼티 접근 자체에 비용이 있다.
- 함정: 다중 카메라면 어느 카메라를 반환할지 불확정이고, 비활성화/씬 전환 시 캐시가 무효화돼 캐싱한 참조가 stale/null이 될 수 있다. 씬 전환 후 재캐싱한다.

JetBrains Rider/ReSharper-Unity에 Camera.main, Update 내 GetComponent/Find, 성능 임계 구간 Debug.Log 인스펙션이 내장돼 있다. 컨벤션/CI로 강제한다.

---

## 3. Transform 접근

`transform` 프로퍼티 접근은 내부 컴포넌트 조회 성격이라 반복 접근 시 로컬 변수/필드 캐싱이 유리하다. (최신 Unity에서 과거보다 최적화됐고, 이득 크기는 오브젝트 수와 플랫폼에 따른다.)

- `position`/`rotation`의 get/set은 값마다 네이티브 호출이고, set은 자식/물리/UI 레이아웃 재계산을 연쇄로 부른다. 한 프레임에 같은 transform을 여러 번 set 하지 말고 최종값 1회만 set 한다.
- 루트가 아닌 오브젝트를 로컬 기준으로 다룰 때 `localPosition`을 쓴다. `position`(월드)은 계층 변환 계산이 붙어 불필요한 로컬↔월드 왕복이 생긴다.
- `transform.Find`와 parent/child 문자열 탐색은 비싸다. 직접 참조를 보관한다.
- 대규모 이동 오브젝트는 `TransformAccessArray` + Job System으로 병렬 접근한다. 단 이 배열 관리 자체가 비용이라 소규모에는 이득이 없다.

---

## 4. 물리

Fixed Timestep:
- 기본 0.02s(50Hz). `Maximum Allowed Timestep` 기본 0.333은 프레임 급락 시 물리 스파이럴을 막는 상한이다.
- rigidbody 이동을 부드럽게 하려고 timestep을 올리지 않는다. CPU만 더 먹는다. Interpolation을 쓴다.

Rigidbody 설정:
- `Interpolation`은 시각적 지터가 보일 때만 켠다. 카메라 추적 대상/플레이어에 `Interpolate` 권장. 불필요하면 `None`으로 둬 비용을 아낀다. `Extrapolate`는 예측이라 오버슈트할 수 있다.
- `Collision Detection`은 기본 `Discrete`. 빠른 오브젝트가 벽을 통과(터널링)할 때만 `Continuous`/`Continuous Dynamic`을 쓴다. Continuous 계열은 비싸니 필요한 오브젝트에만 켠다.
- 저속에서 sleep에 드는 rigidbody는 시뮬에서 빠져 부하가 준다. sleeping threshold를 활용한다.
- 이동하는 collider에는 rigidbody(키네마틱이라도)를 붙인다. rigidbody 없는 static collider를 움직이면 정적 트리 재빌드 비용이 든다.
- `Physics` 레이어 충돌 매트릭스에서 불필요한 레이어 쌍 충돌을 끈다. broadphase 부하가 준다. Trigger에도 적용된다.

물리 이동은 transform 직접 조작이 아니라 rigidbody API로 한다. transform 조작은 물리 엔진과 충돌해 터널링/지터를 부른다.

```csharp
// Don't: Update에서 transform으로 물리 이동
void Update() { transform.position += dir * speed * Time.deltaTime; }

// Do: FixedUpdate에서 rigidbody로
void FixedUpdate() { _rb.MovePosition(_rb.position + dir * speed * Time.fixedDeltaTime); }
```

Raycast:
- 매 프레임 다수 캐스트는 `Physics.RaycastNonAlloc`로 결과 버퍼를 재사용해 할당을 피한다. 더 나은 방법은 `RaycastCommand`/`SpherecastCommand`/`BoxcastCommand` + Job System 배치로 멀티스레드 병렬 처리다.
- 배치 패턴: `NativeArray<RaycastCommand>` 채우기 → `RaycastCommand.ScheduleBatch(commands, results, minCommandsPerJob, maxHits)` → `JobHandle` → 나중에 `.Complete()` 후 `NativeArray<RaycastHit>` 결과 접근.
- 예산을 건다: 프레임당 캐스트 상한, `maxDistance` 제한, `layerMask`로 대상 축소, `QueryTriggerInteraction`으로 트리거 제외.

Unity 6 물리:
- 3D 빌트인 물리는 여전히 PhysX다. 결정론이 필요하면 Unity Physics(DOTS)나 Havok을 선택한다. 이들은 결정론적이지만 CPU 아키텍처(x86/ARM) 간 부동소수 차이로 크로스 플랫폼 결과가 달라질 수 있다.
- 2D는 Unity 6에서 Box2D v3로 교체돼 멀티스레드 성능과 결정론이 개선됐다. (3D PhysX의 대규모 멀티스레드 변경 여부는 미확인.)

---

## 5. 힙 할당과 GC

목표는 핫패스에서 프레임당 매니지드 힙 할당 0바이트에 근접하는 것이다. 핫패스의 대표적 할당원은 LINQ/정규식, 클로저 캡처, 문자열 연결/보간, `params` 배열, 박싱, 매 프레임 `new` 컬렉션이다. 각각의 대안, foreach·yield 할당, 인크리멘탈 GC 수동 제어(`GarbageCollector.GCMode`), struct/Span/NativeArray 규칙은 memory-gc.md.

핫패스 밖(초기화·로딩·에디터)에서는 위 패턴이 모두 허용된다. `GC.Collect()`는 로딩 화면처럼 프레임 튐이 무관한 시점에만 부른다.

---

## 6. 오브젝트 풀링

`Instantiate`는 프리팹 복제와 Awake/OnEnable, 메모리 할당으로 스파이크를 만들고 `Destroy`는 GC 대상을 만든다. 총알/이펙트/적처럼 짧은 수명으로 반복 생성/파괴하는 오브젝트는 풀링한다.

- 전환 신호: 프레임 스파이크가 `Instantiate.Produce`/`GC.Alloc`에 몰림, 초당 다수 생성/파괴.
- `UnityEngine.Pool`(`ObjectPool<T>`, `ListPool<T>` 등) API와 반환 시 상태 리셋 규약은 memory-gc.md.

부하 분산(풀링의 성능 함정): 풀은 보통 `Destroy` 대신 `SetActive(false)` 토글이다. 대량을 한 프레임에 동시 `SetActive(true)` 하면 OnEnable/Awake가 몰려 스파이크가 난다. 프레임당 N개로 나눠 활성화하거나 로딩 시 미리 워밍업한다.

도메인 리로드를 끈 상태에서 정적 풀은 Enter Play Mode 간 상태가 잔존해 오염된다(runtime-architecture.md).

---

## 7. 코루틴 vs async/Awaitable (Unity 6)

`Awaitable`은 내부 풀링돼 `Task` 대비 할당이 크게 준다. 코루틴엔 없는 스레드 전환(`BackgroundThreadAsync`↔`MainThreadAsync`)도 된다. Awaitable/UniTask/코루틴 선택, 취소 토큰(`destroyCancellationToken`), `async void` 금지 등 실행 모델은 runtime-architecture.md. `WaitForSeconds` 캐싱 등 yield 할당 함정은 memory-gc.md.

성능 관점 요약: 기본/의존성 최소는 내장 `Awaitable`, 복잡한 async 조합과 폭넓은 무할당이 필요하면 UniTask가 더 성숙하다.

---

## 8. Jobs / Burst / DOTS 도입 판단

도입 정당화 신호: 데이터 병렬성(동일 연산 대량 반복), 대규모 엔티티(수천+), 결정론 요구, CPU 바운드 계산(물리 쿼리, 스폰, 메시 생성).

도입하지 않는 경우: UI, 프로토타입, 소규모, 개별 분기 로직, GC 참조 많은 게임플레이 코드. 오버엔지니어링이 된다.

Burst 호환 제약 (흔한 위반):
- 관리 타입/참조 타입(class) 사용 불가. struct만. 관리 배열 대신 `NativeArray<T>`.
- Native 컨테이너를 로컬 변수로 복사하거나 struct 필드로 저장하지 않는다. 값은 static 메서드 인자로만 전달한다.
- `StructLayout`은 Sequential/Explicit만 지원하고 Pack은 미지원.
- Burst는 Job System과 함께일 때 최적이다. Job 안전성 규칙이 aliasing 최적화를 허용한다.

점진 도입:
- 무거운 계산만 Jobs+Burst로 분리하고(레이캐스트 배치, 메시, 파티클 시뮬) 나머지는 MonoBehaviour로 둔다. 대부분 프로젝트에 이 하이브리드가 맞다.
- 풀 ECS/DOTS는 학습곡선과 툴링 부담이 있어 대규모 시뮬/대량 엔티티에서만 정당화된다.

JobHandle/Dispose:
- `Schedule()` 직후 `.Complete()` 하면 병렬 이득이 없다. 스케줄과 Complete 사이에 메인스레드가 다른 일을 하게 배치한다. 보통 다음 프레임이나 LateUpdate에서 Complete 한다.
- 같은 데이터를 쓰는 Job은 JobHandle 의존성 체인으로 연결한다.
- Native 컨테이너는 `Dispose`하거나 `Allocator.TempJob`/`[DeallocateOnJobCompletion]`을 쓴다. Job 완료 전 접근/Dispose는 안전성 에러다.

---

## 9. 렌더링 CPU 비용과 배칭

배칭 CPU 비용의 핵심은 SRP Batcher와 GPU Resident Drawer다. 활성 조건·CBUFFER 요건·GPU Occlusion Culling은 rendering-urp.md. 배칭을 깨는 스크립트 실수(`renderer.material` 인스턴스 복제와 누수, `MaterialPropertyBlock`의 SRP Batcher 비호환, `sharedMaterial` 공유)는 shader-vfx.md.

LOD/컬링:
- `LODGroup`으로 거리별 메시를 전환한다. Frustum culling은 자동이다.
- `Camera.farClipPlane`을 줄이고 `Camera.layerCullingDistances`로 레이어별 컬 거리를 지정해 원거리 소품을 조기 컬한다. 커스텀 가시성 판정은 `CullingGroup` API로 한다.

측정은 Frame Debugger로 드로우콜 단위 배칭 붕괴 원인을 진단한다.

---

## 10. UI 최적화

uGUI Canvas:
- Canvas 내 한 요소가 바뀌면 Canvas 전체가 리빌드/리배치된다. 큰 Canvas는 수 ms 스파이크가 난다.
- 핵심은 Canvas 분할이다. 각 Canvas는 독립 배칭 섬이다. 갱신 빈도별로 나눠 자주 바뀌는 것만 작은 서브캔버스에 둔다. 같은 Canvas의 요소는 같은 Z/머티리얼/텍스처를 써야 배칭이 유지된다.
- Layout Group과 Content Size Fitter는 리빌드 때 `LayoutElement`마다 GetComponent를 불러 무겁다. 가능하면 정적 위치/앵커로 대체한다.

매 프레임 UI 갱신을 이벤트/더티 플래그로:

```csharp
// Don't: 값이 안 바뀌어도 매 프레임 리빌드 + 문자열 할당
void Update() { scoreText.text = score.ToString(); }

// Do: 값이 바뀔 때만
void SetScore(int v) { if (v == score) return; score = v; scoreText.text = v.ToString(); }
```

UI Toolkit 런타임 (Unity 6.x):
- 리테인드 비주얼 트리라 전체 리빌드를 최소화하고 요소 수에 예측 가능하게 스케일한다. 요소가 많을수록 uGUI(비선형 급락) 대비 유리하다.
- UI Toolkit vs uGUI 프레임워크 선택과 구성 차이는 packages.md.

안티패턴:
- 상호작용 없는 Image/Text의 Raycast Target을 끈다. 레이캐스트 순회가 준다.
- 투명 이미지, 겹친 풀스크린 이미지 같은 오버드로우를 줄인다.
- Mask는 추가 드로우콜과 스텐실을 쓴다. `RectMask2D`가 더 저렴한 경우가 있다.

---

## 11. 애니메이션과 오디오

Animator:
- 화면 밖 Animator도 `AlwaysAnimate`면 Update 비용이 계속 든다. 다수 오프스크린 Animator가 CPU를 잡아먹는 주 원인이다.
- Culling Mode를 상황에 맞춘다. `CullUpdateTransforms`는 안 보일 때 본 애니/IK 평가를 skip하되 statemachine/root motion은 유지한다(변위 있는 컨트롤러). `CullCompletely`는 안 보이면 완전 정지한다(변위 없는 컨트롤러).
- 임포트의 Optimize Game Objects로 본 계층 트랜스폼을 제거해 갱신 비용을 줄인다. 본에 오브젝트를 직접 붙이면 노출 목록이 필요하다.
- 단순 UI 트윈/시퀀스에 Animator는 과중하다. Playables API나 코드 트윈(무할당 옵션)이 가볍다. Animator는 복잡한 상태머신/블렌드에 쓴다.

오디오:
- 동시 재생 voice 수에 상한을 둔다. Load Type은 짧은 SFX는 Decompress On Load, 긴 BGM은 Streaming, 중간은 Compressed In Memory.
- volume/pitch 등 오디오 파라미터를 Update에서 매 프레임 갱신하지 않는다. 값이 바뀔 때만 갱신한다.

애니메이션 이벤트가 SendMessage 기반이면 리플렉션 비용이 든다. 델리게이트/인터페이스 직접 호출이나 ScriptableObject 이벤트로 바꾼다.

---

## 12. 프로파일링 규율

도구:
- Unity Profiler: 프레임별 CPU/GPU/메모리/렌더/물리 타임라인. 병목 식별 1차 도구.
- Profile Analyzer(패키지): 여러 프레임 집계, 최적화 전후 두 캡처 비교.
- Memory Profiler(패키지): 힙 스냅샷, 릭과 네이티브+매니지드 메모리 상세.
- Frame Debugger: 드로우콜 단위 스텝, 배칭 붕괴 원인 진단.

커스텀 계측:
- `ProfilerMarker`(Unity.Profiling)로 구간을 표시한다. 릴리스(비개발) 빌드에서 Begin/End는 제로 오버헤드다.

```csharp
static readonly ProfilerMarker s_Marker = new("Enemy.Tick");
void Tick() { using (s_Marker.Auto()) { /* ... */ } }
```

- 커스텀 지표(스폰된 적 수 등)는 `ProfilerCounter`(Profiling Core 패키지)로 표시한다.
- Deep Profiling은 모든 메서드를 자동 계측하나 오버헤드가 커 결과를 왜곡한다. 커스텀 마커가 정확하다.

측정 환경:
- 에디터 측정에는 에디터 전용 오버헤드(Gizmo, 인스펙터, 안전성 검사)가 섞여 오판을 부른다.
- 실측은 Development Build를 타깃 디바이스에서, Deep Profile을 끄고 한다. IL2CPP 릴리스 성능은 에디터 Mono와 크게 다르다.
- 측정 없이 미세최적화하지 않는다. 병목을 식별한 뒤 최적화하고, 최적화 변경에는 프로파일러 전후 수치를 근거로 붙인다.

---

## 13. 횡단 안티패턴과 프로젝트 설정

Debug.Log:
- 릴리스 빌드에서도 `Debug.Log`는 실행된다. 자동 제거되지 않고, 로그가 필터링돼도 인자의 문자열 연결/박싱 평가는 발생한다.
- 제거: 커스텀 래퍼 메서드에 `[System.Diagnostics.Conditional("SYMBOL")]`을 붙이면 심볼 미정의 시 호출 자체가(인자 평가 포함) 컴파일에서 제거된다. 가장 깔끔하다. `#if UNITY_EDITOR` / `#if DEVELOPMENT_BUILD`로 감싸는 방법도 있다.

```csharp
[System.Diagnostics.Conditional("DEVELOPMENT_BUILD"), System.Diagnostics.Conditional("UNITY_EDITOR")]
public static void Log(string msg) => Debug.Log(msg);
```

메시징:
- `SendMessage`/`BroadcastMessage`는 리플렉션 기반이라 느리고 타입 안전성이 없다. 대체는 deprecated-api.md.

알고리즘:
- 매 프레임 정렬/선형검색/문자열 파싱은 캐싱/증분 갱신/적절한 자료구조(Dictionary/HashSet)로 바꾼다.

Unity 6 프로젝트 설정:
- 릴리스 스크립팅 백엔드는 IL2CPP(AOT, Mono 대비 성능). 일부 플랫폼은 필수다.
- Managed Code Stripping은 IL2CPP에서 항상 켜지고 리플렉션/직렬화 접근 코드를 잘못 제거할 수 있다. 스트리핑 레벨·`link.xml`·`[Preserve]`는 build-project.md.
- Incremental GC는 Unity 6 기본 활성이다.
- Auto Graphics API가 켜져 있으면 모든 그래픽 API의 셰이더 변형을 컴파일해 빌드/메모리가 팽창한다. 필요한 API만 수동 지정한다. (기본값 변경 여부는 미확인.)
