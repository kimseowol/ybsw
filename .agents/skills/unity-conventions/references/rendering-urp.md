# URP 설정·성능 레퍼런스 (Unity 6.x)

기준 버전: Unity 6000.x (6.5까지 반영) · 최종 확인: 2026-07

목차
- URP Asset / Renderer Data
- Render Graph 필수화, Compatibility Mode 폐지
- 커스텀 ScriptableRenderPass (Render Graph API)
- 카메라
- 라이팅
- 그림자
- 포스트프로세싱 / Volume
- 텍스처 임포트
- 배칭 (SRP Batcher, GPU Resident Drawer)
- 셰이더 작성 / 변형 관리
- 업스케일링 / 안티에일리어싱
- 품질 스케일링
- 프로파일링 / 디버깅
- 오버드로우 / 투명·파티클 / 부가 Renderer Feature

Unity 6.0(URP 17)~6.5 기준으로 URP Asset·렌더러·커스텀 렌더 패스·라이팅·포스트프로세싱·배칭·셰이더 변형을 다룬다. URP 관련 코드를 작성하거나 렌더 설정을 만지거나 성능을 점검할 때 참조한다. 구버전 튜토리얼의 폐기된 API를 걸러내는 판별 기준도 포함한다.

버전이 붙은 규칙은 해당 버전 이상에서만 유효하다. 프로덕션 미검증 기능은 그렇게 표시한다.

## URP Asset / Renderer Data

Depth Texture, Opaque Texture는 필요 없으면 둘 다 끈다. 각각 추가 저장 패스와 텍스처를 만들어 모바일 대역폭을 먹는다. Depth Texture는 셰이더가 scene depth를 샘플하거나 soft particles를 쓸 때만, Opaque Texture는 굴절·왜곡·스크린 스페이스 효과가 화면을 다시 읽을 때만 켠다. 체크박스가 켜져 있는데 이를 샘플하는 셰이더나 Renderer Feature가 없으면 낭비다. Frame Debugger에서 CopyDepth·CopyColor 패스가 실제로 소비되는지 확인한다.

렌더링 패스 선택 기준:
- Forward: URP 기본값. 실시간 라이트가 오브젝트당 6개 이하일 때 유리하다. 라이트 수만큼 패스가 늘어난다.
- Forward+: 라이트를 오브젝트별이 아니라 공간 cluster별로 컬링해 라이트 개수 제한을 푼다. 실시간 라이트 6개 초과부터 이득이고, 그 이하에선 클러스터링 오버헤드로 Forward보다 비싸다. GPU Resident Drawer, GPU Occlusion Culling, Rendering Layers의 전제 조건이다.
- Deferred / Deferred+: G-buffer로 라이팅을 뒤로 미룬다. 비그림자 라이트를 많이 추가해도 비용이 덜 는다. 모바일에선 추가 패스 때문에 대개 Forward보다 느리다. MSAA와 비호환이다.

HDR Precision은 모바일에서 R11G11B10(32비트)를 유지한다. FP16(64비트)은 대역폭이 2배다. Android HDR Mode 기본값이 R11G11B10이다. 색 해상도가 정말 필요할 때만 FP16으로 올린다. (미확인: 과거 포럼에서 FP16 선택이 Windows/Android 빌드에서 R11G11B10으로 자동 변환된다는 보고가 있으나 6.x에서 여전한지 확인되지 않음.)

품질 티어는 Quality 레벨마다 URP Asset을 하나씩 만들어 Project Settings > Quality의 각 레벨 Rendering > Render Pipeline Asset에 할당한다. 런타임 전환은 `QualitySettings.SetQualityLevel()`이 URP Asset도 함께 교체한다. 코드 제어는 `GraphicsSettings.defaultRenderPipeline` 또는 `QualitySettings.renderPipeline`을 쓴다. 어느 Quality 레벨에도, GraphicsSettings default에도 연결되지 않은 URP Asset·Renderer Data는 정리한다. Renderer Data에 실제 씬에서 안 쓰는 Renderer Feature가 쌓이면 셰이더 변형과 패스가 낭비된다.

## Render Graph 필수화, Compatibility Mode 폐지

Render Graph는 Unity 6.0부터 URP 기본이다. Compatibility Mode(Render Graph Disabled)는 6.0~6.2에서 deprecated 경고와 함께 남아있고, 6.3부터 코드가 기본 stripped 되어 `RenderGraphSettings.enableRenderCompatibilityMode`가 항상 false를 반환한다. 6.3+에서는 `URP_COMPATIBILITY_MODE` 컴파일 정의로만 재활성할 수 있고, 출시용으로는 지원되지 않는다. Compatibility Mode가 꼭 필요하면 6.2 이하에 머물러야 한다. 신규 그래픽스 기능(GPU Occlusion Culling 등)은 Compatibility Mode 비활성을 전제한다.

구버전 튜토리얼의 폐기 API 판별 기준. 아래 심볼이 코드에 있으면 Compatibility Mode 시절 튜토리얼이고 교정 대상이다.

Don't (폐기):
```csharp
public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { ... }
public override void OnCameraSetup(...) { ... }
public override void Configure(...) { ... }
cmd.Blit(source, dest);
cmd.SetRenderTarget(target);
RenderTargetHandle handle;   // RenderTargetIdentifier 도 마찬가지
```

Do (Render Graph):
```csharp
public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) { ... }
Blitter.BlitCameraTexture(cmd, source, dest);
TextureHandle handle;   // RTHandle
```

`cmd.Blit` / `Graphics.Blit`은 렌더타깃·뷰포트·XR 상태를 임의로 건드려 Render Graph의 리소스 추적과 어긋나 깨진다. `Blitter.BlitTexture` / `Blitter.BlitCameraTexture`를 쓴다.

## 커스텀 ScriptableRenderPass (Render Graph API)

`ScriptableRenderPass`를 상속하고 `RecordRenderGraph`에서 입출력만 선언한다. 중첩 `PassData` 클래스에는 패스가 실제 쓰는 리소스만 넣는다. 불필요한 필드는 성능을 떨어뜨린다. 패스 종류는 대부분 `renderGraph.AddRasterRenderPass<PassData>()`이고, 컴퓨트 셰이더는 `AddComputePass`, RasterRenderPass가 감당 못 하는 저수준 제어는 `AddUnsafePass`다.

`builder.UseTexture(handle)`로 입력을, `builder.SetRenderAttachment(handle, index)`로 컬러 출력을, `builder.SetRenderAttachmentDepth(handle)`로 depth 출력을 선언한다. 실제 렌더 커맨드는 `builder.SetRenderFunc(...)` 안에 넣되, 메모리 할당을 피하려고 static 메서드나 람다로 작성한다. Renderer Feature의 `AddRenderPasses()`에서 `renderer.EnqueuePass(pass)`로 주입한다.

카메라 타깃은 매 프레임 frameData에서 새로 가져온다. Render Graph가 매 프레임 리소스를 pool에서 새로 할당·재활용하므로 핸들을 필드에 캐싱하면 무효화된다.

Don't:
```csharp
// 프레임 간 캐싱 → 다음 프레임에 무효
private TextureHandle _cachedColor;
```
Do:
```csharp
var resourceData = frameData.Get<UniversalResourceData>();
TextureHandle color = resourceData.activeColorTexture;   // activeDepthTexture, cameraColor 등
var cameraData = frameData.Get<UniversalCameraData>();    // 해상도, HDR 등
```

글로벌 상태는 지양한다. Render Graph는 선언된 입출력으로 의존성을 관리하므로 글로벌은 pass culling과 충돌한다.

`RenderPassEvent`로 주입 시점(BeforeRenderingOpaques, AfterRenderingOpaques, BeforeRenderingTransparents, AfterRenderingPostProcessing 등)을 고른다. 시점을 잘못 고르면 불필요하게 이르거나 늦은 지점에서 중간 텍스처의 store/load가 생겨 모바일에서 특히 비싸다.

단순 풀스크린 이미지 효과(색보정, 왜곡, 스크린 틴트)는 코드 없이 Full Screen Pass Renderer Feature와 Shader Graph의 Fullscreen 셰이더로 처리한다. 특정 레이어·머티리얼 오브젝트만 별도 렌더하거나(아웃라인, 하이라이트) 조건부 지오메트리 렌더 같은 임의 로직은 커스텀 ScriptableRenderPass로 짠다.

## 카메라

카메라 스태킹은 Overlay 카메라마다 별도 컬링·렌더가 붙는다. 같은 Overlay가 여러 스택에 있거나 한 스택에 중복되면 결과를 재사용하지 않고 매번 전체를 반복한다. 단순 UI 오버레이나 무기 뷰가 아니면 Renderer Feature나 단일 카메라로 풀리는지 먼저 본다.

`Camera.main`과 `GetComponent<T>()`를 매 프레임 호출하지 않고 Awake/Start에 캐싱하는 규칙은 performance.md.

Render Scale을 1.0 미만으로 낮추면 내부 해상도를 줄여 GPU fill을 절감한다. UI·픽셀아트에는 블러 부작용이 있다. 컬링 마스크로 카메라가 렌더할 레이어를 제한하고, 포스트를 안 쓰는 카메라는 Post-processing을 꺼서 풀스크린 패스를 제거한다.

Unity 6에서 MSAA가 켜지면 Depth Priming이 강제 비활성화된다. 구버전에서 업그레이드하며 MSAA와 Depth Priming을 함께 켜면 셰이더 아티팩트가 보고됐다. Depth Priming의 이득은 overdraw의 fragment 셰이딩 비용 제거이지 copy-depth 패스 제거가 아니다.

## 라이팅

Adaptive Probe Volumes(APV)는 Unity 6.0(URP 17)에서 production-ready다. 레거시 Light Probe Group과 달리 프로브 그리드를 자동 생성하고 지오메트리 밀도에 적응하며 per-pixel 라이팅으로 동적 오브젝트 품질을 올린다. 설정: (1) URP Asset > Lighting > Light Probe System을 Adaptive Probe Volumes로, (2) GameObject > Light > Adaptive Probe Volume(Mode: Global) 배치, (3) GI 구성. 대형 씬은 Disk Streaming(6.0 추가)으로 카메라 frustum 내 cell만 async 로드해 메모리를 아낀다. Sky Occlusion, Lighting Scenarios(시간대·조명 전환)도 지원한다.

라이팅 방식 선택: 완전 정적 씬은 완전 베이크가 최고 성능이고 동적 오브젝트는 프로브로만 받는다. 직접광은 실시간, 간접광은 베이크하려면 Mixed를 쓰고, 근거리 그림자만 실시간이 필요하면 Distance Shadowmask를 쓴다. 실시간 라이트는 완전 동적이지만 비싸고 Forward에선 픽셀 라이트 개수 제한을 받는다.

동적 오브젝트 GI에서 놓치기 쉬운 것: GPU Resident Drawer는 Mesh Renderer의 Light Probes가 Use Proxy Volume이면 비호환이고 realtime GI도 비호환이다(static GI만 호환). 특정 라이트가 특정 오브젝트만 비추게 하려면 Rendering Layers를 쓰며 Forward+ 또는 Deferred+가 필요하다.

Lightmap은 해상도(texels/unit)를 과대하게 잡으면 메모리·빌드 크기·베이크 시간이 폭증한다. 압축 미설정, UV 겹침, 불필요하게 높은 해상도가 흔한 실수다. (구체 수치 권장값은 정량 근거가 약하므로 프로파일로 정한다.)

실시간 GI와 Screen Space Reflections는 Unity 2026 로드맵(6.5~)에서 URP에 추가 예정이며 dynamic diffuse GI + SSR가 preview 단계다. 품질·안정성이 검증되지 않아 프로덕션 투입은 아직 이르다.

## 그림자

Shadow Max Distance를 카메라가 실제로 보는 범위로 타이트하게 줄이면 그림자 패스가 처리할 오브젝트가 줄어 GPU 시간이 준다. Cascade Count는 방향광이 cascade당 shadow map을 한 장씩 렌더하므로 뷰 범위가 좁으면 1~2개로 줄인다. Soft Shadows는 타일 기반 모바일 GPU에서 성능 영향이 크므로 끄거나 Quality를 낮추고 반드시 프로파일한다.

Depth Bias가 너무 작으면 shadow acne(줄무늬), 너무 크면 peter-panning(그림자가 떠 보임)이 생긴다. Normal Bias와 함께 조정하고 한쪽만 극단으로 두지 않는다. 그림자에 기여하지 않는 작거나 먼 오브젝트는 Mesh Renderer의 Cast Shadows를 Off로 해 shadow 패스를 가볍게 한다.

## 포스트프로세싱 / Volume

On-tile Post-processing은 Unity 6.5+(일반 플랫폼), untethered XR은 6.3+에서 쓴다. GPU 타일 메모리에서 단일 패스로 처리해 시스템 메모리 round-trip을 없애 tile-based GPU(Vulkan/Metal) 대역폭·배터리·발열을 줄인다. on-tile로 처리되는 효과는 공식 6.5 매뉴얼 기준 Color grading, Vignette, Tonemapping, Dithering, Film Grain이다(HDR은 렌더 타깃 포맷 이슈이지 이 목록에 없다). 활성 조건: (1) URP, (2) Renderer의 통합 Post-processing 비활성화, (3) Tile-Only Mode 활성화, (4) Renderer Features에서 On Tile Post Processing 활성화. on-tile이 비활성이면 texture-sampling fallback으로 동작해 결과는 같지만 대역폭 이득이 사라진다. Render Graph Viewer로 진짜 on-tile인지 fallback인지 확인한다.

Volume은 URP가 활성 컴포넌트를 모두 순회하며 카메라 위치로 기여도를 계산하고 보간하므로 개수가 늘수록 매 프레임 비용이 오른다. default volume만 쓰면 URP는 씬 로드나 품질 변경 시에만 평가한다. Volume과 Override 수를 최소화한다. 커스텀 VolumeComponent에서 값을 바꿀 때는 `VolumeParameter.overrideState = true`를 설정해야 프레임워크가 리소스를 덜 쓴다.

포스트 효과 GPU 비용은 이웃 픽셀을 샘플하는 blur류가 비싸다. SSAO는 모바일·VR에서 대체로 과하니 배제부터 검토하고, Motion Blur는 VR에서 멀미 때문에 금지, Depth of Field는 저사양이면 Gaussian, 데스크톱·콘솔이면 Bokeh를 쓴다. Bloom은 다운샘플을 쓰면 저사양 모바일도 가능하다.

포스트를 런타임에 토글할 때 흔한 실수 세 가지. Volume 컴포넌트나 GameObject를 매번 새로 만들면 할당·GC가 발생하니 미리 만들어 두고 weight/parameter만 조정한다. weight를 lerp 없이 급하게 켜고 끄면 팝핑이 생긴다. overrideState를 설정 안 하면 값이 default로 안 돌아온다.

## 텍스처 임포트

Read/Write Enabled를 켜면 픽셀 데이터를 CPU 메모리에도 복사본으로 들고 있어 메모리가 약 2배가 된다. `GetPixels`/`SetPixels`, `EncodeToPNG`처럼 CPU가 픽셀에 접근할 때만 켜고, `Graphics.CopyTexture`/`Graphics.Blit` 같은 GPU 전용 작업엔 끈다. 업로드 후 CPU 접근이 끝나면 `makeNoLongerReadable = true`로 복사본을 해제한다.

압축 포맷은 모바일이 ASTC(블록 크기로 품질·크기 조절), PC·콘솔이 BCn(BC7 고품질), 구형 Android가 ETC2다. 압축 미설정(RGBA32)으로 두면 VRAM과 빌드 크기가 압축 대비 4~8배로 뛴다.

Mipmap은 3D 오브젝트 텍스처에는 생성하고(거리별 샘플로 앨리어싱 방지) UI·2D 스프라이트에는 끈다(항상 최대 해상도라 메모리만 33% 늘고 UI가 블러될 수 있다). Mipmap Streaming은 카메라 위치에 필요한 mip 레벨만 로드해 3D 씬 텍스처 메모리를 아낀다. Generate Mipmap을 켠 상태에서 Stream Mipmap Levels를 활성화하며 Android는 Compression Method가 LZ4/LZ4HC여야 한다.

sRGB 플래그는 albedo·color 텍스처는 ON, normal·mask·roughness 같은 데이터 텍스처는 OFF(linear)로 둔다. 틀리면 색이 어긋난다. Max Size는 작은 오브젝트에 4K를 물리지 않도록 플랫폼 오버라이드로 모바일을 낮춘다. NPOT는 압축·mip 제약이 있으니 POT를 쓴다. 대량 점검은 Editor 스크립트(TextureImporter 순회)나 Memory Profiler로 큰 텍스처·RGBA32·Read/Write 켜진 것을 색출한다.

## 배칭 (SRP Batcher, GPU Resident Drawer)

SRP Batcher는 per-material CBUFFER를 GPU에 상주시켜 SetPass를 최소화한다. 셰이더가 모든 머티리얼 프로퍼티를 `CBUFFER_START(UnityPerMaterial)` 블록에 선언해야 호환된다. `MaterialPropertyBlock`·`renderer.material` 접근이 배칭을 깨는 함정과 그 대안은 shader-vfx.md.

GPU Resident Drawer(Unity 6)는 BatchRendererGroup으로 GameObject를 GPU instancing으로 그려 드로우콜과 CPU 렌더 스레드 시간을 줄인다. 활성 조건: Rendering Path가 Forward+, URP Asset에서 SRP Batcher 활성 + GPU Resident Drawer를 Instanced Drawing, Project Settings > Graphics > Shader Stripping의 BatchRendererGroup Variants를 Keep All, 컴퓨트 지원 플랫폼. 대상 GameObject는 Mesh Renderer 보유, Light Probes가 Use Proxy Volume 아님, static GI만, DOTS instancing 지원 셰이더여야 한다. BRG 셰이더 변형을 전부 컴파일하므로 빌드 시간이 는다.

배칭 우선순위: SRP Batcher가 켜져 있으면 Dynamic Batching은 대부분 무의미하다(SRP Batcher가 우선). Static Batching은 SRP Batcher와 병행 가능하다. 대량 동일 메시는 GPU Resident Drawer(Forward+)로 처리하며, 이것이 사실상 자동 instancing을 대체한다.

GPU Occlusion Culling(Unity 6)은 Compatibility Mode 비활성 + GPU Resident Drawer 활성 + Universal Renderer에서 활성화하며 Forward+와 컴퓨트 지원이 필요하다. 오브젝트를 bounding sphere로 근사하므로 얇고 긴 오브젝트는 컬링이 덜 되고, 오클루전이 적은 씬에선 셋업 오버헤드로 오히려 느려질 수 있다.

## 셰이더 작성 / 변형 관리

URP 셰이더는 LightMode 패스가 여럿 필요하다. `UniversalForward`(색), `DepthOnly`(depth prepass/priming), `ShadowCaster`(그림자), `DepthNormals`(SSAO·DepthNormals prepass), 2D는 `Universal2D`. ShadowCaster가 없으면 그림자가 안 나오고, DepthOnly/DepthNormals가 없으면 depth 텍스처와 depth 기반 효과가 깨진다.

셰이더의 `CBUFFER_START(UnityPerMaterial)` SRP Batcher 호환 요건, 키워드 타입(`shader_feature`/`multi_compile`/`dynamic_branch`)과 variant 스트리핑은 shader-vfx.md. URP Asset에서 안 쓰는 기능을 끄면 관련 변형이 자동 strip 되므로 Renderer Data에 미사용 Renderer Feature를 쌓지 않는다.

## 업스케일링 / 안티에일리어싱

STP(Spatial-Temporal Post-processing, Unity 6.0)는 여러 프레임과 모션 벡터로 저해상도 타깃에서 고해상도를 재구성한다. 낮은 Render Scale과 함께 쓰도록 설계됐고 TAA 전처리가 필요해 선택 안 하면 암묵적으로 TAA가 켜진다. FSR1보다 안정적이고 품질이 높지만 같은 내부 해상도에서 STP가 더 무겁다. 모션 벡터·jitter가 없으면 고스팅이 생긴다. 극저사양에서 최대 성능이 필요하면 FSR을 쓴다.

MSAA는 지오메트리 엣지를 하드웨어로 AA 하지만 Deferred와 비호환이고 타일 GPU에서 샘플 수만큼 대역폭을 먹는다. 포스트 기반 AA 중 SMAA는 단일 프레임 공간적 처리라 가볍고, TAA는 시간적 처리라 모션 벡터·jitter가 필요하고 고스팅 위험이 있으며 Deferred에서도 동작한다. Render Scale이나 Dynamic Resolution으로 내부 해상도를 낮추면 fill-rate를 아껴 GPU bound에 효과적이지만 UI·픽셀아트는 별도 풀해상도 렌더를 권한다.

TAA·STP·커스텀 셰이더는 MotionVectors 패스를 구현해야 한다. 모션 벡터가 없으면 고스팅·스미어가 생긴다.

## 품질 스케일링

저사양/고사양 티어는 URP Asset 단위로 Render Scale, Shadow Distance/Resolution/Cascade, Soft Shadows, HDR, Post-processing, MSAA, Texture Quality, LOD Bias, 픽셀 라이트 수를 차등한다. 프레임 영향이 큰 순서는 대략 Render Scale·해상도 > Shadow(distance/resolution) > Post-processing(SSAO/DoF) > 픽셀 라이트 수 > Texture Quality·LOD Bias이므로 이 순서로 노브를 내린다.

`QualitySettings.SetQualityLevel()`로 URP Asset을 교체하면 셰이더·리소스가 재초기화되어 한 프레임 스파이크가 날 수 있다. 전환 후 카메라·Volume·렌더러 재설정을 빠뜨리거나 로딩 중에 전환하면 팝핑이 생긴다. 모바일은 `Application.targetFrameRate`로 30/45 캡을 걸어 발열·배터리를 관리한다. vSync는 모바일에서 보통 플랫폼이 관리한다.

## 프로파일링 / 디버깅

문제 유형별 도구:
- 드로우콜·SetPass 과다, 배칭 깨짐: Frame Debugger(Window > Analysis > Frame Debugger). SRP Batch를 클릭하면 왜 이전 드로우콜과 배칭 안 됐는지 이유가 나온다.
- 오버드로우, 라이팅: Rendering Debugger.
- 프레임 타임·GPU 병목: Profiler의 Rendering 모듈.
- 텍스처 메모리·라이트맵·Read/Write 텍스처: Memory Profiler.
- Render Graph 패스·리소스·의존성, on-tile vs fallback 검증: Render Graph Viewer(Unity 6 신규, Window > Analysis).

Game view Stats 창의 Batches, SetPass Calls로 배칭 상태를 본다. SRP Batcher에선 SetPass가 머티리얼당이 아니라 셰이더 변형당 호출되므로 변형이 적으면 SetPass가 적다.

에디터 Profiler 수치는 실기와 다르다. 타일 GPU 대역폭·발열·클럭 스로틀이 반영되지 않는다. 모바일 GPU 부하와 대역폭은 Arm Performance Studio(Streamline), Xcode GPU frame capture(Metal), Snapdragon Profiler, RenderDoc 같은 벤더 툴로만 정확히 측정된다.

## 오버드로우 / 투명·파티클 / 부가 Renderer Feature

투명 오브젝트·파티클·UI는 depth write 없이 겹쳐 그려져 오버드로우로 fill-rate를 폭발시킨다. 모바일 대역폭에 가장 치명적이다. Rendering Debugger의 오버드로우 뷰로 측정하고, 파티클 수를 캡하고 알파 텍스처의 투명 영역을 트리밍하고 파티클 셰이더를 단순화해 줄인다.

부가 Renderer Feature는 켜기 전에 프로파일한다. Decal은 별도 렌더 패스로 CPU·GPU 시간을 더하고, SSAO는 DepthNormals prepass를 요구하며 성능 영향이 커 모바일은 배제부터 검토하고, Screen Space Shadows는 depth 기반 추가 패스다. Reflection Probe는 정적 씬이면 베이크하고, 저사양 모바일은 Probe Blending과 Box Projection을 끈다. Feature마다 중간 텍스처와 추가 패스가 store/load 대역폭을 누적하므로 Render Graph Viewer로 실제 패스·리소스 수를 확인하고 모바일은 총 풀스크린 패스 수를 예산으로 관리한다.
