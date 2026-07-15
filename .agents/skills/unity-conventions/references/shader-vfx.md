# 셰이더·VFX 컨벤션 (Unity 6.x)

기준 버전: Unity 6000.x (6.5까지 반영) · 최종 확인: 2026-07

목차
- Shader Graph vs 손 HLSL 경계
- 커스텀 HLSL 통합
- 셰이더 키워드와 variant
- Variant 스트리핑
- 컴파일 시간과 런타임 워밍업
- 머티리얼 프로퍼티 접근
- MaterialPropertyBlock, SRP Batcher, 인스턴싱
- C#에서 셰이더/머티리얼 제어
- VFX Graph vs Particle System(Shuriken)
- VFX Graph 성능과 C# 연동
- 플랫폼·그래픽스 API 이식성
- RenderGraph 커스텀 렌더 패스
- 컴퓨트 셰이더
- 디버깅·검증

Unity 6.0~6.5(URP/HDRP)에서 셰이더, 머티리얼, VFX, 컴퓨트, 커스텀 렌더 패스 코드를 쓸 때 참조한다. 규칙마다 이유를 한 줄로 달았고, 잘못 쓰기 쉬운 곳은 Don't/Do 코드를 병기했다. 버전 의존 사실에는 버전을 표기했다.

패키지↔엔진 대응: Shader Graph / URP / VFX 패키지 17.0.x ≈ Unity 6.0, 17.4.x ≈ 6.3~6.4, 17.5.x ≈ 6.5. "6.5+"는 패키지 17.5 이상을 뜻한다.

## Shader Graph vs 손 HLSL 경계

- 프로토타입, SubGraph 재사용 라이브러리, 셰이더 초심자 작업은 Shader Graph로 한다. 재사용 로직은 SubGraph로 분리해야 시각 diff와 라이브러리화가 가능하다.
- 성능 민감 셰이더는 손 HLSL로 쓰거나, Shader Graph를 쓰되 "View Generated Shader"로 생성 코드를 검수한다. 생성 HLSL은 중복 연산을 남길 수 있다.
- Custom Function 노드에 HLSL을 String으로 길게 넣지 않는다. IDE 지원과 버전관리 diff를 잃으면서 그래프 오버헤드는 그대로 남는다. 코드가 길면 File 모드나 별도 .hlsl로 뺀다.
- Shader Graph의 프로퍼티 Reference 문자열은 C#의 `SetFloat`/`SetColor` 키와 정확히 일치시킨다. 불일치는 코드-그래프 어긋남의 주원인이며 조용히 무시된다.
- 6.5+ SubGraph 입력: Color 입력은 컬러피커로, Float 입력은 integer/enum/slider 제약을 걸 수 있고, 입력 커넥터를 비활성화해 상수 전용 편집을 강제할 수 있다.
- 6.5+ Expression 노드: 긴 수식은 다수 Math 노드 대신 한 노드에서 계산해 가독성을 올린다.
- 6.5+ Switch 노드: 다분기는 Branch/Comparison 조합 대신 Switch 노드로 처리한다. Enum 모드 Float Property를 입력으로 받아 케이스를 도출한다.

버전관리: .shadergraph는 JSON 텍스트지만 라인 diff가 사람이 읽기 어렵다. 한 파일은 한 사람이 소유·편집하고 동시 편집을 피한다. 크게 쪼갤수록(SubGraph) 충돌면이 준다. 파일 이동 시 .meta를 반드시 동반해 GUID를 유지한다.

## 커스텀 HLSL 통합

### Custom Function 노드

- File 모드를 기본으로 쓴다. 최종 셰이더에 include 참조를 주입하므로 버전관리, IDE, 재사용이 된다. String 모드는 그래프 내 임시 코드에만 쓴다.
- File 모드 .hlsl에는 include 가드를 파일마다 고유 id로 넣는다. 없으면 중복 로드로 재정의 컴파일 에러가 난다.

```hlsl
#ifndef MYEFFECT_INCLUDED
#define MYEFFECT_INCLUDED
// ... 함수 ...
#endif
```

- File 모드 함수 파라미터는 bare 타입 대신 struct 타입을 쓴다. 그래프가 텍스처+샘플러+스케일을 함께 전달한다.

```hlsl
// Don't: bare 타입 (File 모드 파라미터)
void Sample_float(Texture2D tex, SamplerState s, float2 uv, out float4 c) { ... }
// Do: struct 타입
void Sample_float(UnityTexture2D tex, UnitySamplerState s, float2 uv, out float4 c) {
    c = SAMPLE_TEXTURE2D(tex, s, uv);
}
```

- 함수명에 precision 접미를 붙이면 노드가 자동 선택한다: `_float`(전정밀), `_half`(절약, 플랫폼 한정). String 모드 Body에서는 `half`/`float` 대신 `$precision` 토큰을 쓴다.

### Shader Function Reflection API (6.5+)

HLSL 함수를 어노테이션만으로 그래프 노드로 노출한다. Custom Function 노드나 C# 보일러플레이트가 필요 없다. 재사용·메뉴 등록형 노드는 이 방식으로, 그래프 내 임시 코드는 Custom Function으로 나눈다.

절차:
1. `Assets > Create > Shader > Empty HLSL`로 파일 생성.
2. 상단에 `#include "ShaderApiReflectionSupport.hlsl"`. 빠지면 컴파일 실패한다.
3. 노출할 함수 선언 앞에 `UNITY_EXPORT_REFLECTION`.

```hlsl
#include "ShaderApiReflectionSupport.hlsl"

///<funchints>
///  <sg:ProviderKey>MyLib</sg:ProviderKey>
///  <sg:DisplayName>Tint</sg:DisplayName>
///  <sg:SearchCategory>Custom/Color</sg:SearchCategory>
///</funchints>
///<paramhints name="color"><sg:Color/></paramhints>
UNITY_EXPORT_REFLECTION
void Tint_float(float4 color, float amount, out float4 result) { result = color * amount; }
```

- `<sg:ProviderKey>`가 없으면 Create Node 메뉴에 뜨지 않는다.
- `paramhints`의 `name`은 HLSL 파라미터명과 글자 단위로 일치해야 한다.
- 표준 HLSL 시그니처(typed in/out)면 포트가 자동 반영된다.

### SRP 이식 매크로

- 텍스처/샘플러는 raw HLSL 대신 매크로를 쓴다. 플랫폼별 선언 차이를 흡수한다: `TEXTURE2D(name)`, `SAMPLER(sampler_name)`, `SAMPLE_TEXTURE2D(tex, sampler, uv)`.
- 좌표 변환은 SRP core의 `SpaceTransforms.hlsl` 함수를 쓴다: `TransformObjectToHClip`, `TransformObjectToWorld`, `TransformWorldToView`.
- URP/HDRP 공용 코드는 SRP core 매크로에만 의존하고, 파이프 전용 경로는 분기한다. URP include는 `Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl`, core 매크로는 `.../core/ShaderLibrary/Common.hlsl`.
- 정밀도는 `half`/`float` 직접 대신 `real`을 쓴다. 플랫폼별 half/float로 치환되어 이식성이 높다.

## 셰이더 키워드와 variant

키워드 n개는 최대 2^n variant를 만들고 여러 키워드셋이 곱해지면 폭증한다. 타입 선택이 variant 수, 빌드 시간, 용량을 좌우한다.

| 상황 | 타입 |
|---|---|
| 머티리얼 프로퍼티로만 제어, C#에서 안 바꿈 | `shader_feature` |
| C# 스크립트로 런타임 토글, 변형 유지 필요 | `multi_compile` |
| 런타임 토글, GPU 여유 있고 variant 폭발 회피 | `dynamic_branch` (Unity 6+) |

- 기본은 `shader_feature`다. 머티리얼이 실제 참조하는 조합만 컴파일하므로 빌드가 가볍다. 함정: 빌드에 없는 조합을 런타임에 요청하면 근접 variant로 대체되어 시각이 어긋난다. 런타임 C# 토글에는 쓰지 않는다.
- `multi_compile`은 모든 조합을 항상 컴파일한다. 런타임 토글에만 쓴다. 남용하면 variant가 수천 개로 는다.
- `dynamic_branch`(Unity 6 정식): 키워드를 uniform 정수로 바꿔 단일 프로그램에 모든 분기를 넣고, draw call마다 상태를 GPU로 전달한다. variant 폭발이 없다. 대가는 GPU 런타임 분기 비용이므로 프로파일이 필수다. 제약: `if` 문만 가능하고 `#if`/`#ifdef` 전처리기는 항상 false로 평가된다. 같은 키워드를 dynamic_branch와 shader_feature/multi_compile로 동시 선언하면 dynamic_branch가 우선한다.
- 이 셰이더에서만 쓰는 키워드는 `_local` 접미를 붙인다(`multi_compile_local`, `shader_feature_local`). 전역 키워드 슬롯은 유한하며 소진되면 빌드가 깨진다.
- 6.5+ 진단: `KEYWORD_TYPE_FLAG`로 선언 타입 확인, `VariantsUploadedToGpu` / `VariantsUploadedToGpuLastFrame` API로 프레임당 GPU 업로드를 추적한다.

키워드를 추가할 때 자문한다: C#에서 바꾸나(아니면 shader_feature) → 그래도 변형이 필요한가(아니면 dynamic_branch) → 이 셰이더만 쓰나(그럼 `_local`) → 추가 후 variant 수를 빌드 로그/ShaderStrippingReport로 확인.

(미확인: stage-specific 키워드의 정확한 접미 구문. 매뉴얼 SL-MultipleProgramVariants를 재확인한다.)

## Variant 스트리핑

- 빌드에서 반드시 필요한 셰이더는 Graphics Settings의 Always Included Shaders에 등록해 스트립을 막는다.
- GPU Resident Drawer/BRG를 쓰면 Project Settings > Graphics > Shader Stripping에서 BatchRendererGroup Variants를 Keep All로 둔다. 아니면 BRG 셰이더가 깨진다.
- 커스텀 스트리핑은 `IPreprocessShaders.OnProcessShader`(컴퓨트는 `IPreprocessComputeShaders`)에서 각 Pass 컴파일 직전 `data` 리스트의 불필요한 variant를 제거한다. 무엇을 지우는지 로깅하고, 콜백은 빌드 중 다수 호출되므로 로직을 가볍게 둔다. `callbackOrder`로 순서를 제어한다.
- 과도 스트립 조기 발견: strict shader variant matching을 켜면 실제 필요한데 스트립된 variant를 missing으로 식별한다.

핑크(분홍) 셰이더는 필요한 variant를 스트립해 런타임 요청이 대체·실패한 결과다. 예방: 런타임 토글 키워드를 multi_compile로 두고, Always Included Shaders에 등록하고, 실사용 variant를 ShaderVariantCollection으로 수집·프리로드하고, strict matching으로 빌드 전 검출한다. 진단: Editor.log의 ShaderStrippingReport, Frame Debugger에서 fallback 확인.

## 컴파일 시간과 런타임 워밍업

- 에디터 이터레이션: Project Settings > Editor의 Async Shader Compilation을 쓰면 컴파일 중 더미를 표시하고 백그라운드로 컴파일한다. CI는 Library/셰이더 캐시를 공유해 재컴파일을 피한다.
- 런타임 첫-사용 스톨(셰이더 컴파일 + PSO 생성)은 Unity 6의 PSO precooking으로 없앤다:
  - `GraphicsStateCollection`으로 PSO를 수집하고 `WarmUp()`(즉시 전체) 또는 `WarmUpProgressively(count)`(점진, 블로킹 회피)로 예열한다. 둘 다 JobHandle을 반환한다. 앱/씬 로딩 시퀀스에서는 progressive를 쓴다.
  - PSO Tracing으로 실제 사용된 PSO를 추적·수집한다. 6.5+는 codeless로 Graphics State Collection을 생성하고 `traceCacheMisses`로 누락 PSO를 자동 추적한다.
- Unity 6+에서는 GraphicsStateCollection + PSO tracing이 권장 경로다. 레거시 ShaderVariantCollection은 키워드/variant 프리로드 보조로만 쓴다(Graphics Settings의 Preloaded shaders에 등록하면 빌드 시작 시 자동 WarmUp).
- 6.5+ VFX: `VisualEffectAsset.PrewarmComputeShaders`로 VFX 컴퓨트 셰이더를 사전 워밍해 첫 재생 hitch를 줄인다.

## 머티리얼 프로퍼티 접근

- `Renderer.material` 접근은 즉시 머티리얼 인스턴스를 복제한다. 그 인스턴스는 직접 Destroy할 책임이 있다. 빌드에서 자동 회수되지 않는다.
- 에디터 스크립트에서는 `renderer.material`을 호출하지 않는다. 씬에 인스턴스가 남아 누수된다. `sharedMaterial`을 쓴다.
- `sharedMaterial` 수정은 그 머티리얼을 쓰는 모든 오브젝트에 영향을 준다.
- 멀티 서브메시는 `materials`/`sharedMaterials` 배열을 인덱스로 접근한다. `.materials` 접근도 전체를 인스턴스화한다.
- 프로퍼티 ID는 static 필드에 캐싱한다. 실행 중 불변이다.

```csharp
// Don't: 매 프레임 문자열 해싱
void Update() { mat.SetColor("_Color", c); }
// Do: ID 캐싱 후 int 오버로드
static readonly int ColorId = Shader.PropertyToID("_Color");
void Update() { mat.SetColor(ColorId, c); }
```

## MaterialPropertyBlock, SRP Batcher, 인스턴싱

- `Renderer.SetPropertyBlock`으로 per-object 값을 바꾸면 SRP Batcher가 깨진다. MPB는 per-object CBUFFER 오버라이드라 SRP Batcher의 persistent buffer 전제와 충돌한다.
- 셰이더를 SRP Batcher 호환으로 만들려면 머티리얼로 바뀌는 모든 프로퍼티를 하나의 CBUFFER 블록에 선언한다. 프로퍼티가 블록 안팎으로 흩어지면 비호환으로 배칭에서 빠진다. 셰이더 인스펙터에 호환 여부가 표시된다.

```hlsl
CBUFFER_START(UnityPerMaterial)
    float4 _Color;
    float  _Smoothness;
CBUFFER_END
```

- SRP Batcher는 draw call을 합치지 않고 render-state 변경을 줄인다. Built-in RP는 미지원이다.

per-instance 데이터 경로:
- 소수·간단: 셰이더에 GPU Instancing 매크로(`UNITY_INSTANCING_BUFFER_START/END`, `UNITY_DEFINE_INSTANCED_PROP`, `UNITY_ACCESS_INSTANCED_PROP`)를 넣고 머티리얼의 Enable GPU Instancing을 켠다. 이 경로에서는 MPB가 정상 경로다.
- 대량·GPU 구동: GraphicsBuffer/StructuredBuffer로 전달한다(RenderMeshIndirect 등).

Unity 6 GPU Resident Drawer(URP/HDRP)의 셰이더 요건:
- BRG는 DOTS Instancing만 지원한다. 전통적 GPU instancing 셰이더는 BRG에서 동작하지 않는다. Shader Graph 셰이더는 기본 대응하고, 커스텀 셰이더는 `UNITY_DOTS_INSTANCING_START` 매크로가 필요하다.
- 인스턴스 데이터는 GPU 버퍼에 상주하며 CPU는 값이 바뀔 때만 업로드한다. GPU Resident Drawer를 쓰면 per-object 값은 MPB 대신 DOTS instancing 버퍼로 넘긴다.
- 활성화 조건(Forward+, SRP Batcher, BatchRendererGroup Variants = Keep All 등)은 rendering-urp.md.

## C#에서 셰이더/머티리얼 제어

- 키워드는 반복 토글이면 문자열 오버로드 대신 `LocalKeyword`/`GlobalKeyword` struct를 캐싱해 쓴다. 문자열 오버로드는 느리다. `LocalKeyword`는 특정 Shader/ComputeShader 인스턴스 전용이라 같은 이름이라도 다른 셰이더엔 못 쓴다.

```csharp
// Don't: 문자열 반복
mat.EnableKeyword("_EMISSION");
// Do: struct 캐싱 + 상태 지정
static readonly LocalKeyword Emission = new(shader, "_EMISSION");
mat.SetKeyword(Emission, isOn);
```

- 머티리얼/셰이더 스코프는 `Material.SetKeyword`, 전역은 `Shader.SetKeyword`. `SetKeyword(keyword, bool)`가 Enable/Disable보다 분기 없이 편하다.
- setter는 모두 int ID 오버로드를 쓰고 PropertyToID를 캐싱한다.
- 버퍼는 Unity 6에서 `GraphicsBuffer`가 권장이다(geometry+compute 겸용, target 지정 유연). ComputeBuffer도 유효하나 GraphicsBuffer로 수렴한다. 수명은 `using` 또는 명시적 `Dispose()`/`Release()`로 관리한다. 네이티브 리소스라 GC로 자동 해제되지 않고, 바인딩 중 Dispose하면 안 된다.
- `Shader.SetGlobalFloat/Vector/Color/Texture`는 진짜 씬 전역(포그 밀도, 시간, 바람)에만 쓴다. 전역 상태는 암묵 의존과 디버깅 난이도를 늘린다. per-material 값은 머티리얼/MPB로 둔다. 전역 setter는 머티리얼 Properties에 노출된 프로퍼티엔 먹지 않는다(미노출 변수용이다).

## VFX Graph vs Particle System(Shuriken)

| 항목 | Shuriken | VFX Graph |
|---|---|---|
| 파티클 수 | 수천 | 수백만 |
| 렌더파이프 | Built-in/URP/HDRP | URP/HDRP만 |
| 시뮬레이션 | CPU | GPU(compute) |
| 물리 | Unity 콜라이더 충돌 | 커스텀(depth/SDF/plane), 실제 콜라이더 X |
| C# 파티클 접근 | 배열 read/write, 충돌 콜백 | Exposed property/event만 |

- Shuriken을 쓴다: compute 미지원 플랫폼, 파티클을 C#으로 직접 조작, 물리 콜라이더 충돌, 게임플레이 상호작용, 수천 개로 충분할 때.
- VFX Graph를 쓴다: compute 지원 플랫폼, 수백만 파티클, 대규모 효과, 버퍼 기반 환경 상호작용.
- VFX Graph는 compute shader가 필수다. WebGL2는 compute가 없어 VFX Graph를 못 쓴다(WebGPU/Vulkan/Metal은 지원). 저사양 타깃은 `SystemInfo.supportsComputeShaders`로 분기해 Shuriken 폴백을 별도로 만든다.
- 코드 제어: Shuriken은 `ParticleSystem`의 `Emit()`, `GetParticles()/SetParticles()`, `OnParticleCollision`. VFX Graph는 `VisualEffect`의 `Play()/Stop()/SendEvent()`, `SetFloat/SetVector/SetTexture`. VFX Graph는 파티클 배열에 직접 접근할 수 없다(GPU 상주).

## VFX Graph 성능과 C# 연동

- Initialize Particle의 Capacity는 필요 최소로 잡는다. 크게 잡으면 spawn rate가 낮아도 메모리·시뮬 비용이 선할당된다.
- 컬링용 Bounds를 정확히 둔다. 부정확하면 잘못 컬링되거나 오버컬한다.
- 큰 반투명 파티클을 다수 쓰면 fill-rate 병목(오버드로우)이 생긴다. 파티클당 텍스처 샘플 수를 줄인다.
- Exposed 설정: Blackboard에서 프로퍼티의 Exposed를 켜야 스크립트가 접근한다. 접근은 ID를 캐싱한다.

```csharp
// Don't: 존재 검증 없이 문자열 반복
vfx.SetFloat("Speed", v);
// Do: ID 캐싱 + 존재 확인
static readonly int SpeedId = Shader.PropertyToID("Speed");
if (vfx.HasFloat(SpeedId)) vfx.SetFloat(SpeedId, v);
```

- 존재하지 않는 Exposed Property명에 SetFloat하면 조용히 무시된다. `HasFloat`/`HasVector`로 검증한다. 컴포넌트 disable/enable, Reinit 시점에 프로퍼티가 리셋된다.
- 씬 요소 연동(Transform 위치, 거리 등)은 `VFXPropertyBinder` + Property Binder로 자동 바인딩한다. 코드에서 자주 갱신하는 값은 SetXxx(ID 캐싱)로 둔다.
- SkinnedMesh/씬 데이터 샘플링은 Position Map, Point Cache(.pcache), GraphicsBuffer 바인딩, Skinned Mesh Sampling으로 한다. 버퍼/맵 포맷과 좌표계 불일치에 주의한다.

## 플랫폼·그래픽스 API 이식성

- 기능 지원은 런타임에 `SystemInfo`로 쿼리하고 폴백한다: `supportsComputeShaders`(GLES2/WebGL2 미지원), `supports2DArrayTextures`, `supportedRenderTargetCount`.
- 정밀도: `real`(SRP 타입, 플랫폼별 half/float 자동 치환)을 우선한다. 모바일 성능엔 half/real, 위치·큰 좌표·누적은 float(정밀도). 데스크톱은 half도 float로 처리하므로 half 정밀도 버그가 안 드러난다. 실기기 검증이 필수다.
- 셰이더 모델은 `#pragma target 3.5/4.5/5.0`으로 지정한다. 높으면 구형이 미지원이다. 플랫폼 분기는 `#if SHADER_API_METAL`, `#if defined(SHADER_API_MOBILE)` 등을 쓴다.
- WebGPU(6.2 experimental, 6.3 enable 문서)는 아직 experimental이고 제약이 있다: compute에서 `RWStructuredBuffer`는 되고 `RWBuffer`는 안 된다. read-write storage texture 포맷이 제한적이다. barrier 함수는 non-uniform 블록에서만 호출해야 하며 아니면 컴파일 실패한다.

## RenderGraph 커스텀 렌더 패스

URP 17(Unity 6.0)부터 RenderGraph가 기본이고 커스텀 패스 작성법이 바뀌었다. `RecordRenderGraph` API, obsolete 심볼(`Execute`/`OnCameraSetup`/`RenderTargetHandle`/`cmd.Blit`), Blitter/RTHandle 전환, Compatibility Mode 폐지는 rendering-urp.md.

셰이더 작성자가 유의할 점: **Shader Graph로 만든 셰이더는 Blitter API와 비호환**이라 풀스크린 패스에서 별도 처리가 필요하다. 커스텀 fullscreen blit 셰이더는 손 HLSL이나 Fullscreen Shader Graph 타깃으로 작성한다.

## 컴퓨트 셰이더

- `Dispatch(kernel, groupsX, groupsY, groupsZ)`는 스레드 수가 아니라 그룹 수를 받는다. 총 호출 = 그룹 수 × `[numthreads]` 크기.
- 그룹 수는 올림으로 계산한다. 안 그러면 꼬리 원소가 누락된다.

```csharp
int groups = (count + threadGroupSize - 1) / threadGroupSize; // 올림
cs.Dispatch(kernel, groups, 1, 1);
```

- 올림으로 총 스레드가 데이터 크기를 넘으므로 셰이더 안에서 out-of-range를 가드한다: `if (id.x >= count) return;`.
- `numthreads` 크기는 워프/웨이브 배수로 둔다(NVIDIA 32, AMD 64). 64가 무난하다.
- GPU→CPU 회수는 `AsyncGPUReadback.Request`로 한다. Dispatch 직후 동기 `GetData()`는 GPU 완료 전이면 완전 스톨되고, 너무 일찍 읽으면 이전 데이터가 나온다. 동기 GetData는 디버그/1회성만.
- `SystemInfo.supportsComputeShaders`를 확인하고 dispatch한다. 미지원(GLES2/WebGL2)은 CPU 폴백하거나 기능을 끈다.

## 디버깅·검증

- Frame Debugger: 렌더 이벤트를 드로우콜별로 멈춰 상태·셰이더·키워드를 본다. 배칭 파괴와 셰이더 fallback 진단에 쓴다.
- 셰이더 인스펙터의 "Compile and show code": 생성 HLSL, 플랫폼별 코드, variant, SRP Batcher 호환 여부를 확인한다.
- RenderDoc: 에디터 통합 캡처. 픽셀 history/debug로 픽셀 셰이더를 디버깅한다. DX11에서 이름·소스를 보려면 셰이더에 `#pragma enable_d3d11_debug_symbols`를 넣는다(기본은 스트립).
- GPU 병목은 Unity Profiler(GPU 모듈)로 잡는다. 유형: 오버드로우(반투명 fill-rate), 대역폭(텍스처 read), occupancy(레지스터/스레드).
- 코드-그래프 프로퍼티 어긋남: C# Reference 이름과 셰이더/그래프 Reference가 다르면 조용히 실패한다. Reference 이름을 `static readonly int`로 중앙 관리하고, `material.HasProperty(id)`/VFX `HasFloat`로 존재를 확인하고, rename은 양쪽을 함께 바꾼다.

(미확인: ShaderStrippingReport의 정확한 설정 UI 경로. Switch 노드 내부의 variant 생성 방식. VFX Graph 세부 기능 지원표는 버전별로 다르다.)
