# Unity 폐기·금지 API 블랙리스트

기준 버전: Unity 6000.x (6.5까지 반영) · 최종 확인: 2026-07

목차
- 등급 구분
- 1. Unity 6.5에서 컴파일 에러로 승격
- 2. InstanceID → EntityId
- 3. FindObjectOfType 계열
- 4. 레거시 GameObject / Component / Transform 멤버
- 5. 렌더 파이프라인
- 6. 제거된 서브시스템
- 7. WWW → UnityWebRequest
- 8. Resources 폴더 / AssetBundle → Addressables
- 9. 런타임 IMGUI (OnGUI / GUI / GUILayout)
- 10. 리플렉션·문자열 기반 메시징
- 11. 코루틴·타이밍 → Awaitable
- 12. 물리 API 리네이밍
- 13. 씬 로딩·직렬화 레거시
- 14. 레거시 Input vs Input System
- 15. 탐지·강제

Unity 6.x에서 코드를 쓰거나 고칠 때 참조한다. 폐기됐거나 제거됐거나 안티패턴인 API와 그 대체를 담는다. LLM이 옛 튜토리얼 기반으로 생성하기 쉬운 심볼, API Updater가 자동으로 못 고치는 의미론 변경에 집중한다. 대상 Unity 6.0 ~ 6.5(6.6 알파 일부).

## 등급 구분

블랙리스트 항목은 성격이 다르다. 대응 전에 어느 등급인지 판별한다.

- **제거/컴파일 에러**: 쓰면 빌드가 깨진다. 무조건 대체.
- **Obsolete 경고(CS0618)**: 아직 동작하나 폐기 예정. 신규 코드 금지, 기존 코드 이관.
- **안티패턴**: obsolete가 아니고 유효하지만 성능·안전성 문제로 지양. 정당한 예외 영역이 있다.
- **프로젝트 설정 종속**: 프로젝트 설정·패키지 설치 여부에 따라 정당하거나 금지. 설정을 먼저 확인.

패키지 의존 API(Netcode, Addressables, Input System, XR)는 "폐기"가 아니다. 패키지 미설치 시 `type/namespace not found` 에러가 나지만, 설치돼 있으면 정당하다.

**대체 API에도 도입 버전이 있다.** 프로젝트 버전보다 새 API로 교체하면 옛 API 생성과 똑같이 컴파일이 깨진다. 예: `GetEntityId()`는 6.4+에만 존재하므로 그 미만 버전에서는 `GetInstanceID()`가 정상 API다. 교체 전에 프로젝트의 실제 버전을 확인한다(양방향 게이트).

---

## 1. Unity 6.5에서 컴파일 에러로 승격

6.5+에서 hard error가 되는 항목. 일부는 API Updater가 자동 치환한다.

| 옛 API | 대체 | 자동 업그레이드 |
|---|---|---|
| `GameObject.active` | `SetActive()` / `activeSelf` / `activeInHierarchy` | 불가(용도별 수동 판단) |
| `GameObject.SetActiveRecursively()` | `SetActive()` | 가능 |
| `InstanceID` 타입, `Object.GetInstanceID()` | `EntityId`, `Object.GetEntityId()` | 부분(2절) |
| `ModelImporter.isFileScaleUsed` 외 5종 | 에러 메시지 참조 | 가능 |
| Legacy Render Graph compiler | 새 Render Graph 인터페이스 | 불가(커스텀 SRP) |
| Entities `ForEach` | `IJobEntity` / `SystemAPI.Query` | 불가 |
| Entities `Aspects` | 명시적 컴포넌트 쿼리 | 불가 |
| VR Module | XR Module (6절) | - |
| ReplayKit API | 네이티브 플러그인 | - |

ModelImporter 6종: `isFileScaleUsed`, `normalImportMode`, `optimizeMesh`, `resampleRotations`, `splitTangentsAcrossSeams`, `tangentImportMode`.

`Screen.fullScreen`은 Android에서 6.5+ 동작이 바뀐다. Navigation Bar를 제어하지 않으므로 `AndroidApplication.currentWindowInsets`를 쓴다.

---

## 2. InstanceID → EntityId

`GetInstanceID()`는 6.4에서 경고, 6.5+에서 컴파일 에러다. 6.6 알파에서는 암시적 `int → EntityId` 캐스트가 제거된다.

`EntityId`는 64비트 struct(8바이트)다. 옛 `InstanceID`는 32비트 int였다. `EntityId → int` 변환은 상위 32비트를 조용히 잘라 손상된 식별자를 만들고 에러도 안 난다. 그래서 int 필드·딕셔너리 키·직렬화에 저장하던 코드는 타입을 바꿔야 한다.

### API 대체

| 옛 | 새 |
|---|---|
| `Object.GetInstanceID()` | `Object.GetEntityId()` |
| `Resources.InstanceIDToObject(int)` | `Resources.EntityIdToObject(EntityId)` |
| `Resources.InstanceIDIsValid(int)` | `Resources.EntityIdIsValid(EntityId)` |
| `Selection.instanceIDs` | `Selection.entityIds` |
| `Selection.activeInstanceID` | `Selection.activeEntityId` |
| `Selection.Contains(int)` | `Selection.Contains(EntityId)` |
| `EditorUtility.InstanceIDToObject(int)` | `EditorUtility.EntityIdToObject(EntityId)` |
| `SerializedProperty.objectReferenceInstanceIDValue` | `SerializedProperty.objectReferenceEntityIdValue` |
| `EditorApplication.hierarchyWindowItemOnGUI` | `hierarchyWindowItemByEntityIdOnGUI` |
| `LazyLoadReference<T>.instanceID` | `LazyLoadReference<T>.entityId` |
| `HierarchyProperty` | `HierarchyIterator` |

### 저장

식별자는 `EntityId` 타입 필드에 저장한다. 컬렉션은 `Dictionary<EntityId, V>`, `HashSet<EntityId>`를 쓴다. 영속화가 필요하면 `EntityId.ToULong` / `EntityId.FromULong`로 `ulong` 원시값을 저장·복원한다(int 아님).

금지: int 캐스트, `IntPtr`/`nint` 저장(32비트 플랫폼 실패), `ToString()` 파싱(포맷이 버전 간 변경됨).

### GetHashCode()로 대체하지 않는다

LLM이 자주 내놓는 오답이다. `GetHashCode()`는 식별자가 아니다. 서로 다른 오브젝트가 같은 해시코드를 공유할 수 있어 잘못된 오브젝트가 매칭된다.

```csharp
// Don't - 해시 충돌로 다른 오브젝트와 오매칭
int id = obj.GetInstanceID().GetHashCode();

// Do
EntityId id = obj.GetEntityId();
```

### 값의 의미에 의존하지 않는다

API Updater가 자동으로 못 잡는 지점이다. 수동 검토한다.

- **정렬 순서 없음**: `EntityId` 순서는 임의다. 생성 순서·씬 계층·로드 순서를 값에서 추론하지 않는다. InstanceID로 정렬하던 코드는 실제 속성(name, sibling index 등)으로 교체한다.
- **값 재사용**: 오브젝트 파괴 후 같은 값이 다른 오브젝트에 재사용될 수 있다.

---

## 3. FindObjectOfType 계열

6.0+에서 obsolete 경고다. 6.x에서 제거되진 않았지만 신규 코드에서 쓰지 않는다.

| 옛 | 새 |
|---|---|
| `FindObjectOfType()` | `FindFirstObjectByType()` (아무거나 OK면 `FindAnyObjectByType()`) |
| `FindObjectsOfType()` | `FindObjectsByType(FindObjectsSortMode)` |

### 정렬 순서를 보존한다

옛 `FindObjectsOfType()`는 항상 InstanceID 순으로 정렬했다. 의미를 보존하려면 `FindObjectsSortMode.InstanceID`를 쓴다. 성능만 보고 `None`으로 바꾸면 호출 간 순서가 달라져 순서 의존 코드(인덱스 0 사용, 결정적 리플레이·테스트)에서 버그가 난다.

```csharp
// 의미 보존
FindObjectsByType<T>(FindObjectsSortMode.InstanceID);
// 순서 무관하게 전체 처리할 때만 (더 빠름)
FindObjectsByType<T>(FindObjectsSortMode.None);
```

### 비활성 오브젝트를 명시한다

새 API의 기본값은 비활성 제외(`Exclude`)다. 옛 `FindObjectsOfType(true)`(비활성 포함)를 옮길 때 명시하지 않으면 비활성 오브젝트가 조용히 누락된다.

```csharp
// FindObjectsOfType(true)의 정확한 대응
FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
```

### 반복 호출은 안티패턴

폐기 여부와 별개로, Find 계열은 씬 전체를 스캔한다. Awake/Start에서 1회 캐싱하거나 `[SerializeField]` 참조 주입으로 대체한다. Update 루프·빈번한 호출에서는 금지, 초기화 1회는 허용.

---

## 4. 레거시 GameObject / Component / Transform 멤버

### 컴포넌트 단축 프로퍼티 (Unity 5에서 제거)

`component.rigidbody`, `.rigidbody2D`, `.camera`, `.audio`, `.renderer`, `.light`, `.animation`, `.collider`, `.particleSystem` 등은 모두 제거됐다. LLM이 옛 튜토리얼 때문에 여전히 생성한다.

```csharp
// Don't
rigidbody.linearVelocity = v;
// Do
GetComponent<Rigidbody>().linearVelocity = v;
```

### 기타

- `Transform.FindChild(string)` → `Transform.Find(string)` (오래전 obsolete).
- `GameObject.active`, `SetActiveRecursively()`: 1절 참조(6.5+ 컴파일 에러).

### 태그 비교

`.tag` 게터는 문자열 힙 할당(GC)을 일으킨다. `CompareTag`는 무할당이고 더 빠르며, 미정의 태그에는 예외를 던져 오타를 잡는다.

```csharp
// Don't - 매 호출 문자열 할당
if (gameObject.tag == "Enemy") { }
// Do
if (gameObject.CompareTag("Enemy")) { }
```

---

## 5. 렌더 파이프라인

Built-in Render Pipeline(BiRP)은 6.5+에서 공식 deprecation 프로세스가 시작됐다. 6.7 LTS까지 유지되고 최종 제거 버전은 미정이다. 신규 프로젝트는 URP 또는 HDRP를 쓴다.

URP 프로젝트에서 다음 BiRP 관례는 깨진다.

- **Standard Shader / Surface Shader**: URP 미지원. Shader Graph 또는 URP Lit/Unlit을 쓴다.
- **Post Processing Stack v2**(`PostProcessVolume`, `PostProcessLayer`): URP 비호환. URP 내장 Volume 프레임워크를 쓴다(별도 패키지 불필요).
- **옛 `Graphics.Blit` / 옛 CommandBuffer 커스텀 렌더링**: URP는 `ScriptableRenderPass` 기반이다. 6.5+에서 Legacy Render Graph compiler가 제거됐으므로 새 Render Graph 인터페이스로 이관한다. URP는 RTHandle 기반 `Blitter.BlitCameraTexture`를 권장한다(구체 obsolete 특성 여부는 미확인, 버전별 URP 릴리스 노트 확인).

프로젝트가 URP면 HDRP 심볼(`HDRenderPipelineAsset`, HDRP Volume 오버라이드)을 섞지 않는다. LLM이 두 파이프라인 API를 혼동해 생성하기 쉽다. HDRP의 2026 유지보수 모드 전환 방향은 미확인(공식 전략 페이지 확인 필요).

---

## 6. 제거된 서브시스템

### 레거시 VR/XR

빌트인 XR은 2019 deprecated, 2020에서 제거됐다. VR Module 자체가 6.5+에서 제거됐다.

- `UnityEngine.VR.VRSettings` → `UnityEngine.XR.XRSettings` (CS0619).
- 현대: XR Plugin Management 패키지 + OpenXR Plugin. Oculus XR Plugin은 deprecated.

### UNet / HLAPI

2018년 deprecated, 2022.2에서 완전 제거됐다.

- 제거 심볼: `NetworkView`, `NetworkServer`, 구 `NetworkBehaviour`, 구 `[SyncVar]`, `UnityEngine.Networking.*`.
- 대체: Netcode for GameObjects(`Unity.Netcode`). 주의: NGO에도 `NetworkBehaviour`·`SyncVar` 동명 개념이 있으나 시그니처가 다르다. LLM이 섞기 쉽다.

### 레거시 애니메이션

레거시 `Animation` 컴포넌트(`animation.Play("clip")`)는 제거는 아니나 지양한다. Animator/AnimatorController 또는 Playables API를 쓴다. 단축 `.animation` 프로퍼티는 제거됐다(4절).

`GUIText`, `GUITexture` 컴포넌트는 제거됐다. `UI.Text`(또는 TMP), `UI.Image`를 쓴다.

---

## 7. WWW → UnityWebRequest

`WWW` 클래스와 구 `WWWForm`은 obsolete다(2018.3부터). `UnityWebRequest` + `DownloadHandler`/`UploadHandler`를 쓴다.

UnityWebRequest 내부 폐기 시그니처:

```csharp
// Don't
if (req.isNetworkError || req.isHttpError) { }
// Do
if (req.result == UnityWebRequest.Result.ConnectionError ||
    req.result == UnityWebRequest.Result.ProtocolError) { }
```

`UnityWebRequest.Get(url)` 팩토리를 권장한다.

**HttpClient는 WebGL에서 안 된다.** 순수 .NET 소켓·`HttpClient`는 WebGL 브라우저 샌드박스에서 동작하지 않는다(WebGL은 UnityWebRequest만 XHR로 매핑). 플랫폼 이식성이 필요하면 UnityWebRequest를 쓴다.

---

## 8. Resources 폴더 / AssetBundle → Addressables

`Resources.Load`와 Resources 폴더는 폐기는 아니나 지양한다.

- Resources 폴더 전체가 하나의 번들로 빌드에 항상 포함된다(트리셰이킹 안 됨, 빌드 크기 증가).
- 앱 시작 시 인덱스를 구축하고 메모리에 상주한다. 세밀한 로드/언로드 제어가 안 된다.
- `Resources.LoadAll`은 폴더 전체 동기 로드로 힙 스파이크를 낸다. 런타임 게임플레이 중 큰 에셋 동기 로드는 프레임 스톨을 만든다.

대체는 Addressables(`LoadAssetAsync`, 비동기)다. 레거시 AssetBundle 직접 API(`AssetBundle.LoadFromFile`, manifest 수동 관리)도 Addressables가 상위 래핑한다.

Addressables는 별도 패키지다. "금지"가 아니라 "권장"이다. 프로토타입에서 Resources 소량 사용은 허용, 프로덕션 스케일·다양한 플랫폼에서는 지양.

---

## 9. 런타임 IMGUI (OnGUI / GUI / GUILayout)

런타임 게임플레이 UI에서 IMGUI는 안티패턴이다.

- `OnGUI`는 프레임당 힙을 할당한다(이벤트 객체). GC 스파이크가 난다.
- 즉시 모드라 레이아웃을 매 프레임 재계산한다.
- UI Toolkit 전환 이후 런타임용 IMGUI는 개선되지 않는다.

정당한 영역은 에디터 확장(커스텀 인스펙터·에디터 윈도우)과 개발용 디버그 오버레이뿐이다. 플레이어가 상호작용하는 실제 게임 UI는 UI Toolkit(VisualElement) 또는 uGUI(Canvas)를 쓴다. 디버그 용도임을 명시하지 않은 OnGUI 생성은 차단한다.

---

## 10. 리플렉션·문자열 기반 메시징

이 패턴들은 컴파일은 되지만 런타임에 조용히 실패한다. 테스트가 없으면 발견이 늦어져 LLM 생성 코드에서 특히 위험하다. "컴파일 통과"가 "정상 동작"을 뜻하지 않는다.

- **`SendMessage` / `BroadcastMessage` / `SendMessageUpwards`**: 리플렉션 + 컴포넌트 전수 탐색으로 매우 느리다. 메서드명이 문자열이라 오타·리팩토링 시 조용히 무반응. 대체: C# `event`/`delegate`, `UnityEvent`(인스펙터 노출), 직접 참조, 인터페이스.
- **`Invoke("Method", t)` / `InvokeRepeating(...)` / `StartCoroutine("MethodName")`**: 문자열 메서드명은 리팩토링에 취약하다. 최소한 `nameof(Method)`, 이상적으로는 직접 참조.

```csharp
// Don't
StartCoroutine("MyRoutine");
Invoke("Fire", 1f);
// Do
StartCoroutine(MyRoutine());
Invoke(nameof(Fire), 1f);
```

- **`GetComponent(string)` / `AddComponent(string)`**: 문자열 오버로드는 느리고 타입 안전하지 않다. `AddComponent(string)`은 deprecated. 제네릭 `GetComponent<T>()`를 쓴다.

---

## 11. 코루틴·타이밍 → Awaitable

`Awaitable`(Unity 2023.1 / 6.0+)은 코루틴을 대체한다. `await Awaitable.WaitForSecondsAsync(0.1f)`, `Awaitable.NextFrameAsync()`, `Awaitable.EndOfFrameAsync()` 등. `MonoBehaviour.destroyCancellationToken`(2022.2+)으로 취소한다.

`yield return new WaitForSeconds(t)`의 매 반복 할당은 GC를 압박한다. 캐싱·yield 할당 규칙은 memory-gc.md.

코루틴이 여전히 정당한 경우가 있다: 간단한 시퀀싱, 프레임 단위 대기, MonoBehaviour 수명과 자동 연동. 전면 금지가 아니다.

`async void`·`Task.Run`·메인스레드 복귀 등 async 오용 규칙은 runtime-architecture.md.

Mono → CoreCLR 이행이 6.x 로드맵에 있다. 신규 코드를 표준 async/await·Task 친화적으로 써두면 이행에 유리하다. 정확한 착지 버전은 미확인.

---

## 12. 물리 API 리네이밍

6.0+에서 obsolete 경고(CS0618)다. API Updater가 자동 치환한다.

| 옛 | 새 |
|---|---|
| `Rigidbody.velocity` | `Rigidbody.linearVelocity` |
| `Rigidbody2D.velocity` | `Rigidbody2D.linearVelocity` (+ `linearVelocityX/Y`) |
| `Rigidbody.drag` | `Rigidbody.linearDamping` |
| `Rigidbody.angularDrag` | `Rigidbody.angularDamping` |
| `Rigidbody2D.drag` | `Rigidbody2D.linearDamping` |
| `Rigidbody2D.angularDrag` | `Rigidbody2D.angularDamping` |

3D와 2D를 혼동하지 않는다. `angularVelocity`는 2D는 float(도/초), 3D는 Vector3(라디안/초)로 의미가 다르다.

### 시뮬레이션 / Raycast

- `Physics.autoSimulation` (obsolete) → `Physics.simulationMode` (enum `FixedUpdate`/`Update`/`Script`). 수동 시뮬레이션은 `simulationMode = Script` 후 `Physics.Simulate()`.
- `Physics2D.autoSimulation` → `Physics2D.simulationMode`.
- Physics2D의 `~NonAlloc` 계열은 deprecated다. `List`·버퍼를 받는 일반 오버로드로 대체한다. Physics(3D) `RaycastNonAlloc` 계열의 deprecated 여부는 미확정이다(현재도 동작). 대량 레이캐스트는 `RaycastCommand` + Job System으로 배치한다. 버퍼 재사용·할당 규칙은 memory-gc.md.

---

## 13. 씬 로딩·직렬화 레거시

### 씬 로딩 (5.3부터 obsolete)

```csharp
// Don't
Application.LoadLevel("Level1");
Application.LoadLevelAsync("Level1");
// Do - using UnityEngine.SceneManagement;
SceneManager.LoadScene("Level1");
SceneManager.LoadSceneAsync("Level1");
```

`Application.loadedLevel`/`loadedLevelName`도 `SceneManager` 프로퍼티로 대체됐다.

### JSON 직렬화

`JsonUtility`의 한계(최상위 배열·딕셔너리 미지원, 다형성 약함)와 Newtonsoft·SerializeReference 선택 기준은 packages.md. 여기서는 AOT 함정만 다룬다.

Newtonsoft·System.Text.Json으로 넘어갈 때 IL2CPP/AOT에서 리플렉션 스트리핑으로 깨진다. 리플렉션으로만 쓰이는 타입은 AOT 컴파일러가 코드를 생성하지 않아 `ExecutionEngineException`이 난다. Unity 공식 배포 Newtonsoft(`com.unity.nuget.newtonsoft-json`)가 IL2CPP 대응을 포함해 순수 nuget보다 안전하고, `link.xml`로 스트리핑을 막는다(스트리핑 상세는 build-project.md).

### 오용 경고 (유효하지만 오남용)

- `PlayerPrefs`: 소량 설정용이다. 세이브 데이터·대량·민감정보를 저장하지 않는다(평문).
- `Application.persistentDataPath`: 유효하나 플랫폼별 경로가 다르고 메인스레드 외 접근에 주의한다.

---

## 14. 레거시 Input vs Input System

레거시 Input Manager(`Input.GetKey`, `GetAxis`, `mousePosition`, `touches`)는 폐기가 아니다. 전면 금지는 부적절하고, 프로젝트의 **Active Input Handling** 설정(Player > Other Settings)에 종속된다.

- `Input System Package (New)` 전용이면 레거시 `Input.*` 호출은 런타임 `InvalidOperationException`이다. 이 설정에서는 금지.
- `Both`면 둘 다 동작하나 섞으면 중복 처리·혼란. `Input Manager (Old)`면 레거시만 가능.

**Both 함정**: 마우스 좌표(`Input.mousePosition` vs `Mouse.current.position`), 터치(`Input.touches` vs `Touchscreen`), 게임패드에서 두 API가 다른 값·타이밍을 반환한다. LLM은 거의 항상 레거시 Input을 생성하므로 프로젝트 설정을 먼저 확인한다. 신규 프로젝트는 Input System 패키지(`UnityEngine.InputSystem`)를 권장한다.

---

## 15. 탐지·강제

### 컴파일러

- `[Obsolete("msg")]` → CS0618 경고.
- `[Obsolete("msg", true)]` → CS0619 컴파일 에러. Unity가 6.5에서 InstanceID 등을 이 방식으로 승격했다.
- CI 강제: `-warnaserror`(csc.rsp / Player 설정)로 경고를 에러화. 특정 경고는 `-nowarn`으로 제외. .NET subset 등에서 원치 않는 CS0618이 에러화될 수 있어 nowarn 목록을 관리한다.

### API Updater

UnityUpgradable 특성 기반으로 obsolete 타입·멤버를 자동 치환한다.

- **커버**: 단순 1:1 리네이밍(`velocity → linearVelocity`, `VRSettings → XRSettings`).
- **커버 못함**: 의미론 변경(EntityId 정렬·저장 타입, sort mode 선택, inactive 포함 여부), 패키지 임포트 후 일부 인스턴스 누락. 수동 검토 목록이 필요하다.

### 린트·커밋 게이트

- Roslyn 분석기 + `.editorconfig`로 WWW/OnGUI/SendMessage/Find*/문자열 GetComponent 등을 차단할 수 있다. 단 Unity 번들 Roslyn이 구버전(2022)이라 최신 소스 제너레이터 일부가 미동작할 수 있다.
- 정규식 기반 pre-commit으로 블랙리스트 심볼을 grep 차단하는 편이 분석기보다 도입이 쉽다. 문자열 내 언급 등 오탐은 관리한다.

### 출처 갱신

버전별 Upgrade Guide, ScriptReference의 obsolete 표기(버전 URL로 조회), Planned Breaking Changes 스레드가 1차 출처다. 개인 블로그·영상은 교차 확인 후에만 채택한다.
