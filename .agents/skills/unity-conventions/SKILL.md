---
name: unity-conventions
description: >-
  Unity 6 (URP) 프로젝트에서 C# 코드를 작성·리뷰하거나 빌드·렌더·에디터·셰이더 설정을 만질 때 사용한다.
  C# 9 언어 상한, fake null, 직렬화, 폐기 API, async 수명, GC 할당, 배칭, RenderGraph처럼
  AI가 옛 튜토리얼 기반으로 자주 틀리는 Unity 규약을 다룬다. Assets/Scripts 아래 코드나 셰이더,
  에디터 확장, asmdef·Player Settings·URP Asset을 건드리는 작업이면 참조한다.
---

# Unity 컨벤션 (Unity 6 / URP)

기준 버전: Unity 6000.x (6.5까지 반영) · 최종 확인: 2026-07

코드를 쓰기 전에 이 목록을 먼저 본다. 각 규칙의 상세와 근거는 `references/` 아래 소유 문서에 있다. 스타일이 갈리는 항목은 **기존 코드베이스의 실제 스타일이 최상위 규칙**이다. 프로젝트의 실제 Unity 버전을 먼저 확인하고, 표기 버전이 프로젝트 버전보다 높은 규칙은 적용하지 않는다.

## 최악의 실수를 막는 횡단 규칙

1. **C# 언어 버전은 9.0 상한이다.** file-scoped namespace, global using, record struct, collection expression(`[..]`), primary constructor, `required`는 컴파일되지 않는다. 상향(`-langversion`)하지 않는다. (csharp-style.md)

2. **UnityEngine.Object에 `?.`·`??`를 쓰지 않는다.** 파괴된 오브젝트는 `== null`이 true인 fake null이고 `?.`는 이를 우회해 `MissingReferenceException`을 낸다. 명시적 `!= null`/`== null`을 쓴다. 델리게이트의 `?.Invoke()`는 예외다. (runtime-architecture.md)

3. **인스펙터 노출은 `public`이 아니라 `[SerializeField] private`다.** 프로퍼티 직렬화는 `[field: SerializeField]`. `static`/`const`/`readonly`는 직렬화되지 않으므로 튜닝값에 쓰지 않는다. (csharp-style.md)

4. **폐기 API를 생성하지 않는다.** `GetInstanceID`→`GetEntityId`(6.5 컴파일 에러), `Rigidbody.velocity`→`linearVelocity`, `FindObjectOfType`→`FindObjectsByType`, `WWW`→`UnityWebRequest`, `cmd.Blit`→`Blitter`, `.tag ==`→`CompareTag`, 컴포넌트 단축 프로퍼티(`rigidbody`, `camera`)는 제거됨. 대체 API에도 도입 버전이 있다 — 프로젝트 버전에 없는 새 API로 교체하지 않는다(양방향 확인). (deprecated-api.md)

5. **이벤트 구독은 `OnEnable`, 해제는 `OnDisable`로 대칭**을 맞춘다. 해제하려면 람다가 아니라 named 메서드로 등록한다. 비대칭은 누수와 중복 구독을 만든다. UnityEvent는 인스펙터 바인딩이 필요할 때만 쓴다(C# event 대비 느리고 GC 발생). (runtime-architecture.md)

6. **async 오용을 피한다.** `async void`는 이벤트 핸들러 외 금지, `.Result`/`.Wait()` 블로킹 금지, `Task.Run`에서 Unity API 접근 금지. 취소 토큰(`destroyCancellationToken`)을 체인 끝까지 전파한다. 신규 코드는 프로젝트에 UniTask가 있으면 UniTask, 없으면 내장 `Awaitable`을 쓴다 — UniTask 의존성을 임의로 추가하지 않는다. (runtime-architecture.md)

7. **핫패스에서 GC 할당 0을 지향한다.** Update/물리 콜백/코루틴 루프에서 LINQ, 클로저 캡처, 박싱, `params`, 매 프레임 `new` 컬렉션, `new WaitForSeconds`를 피한다. 반복 생성/파괴는 `UnityEngine.Pool`로 풀링한다. (memory-gc.md)

8. **Update를 최소화한다.** 빈 `Update()`를 지운다(등록 오버헤드). `GetComponent`/`Camera.main`은 `Awake`/`Start`에서 1회 캐싱한다. `GameObject.Find`류는 핫패스 금지, 초기화 1회만. 매 프레임 폴링 대신 이벤트로 바꾼다. (performance.md)

9. **물리는 FixedUpdate에서 rigidbody API로 한다.** 프레임 종속 이동에 `Time.deltaTime`을 곱한다. transform 직접 조작으로 물리 오브젝트를 움직이면 터널링·지터가 난다. FixedUpdate로 움직이는 Rigidbody는 Interpolate를 켠다. (performance.md, runtime-architecture.md)

10. **도메인 리로드 OFF를 가정해 static을 리셋한다.** Enter Play Mode 최적화를 켜면 static 필드·이벤트 핸들러·싱글톤이 플레이 세션 간 잔존한다. `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`으로 명시 리셋한다. (runtime-architecture.md)

11. **SRP Batcher 호환을 깨지 않는다.** 셰이더의 모든 머티리얼 프로퍼티를 `CBUFFER_START(UnityPerMaterial)` 블록에 넣는다. `MaterialPropertyBlock`은 SRP Batcher와 비호환이라 배칭을 깬다. `renderer.material` 접근은 머티리얼 인스턴스를 복제해 누수 위험을 만든다. 오브젝트별 변형은 목적에 맞게 고른다: 소수 변형은 별도 머티리얼 에셋 교체, 다수 동일 메시는 GPU 인스턴싱 per-instance 데이터, 배칭 포기를 감수할 때만 MPB. `sharedMaterial`을 직접 수정하면 에셋 원본과 모든 사용처가 함께 바뀐다. (shader-vfx.md)

12. **URP 커스텀 렌더는 RenderGraph API로 쓴다.** `Execute()`/`OnCameraSetup()`/`cmd.Blit`/`RenderTargetHandle`은 obsolete다. `RecordRenderGraph()` + `Blitter` + RTHandle을 쓴다. Compatibility Mode는 6.3부터 stripped. (rendering-urp.md)

13. **에디터 코드를 런타임과 격리한다.** 런타임 어셈블리가 `UnityEditor`를 참조하면 빌드가 CS0246으로 깨진다. 참조 방향은 항상 Editor → Runtime. 에디터에서 직렬화 필드를 바꿀 때는 `target.field` 직접 대입이 아니라 `SerializedObject`를 경유한다(undo·프리팹 오버라이드·멀티편집). (build-project.md, editor-scripting.md)

14. **Unity 에셋(.unity·.prefab·.asset·.meta)은 명시 요청 없이 수정하지 않는다.** GUID·바이너리 구조를 훼손하고 병합 불가 충돌을 만든다. 에셋과 `.meta`는 항상 함께 이동·커밋한다. 텍스트 코드·설정만 안전하게 편집한다. (build-project.md)

15. **셰이더 키워드 variant 폭발을 관리한다.** 머티리얼 체크박스는 `shader_feature`, 런타임 C# 토글은 `multi_compile`, variant 회피가 필요하면 `dynamic_branch`(6+). 이 셰이더 전용 키워드는 `_local`. 스트립된 variant는 런타임에 핑크 셰이더가 된다. (shader-vfx.md)

## references 목차

- **csharp-style.md**: C# 코드 스타일과 구조. C# 9 상한, 네이밍, SerializeField 직렬화, 포맷, 네임스페이스, 멤버 순서, MonoBehaviour 라이프사이클, record 대신 readonly struct, 스타일 강제 도구(.editorconfig·분석기).
- **runtime-architecture.md**: 런타임 아키텍처. async 실행 모델(Awaitable/UniTask/코루틴), 취소·수명, fake null 심층, 이벤트 메커니즘, ScriptableObject 아키텍처, DI와 싱글톤 대안, MonoBehaviour 실행 순서, 시간·물리 틱, 도메인 리로드와 static 리셋, 참조 연결.
- **deprecated-api.md**: 폐기·금지 API 블랙리스트. 등급별(컴파일 에러/obsolete/안티패턴/설정 종속) 정리. EntityId 마이그레이션, Find 계열 의미론, 렌더 파이프라인, 물리 리네이밍, SendMessage·리플렉션 메시징, Input System, 탐지·강제.
- **memory-gc.md**: 메모리·GC·풀링. 박싱·LINQ·클로저·foreach·yield 할당, 인크리멘탈 GC 수동 제어, `UnityEngine.Pool`과 GameObject 풀링, 문자열·컬렉션 버퍼 API, struct/Span/NativeArray, 이벤트·static·네이티브 누수, Addressables, 진단 워크플로우.
- **performance.md**: 런타임 CPU/GC 최적화. Update 루프, 컴포넌트·Transform 캐싱, 물리 설정, 힙/GC 요약, 오브젝트 풀링, async 선택, Jobs/Burst 도입 판단, 렌더 배칭, UI, 애니메이션·오디오, 프로파일링 규율.
- **rendering-urp.md**: URP 설정·성능. URP Asset·렌더러, RenderGraph 커스텀 패스 API, 라이팅·그림자·포스트프로세싱, 텍스처 임포트, SRP Batcher·GPU Resident Drawer 배칭, 업스케일링·AA, 프로파일링, 오버드로우.
- **shader-vfx.md**: 셰이더·VFX. Shader Graph/HLSL 경계, 커스텀 HLSL 통합과 6.5+ Reflection API, 키워드·variant 스트리핑, PSO 워밍업, 머티리얼·MPB·SRP Batcher·인스턴싱, C# 셰이더 제어, VFX Graph vs Shuriken, 플랫폼 이식성, 컴퓨트 셰이더, 디버깅.
- **editor-scripting.md**: 에디터 확장. UI Toolkit vs IMGUI, SerializedObject 경유 편집, 커스텀 인스펙터·PropertyDrawer, 바인딩·콜백, UXML/UxmlElement, EditorWindow 라이프사이클, 도메인 리로드 안전성, Undo·프리팹 정합성, 씬 뷰 툴, 에셋 훅, 메뉴·설정, Odin 공존.
- **build-project.md**: 빌드·프로젝트 설정. Build Profiles, asmdef 어셈블리 구조, Editor 코드 분리, 인크리멘탈·클린 빌드, 빌드 스크립팅·CI, Enter Play Mode Options, IL2CPP/Mono, 코드 스트리핑, Player Settings, Scripting Define Symbols, Package Manager, 버전 관리, 재현성.
- **packages.md**: 공식 패키지 지형. 성숙도·버전 고정·Package Manager 워크플로, Input System, Cinemachine 3, Addressables, Localization, 직렬화·비동기, ECS, UI, 네트워킹, 서드파티 라이브러리에서 흔한 구버전 API·수명 관리 실수.
