# Unity 빌드·프로젝트 설정 레퍼런스

기준 버전: Unity 6000.x (6.5까지 반영) · 최종 확인: 2026-07

목차
- Build Profiles (6.0+)
- 어셈블리 구조 (asmdef)
- Editor 코드 분리
- 인크리멘탈 빌드 / 클린 빌드
- 빌드 스크립팅 & CI
- Enter Play Mode Options
- Scripting Backend (IL2CPP / Mono)
- 코드 스트리핑
- Player Settings (실수하기 쉬운 항목)
- Scripting Define Symbols
- Package Manager & 의존성
- Project Auditor (6.4+ 내장)
- 버전 관리
- 빌드 재현성

Unity 6.x(6000.0~6000.5)에서 빌드 구성, 어셈블리 구조, 스크립팅 백엔드, 코드 스트리핑, 프로젝트 설정, 버전 관리를 다룬다. 빌드 스크립트·asmdef·Player Settings를 만지거나, 조건부 컴파일·CI 빌드·재현성 문제를 다룰 때 참조한다. 버전 표기 기준: 6000.0=6.0, 6000.4=6.4, 6000.5=6.5.

## Build Profiles (6.0+)

Build Profiles는 6.0에서 도입돼 기존 Build Settings 창을 대체한다. 두 워크플로는 6.x에서 공존하므로 기존 스크립트는 그대로 동작한다.

- Build Profile은 에셋(`.asset`) 파일이고 기본 위치는 `Assets/Settings/Build Profiles/`다. 버전관리 대상이며 팀이 공유한다.
- 프로파일이 캡슐화하는 것: 자체 Scene List, 추가(additive) Scripting Defines, Player Settings override, 그리고 Graphics/Quality/Adaptive Performance/Diagnostics override(6.4+).
- 두 종류가 있다. 플랫폼 내장 프로파일(Android/iOS/Windows 등)은 shared 설정을 서로 공유한다. 커스텀 Build Profile은 독립적이며 전역 설정을 override 한다.
- 전역 Player/Quality/Graphics는 여전히 `ProjectSettings/*.asset`에 남는다. 프로파일별로 갈리는 설정만 프로파일 override로 관리한다.
- 프로파일을 전환할 때 defines가 다르면 재컴파일이 일어난다. Player Settings가 바뀌면 에디터 재시작이 필요할 수 있다.

빌드 자동화 API는 `UnityEditor.Build.Profile` 네임스페이스의 `BuildProfile` 클래스다.

- 로드: `AssetDatabase.LoadAssetAtPath<BuildProfile>("Assets/Settings/Build Profiles/macOS.asset")`.
- 활성 프로파일 설정/조회: `BuildProfile.SetActiveBuildProfile(profile)` / `GetActiveBuildProfile()`.
- 빌드 실행: `BuildPipeline.BuildPlayer(BuildPlayerWithProfileOptions)` 오버로드.

`SetActiveBuildProfile`은 batchmode에서 비활성 플랫폼 대상 프로파일로 전환하지 못한다. 플랫폼 전환은 스크립트 재컴파일을 요구하는데 스크립트 실행 중에는 불가능하기 때문이다. batchmode에서는 CLI 인자 `-activeBuildProfile <path>`를 쓴다.

구방식 `EditorUserBuildSettings.SwitchActiveBuildTarget` 직접 조작 대신, 프로파일 에셋이 씬·defines·Player Settings를 캡슐화하므로 프로파일 API로 이전한다.

미확인: 활성 Build Profile 미선택 상태로 빌드할 때 적용되는 설정은 공식 문서가 직접 설명하지 않는다. 클래식 플랫폼 프로파일의 shared 설정 + 전역 Scene List가 baseline으로 보이나 확정 근거는 없다.

## 어셈블리 구조 (asmdef)

asmdef가 없으면 모든 스크립트가 단일 `Assembly-CSharp`으로 컴파일되고, 한 파일만 바꿔도 전체가 재컴파일된다. 코드를 어셈블리로 나누고 의존성을 명시하면 수정된 어셈블리와 그에 의존하는 어셈블리만 재빌드한다.

- 도메인별로 분리하고 의존 방향을 단방향으로 만든다. 상위(게임플레이)가 하위(코어/유틸)를 참조하고 역방향은 금지한다.
- A가 B를, B가 A를 참조하는 순환(A⇄B)은 컴파일되지 않는다. 끊는 표준 패턴: 공통 부분을 제3 어셈블리로 추출해 양쪽이 참조하거나, 인터페이스/이벤트를 하위 어셈블리에 두고 상위가 구현한다(의존성 역전). 콜백 대신 C# `event`로 역참조를 제거한다.

asmdef 필드 권장값:

- `rootNamespace`: 지정하면 새 스크립트 생성 시 네임스페이스가 자동으로 붙는다.
- `autoReferenced`: 라이브러리성 코드는 `false`로 둔다. `Assembly-CSharp`이 암묵적으로 참조하는 것을 막는다.
- `noEngineReferences`: 순수 C# 로직 어셈블리는 켜서 UnityEngine/UnityEditor 참조를 끊는다.
- `defineConstraints`: 심볼이 참일 때만 어셈블리를 컴파일한다.

폴더 구조와 어셈블리 경계를 분리하려면 asmref(Assembly Definition Reference)로 다른 폴더의 스크립트를 기존 asmdef에 포함시킨다.

Version Defines는 특정 Unity/패키지 버전이 설치됐을 때만 심볼을 정의한다. 세 필드는 `If resource`(Unity 또는 패키지/모듈명), `version is`(버전 표현식), `set define`(정의할 심볼)이다. 패키지 유무에 따라 API가 갈릴 때 조건부 컴파일에 쓴다.

## Editor 코드 분리

런타임 어셈블리가 `UnityEditor` 타입을 직접 참조하면 non-Editor 플랫폼 빌드에서 컴파일이 실패한다. 그 어셈블리에는 UnityEditor가 없기 때문이다.

런타임 코드는 Editor 코드를 참조하지 못한다. 반대로 Editor 어셈블리는 런타임 어셈블리를 참조할 수 있다. 분리 방법 세 가지:

1. Editor 전용 asmdef: Platform 속성을 Editor로 제한한다. 빌드에서 자동 제외되고 가장 견고하다. 커스텀 인스펙터·에디터 윈도우·툴은 여기 둔다.
2. `Editor` 폴더: 이름이 `Editor`인 폴더의 스크립트는 Editor 전용으로 컴파일된다. asmdef 없는 프로젝트의 방식이다.
3. `#if UNITY_EDITOR` 가드: 런타임 로직 안의 소량의 에디터 편의 코드(Gizmos, 검증)에만 쓴다.

런타임 어셈블리를 별도 asmdef로 분리하면 UnityEditor를 참조하는 순간 에디터에서 즉시 컴파일 에러가 나므로 빌드 전에 잡힌다. `Assembly-CSharp` 단일 구조에서는 `#if UNITY_EDITOR` 밖에서의 참조가 빌드할 때만 터진다.

## 인크리멘탈 빌드 / 클린 빌드

인크리멘탈 Player 빌드는 바뀐 부분만 재빌드하고 아티팩트를 `Library/Bee`에 저장한다. Shader Cache와 AssetDatabase도 Library에 있어 후속 빌드를 가속한다. 씬/에셋 변경이 없으면 데이터 빌드를 생략하고 이전 데이터를 재사용한다.

머신 레벨 캐시(비-임베디드 패키지, libIL2CPP 아티팩트)는 프로젝트 간 재사용되며 위치는 환경변수 `BEE_CACHE_DIRECTORY`로 정한다. 콘텐츠 빌드(에셋)는 프로젝트별이라 공유되지 않는다.

Clean Build 옵션(Build Player 창 Build 버튼 팝업 메뉴)은 캐시 아티팩트를 삭제하고 전체 재빌드한다. 패키지 업그레이드 후 손상 캐시, 결정성 검증, 인크리멘탈 캐시 꼬임에 쓴다.

- 하지 않는다: 문제가 생길 때마다 `Library`를 통째로 삭제. 전체 에셋 재임포트 + 셰이더 재컴파일로 빌드 시간이 폭증한다.
- 한다: Clean Build 옵션으로 빌드 아티팩트만 국소 정리. `Library` 전체 삭제는 최후 수단이다. CI는 `Library`를 워크스페이스에 캐시해 재사용한다.

에셋 임포트 결과를 팀·CI가 공유하려면 Unity Accelerator로 캐시한다. Library 캐시 공유는 LFS가 아니라 Accelerator로 한다.

## 빌드 스크립팅 & CI

빌드 구동 API는 `BuildPipeline.BuildPlayer(BuildPlayerOptions)` 또는 프로파일 기반 오버로드다. `BuildPlayerOptions`에서 자주 누락되는 것: `scenes`(빌드할 씬 배열), `target`, `locationPathName`, `options`. `scenes`가 비면 빈 빌드나 에러가 난다.

빌드 콜백(`UnityEditor.Build` / `.Reporting`):

- `IPreprocessBuildWithReport.OnPreprocessBuild`: 빌드 시작 전. Player 빌드에만 호출되고 AssetBundle 빌드에는 호출되지 않는다.
- `IPostprocessBuildWithReport.OnPostprocessBuild`: 빌드 완료 후. 빌드 실패/취소 시에는 호출되지 않는다.
- 실행 순서는 `IOrderedCallback.callbackOrder`(int, 낮을수록 먼저)로 정한다. 순서에 의존하면 명시한다.

CLI 빌드 인자: `-batchmode -nographics -quit -executeMethod <Class.Method> -buildTarget <t> -logFile <path>`. `-nographics`는 GPU 없는 CI에서 안전하다.

`BuildPipeline.BuildPlayer`는 실패해도 프로세스 exit code를 자동으로 1로 만들지 않는다. `-quit`에 의존하지 말고 결과를 직접 판정해 `EditorApplication.Exit(code)`를 호출한다.

```csharp
// Don't: -quit이 성공/실패와 무관하게 0을 반환할 수 있음
BuildPipeline.BuildPlayer(opts);

// Do: BuildReport로 판정 후 명시적 종료
var report = BuildPipeline.BuildPlayer(opts);
if (report.summary.result != BuildResult.Succeeded)
    EditorApplication.Exit(1);
else
    EditorApplication.Exit(0);
```

`BuildResult` enum은 `Succeeded/Failed/Cancelled/Unknown`이다. `report.summary`(totalSize, totalTime, 결과, 출력 경로), `report.steps`(단계별 시간), `report.packedAssets`(포함 에셋·크기)는 빌드 크기 회귀 검사에 쓴다.

## Enter Play Mode Options

Edit > Project Settings > Editor > Enter Play Mode Options를 켜면 Reload Domain / Reload Scene을 개별 토글할 수 있다. 프로젝트에 따라 플레이모드 진입 시간을 크게 줄인다. 설정은 `ProjectSettings/EditorSettings.asset`에 직렬화되므로 커밋으로 팀에 공유한다.

Domain Reload를 끄면 static 필드 값과 static 이벤트 핸들러가 플레이 세션 간에 유지돼 "두 번째 플레이부터 버그"(이벤트 중복 구독, 카운터 누적, 싱글톤이 null이 아님)가 난다. 코드 레벨 방어 패턴(`RuntimeInitializeOnLoadMethod` / `SubsystemRegistration` 명시 리셋)은 runtime-architecture.md.

빌드·설정 관점: `[AutoStaticsCleanup]` / `[NoAutoStaticsCleanup]` 어트리뷰트로 static 필드 단위 자동 리셋 여부를 지정한다(6.x).

Scene Reload를 끄면 씬 상태가 이전 플레이 세션에서 남을 수 있다. 씬을 절차적으로 생성/파괴하거나 Awake/Start에서 초기화를 가정하는 코드는 재확인한다. 수정된 씬은 백업 후 종료 시 복원된다.

미확인: 6.x에서 이 옵션의 기본값 변화. 기본 비활성(Domain/Scene Reload 켜짐 = 안전하지만 느림) 유지로 보이나 확정 근거는 없다.

## Scripting Backend (IL2CPP / Mono)

- Mono: JIT, 빌드가 빠르고 관리 라이브러리 지원이 넓다. 개발 이터레이션에 유리하다.
- IL2CPP: IL을 C++로 AOT 컴파일한다. 빌드가 느리고 런타임 성능이 좋다. iOS/콘솔/WebGL은 IL2CPP가 필수다. 데스크톱은 개발 중 Mono, 릴리스 IL2CPP가 일반적이다.

IL2CPP AOT 제약(6.0 문서):

- `System.Reflection.Emit` 미지원.
- 런타임 생성 제네릭(`Activator.CreateInstance<T>()`)은 느린 shared code로 폴백한다. value type 제네릭은 타입별 코드가 필요해서, 컴파일 시 인스턴스를 파악하지 못하면 런타임에 실패한다.
- 리플렉션/직렬화로만 참조되는 타입은 스트리핑돼 사라질 수 있다.
- `System.Threading`은 Web 플랫폼에서 미지원이다.
- `dynamic` 키워드, `System.Diagnostics.Process`, `Marshal.Prelink/PrelinkAll` 미지원. Exception filter 실행 순서가 Mono와 다르다.

AOT 회피책:

- 제네릭 제약 `where T: class` / `where T: struct`를 추가한다.
- `UsedOnlyForAOTCodeGeneration` 더미 메서드로 필요한 제네릭 특수화를 참조만 해두면 IL2CPP가 코드를 생성한다.
- 네이티브 콜백 static 메서드에는 `[MonoPInvokeCallback]`을 붙인다.
- 리플렉션/JSON 역직렬화 대상 타입은 `[Preserve]` + link.xml로 보존하고 코드에서 한 번은 명시적으로 참조한다.

C++ Compiler Configuration은 Debug/Release/Master다. Master가 최적화 최고이고 빌드가 가장 오래 걸린다. IL2CPP Code Generation은 "Faster runtime"(제네릭 특수화 전부 생성, 빌드 크고 빠름)과 "Faster (smaller) builds"(공유 제네릭 버전만, 빌드 작고 런타임 느림) 중 고른다.

## 코드 스트리핑

Managed Stripping Level은 Disabled / Low / Medium / High다. High로 갈수록 제거가 공격적이고 리플렉션 코드가 오작동하거나 크래시할 위험이 커진다. 문자열 기반 타입 로딩(`Type.GetType("...")`, `AddComponent("...")`), JSON 역직렬화 대상 타입이 High에서 제거돼 런타임 null/예외가 난다.

리플렉션으로만 참조되는 타입은 스트리퍼가 감지하지 못하므로 `[Preserve]`(UnityEngine.Scripting) 또는 link.xml로 보존을 선언한다. `[Preserve]`는 타입과 기본 생성자를 보존한다. link.xml이 더 세밀해서 특정 필드/메서드만 보존할 수 있다.

link.xml은 Assets 폴더 어디든 둔다. 패키지가 자체 link.xml을 제공하면 Unity가 자동 병합한다.

```xml
<linker>
  <assembly fullname="MyAssembly">
    <type fullname="MyNamespace.MyType" preserve="all"/>
  </assembly>
</linker>
```

`preserve` 값은 `all` / `fields` / `methods` / `nothing`이다. `<assembly ... preserve="all"/>`로 어셈블리 전체를 보존한다. 스트립 위험 코드는 Project Auditor의 코드 뷰나 빌드 로그의 링커 출력으로 진단한다.

미확인: 6.x의 플랫폼별 정확한 기본 스트리핑 레벨. IL2CPP 빌드는 통상 스트리핑이 기본 활성이고 Mono는 Disabled에 가까우나 정확한 디폴트는 플랫폼에 의존하며 문서상 명시 근거가 부족하다.

## Player Settings (실수하기 쉬운 항목)

릴리스 직결:

- Bundle Identifier(`applicationIdentifier`): iOS/Android 패키지명. 스토어 게시 후 변경하면 업데이트가 불가능하고 신규 앱으로 취급된다.
- Version(`bundleVersion`) + Build Number(iOS `buildNumber` / Android `bundleVersionCode`): 스토어는 증가를 요구한다. 안 올리면 제출이 거부된다.

렌더링/런타임:

- Color Space: Linear가 물리기반 렌더링에 정확하다. 프로젝트 중간에 바꾸면 전 머티리얼의 룩이 변하므로 초기에 결정한다. 일부 구형 모바일 GPU는 Linear에 제약이 있다.
- Graphics API: Auto면 Unity가 플랫폼 기본을 고른다. 특정 기기에서 원치 않는 API가 선택될 수 있으므로 명시 지정을 권장한다.
- Api Compatibility Level: 신규 프로젝트는 `.NET Standard 2.1`(표면 작고 빌드 작음), 레거시 라이브러리가 필요하면 `.NET Framework`.

모바일 함정:

- Android는 ARM64가 필수다(Google Play). ARMv7만 두면 제출이 거부된다.
- Multithreaded Rendering / Graphics Jobs는 일부 기기 드라이버에서 크래시하므로 문제 시 끈다.

Player Settings는 `ProjectSettings/ProjectSettings.asset`(YAML)에 직렬화된다. 한 파일에 설정이 많아 병합 충돌이 잦으므로 CI 자동 변경(버전 넘버 등)은 diff를 최소화한다. 코드 변경은 `PlayerSettings.*` API(예: `SetScriptingDefineSymbols`, `colorSpace`, `SetApplicationIdentifier`, `bundleVersion`)로 한다. 플랫폼별로 갈리는 항목(Managed Stripping Level, Define Symbols, Active Input Handling)은 Build Profile의 Player Settings override에서 관리하고 전역 설정은 baseline만 둔다.

## Scripting Define Symbols

Unity는 여러 곳의 심볼을 덮어쓰지 않고 합산한다. 현재 빌드 구성에 해당하는 모든 심볼을 더한다.

- `csc.rsp`(프로젝트 전역): 시작 시 읽어 모든 코드 컴파일 전에 적용된다. batchmode에서 시작부터 필요한 심볼은 여기 둔다. `.rsp` 변경은 스크립트 리임포트로 재컴파일하기 전까지 반영되지 않는다.
- Player Settings의 Scripting Define Symbols(플랫폼별): 전역 `#define` 수정에 쓴다.
- Build Profile의 Scripting Defines(additive): active build profile과 매치될 때만 포함된다.
- asmdef Version Defines / defineConstraints: 어셈블리 단위.

내장 심볼: `UNITY_EDITOR`, `UNITY_STANDALONE`, `UNITY_ANDROID`, `UNITY_IOS`, `DEVELOPMENT_BUILD`, `UNITY_6000_0_OR_NEWER` 등.

"다른 플랫폼 빌드에서 갑자기 깨짐"을 막는 규율:

- `#if PLATFORM_X` 안에서만 존재하는 심볼/타입을 밖에서 참조하지 않는다.
- 조건부 블록은 완결적으로 쓴다. else 브랜치로 모든 플랫폼에 필요한 멤버를 유지한다.
- `#if`를 남발하면 미컴파일 데드코드가 리팩토링·리네임 때 조용히 깨진다. 조건부는 플랫폼 API 경계에만 쓰고, 로직은 인터페이스/부분 클래스로 분리해 항상 컴파일되게 한다.
- 에디터 컴파일 통과가 타겟 빌드 통과를 뜻하지 않는다. 에디터에서만 컴파일되는 경로는 CI에서 실제 타겟 빌드로 주기 검증한다.

## Package Manager & 의존성

`manifest.json`·`packages-lock.json` 역할, lock 커밋, Git URL/scoped registry/embedded 방식, 성숙도별 버전 선택은 packages.md. 빌드·CI 관점만 여기 둔다.

- 재현성: registry는 정확한 버전, Git URL은 hash를 고정한다. Git 브랜치 참조와 로컬 `file:` 경로는 비결정적이라 CI에서 취약하다.
- 코드에서 특정 패키지 API를 쓰면 manifest.json에 그 의존성을 추가하고 버전을 고정한다. 안 하면 로컬 캐시에는 있어도 클린 체크아웃·CI에서 복원 실패로 컴파일이 깨진다.
- 일부 기능은 버전에 따라 내장/패키지가 갈린다(예: Project Auditor는 6.4+ 에디터 내장, 그 미만은 `com.unity.project-auditor` 패키지). Version Defines로 조건부 대응한다.

## Project Auditor (6.4+ 내장)

6.4+에서 에디터 내장 모듈이다(Window > Analysis > Project Auditor). 그 미만은 `com.unity.project-auditor` 패키지를 설치한다. 분석 영역: Code(메인 player 어셈블리 정적 분석: 박싱, 빈 MonoBehaviour 메서드, 비효율 API, 스트립 위험), Assets(임포트 설정), Project Settings, Build Report.

코드로 사전 예방할 수 있는 것: 빈 `Update`/메서드 제거, 박싱 회피(제네릭·struct), 올바른 릴리스 Player Settings, 스트립 위험 코드에 `[Preserve]`/link.xml. 서드파티 패키지 내부 경고나 실측과 무관한 마이크로 최적화는 무시하고, 잘못된 릴리스 Player Settings와 hot-path 박싱은 수정한다.

미확인: 6.4 내장판의 공식 CI 배치 분석/게이트 API 명세. 패키지 시절에는 스크립팅 API로 배치 분석이 가능했으나 내장판 API 세부는 확인이 필요하다.

## 버전 관리

`.gitignore`:

- 무시: `Library/`, `Temp/`, `obj/`, `Build/`, `Builds/`, `Logs/`, `UserSettings/`, `.vs/`, `*.csproj`, `*.sln`(모두 생성물).
- 커밋: `Assets/`(모든 `.meta` 포함), `ProjectSettings/`, `Packages/`(manifest.json + packages-lock.json).

`.meta`와 직렬화:

- Editor Settings > Version Control Mode = Visible Meta Files. 에셋마다 `.meta`(GUID + 임포트 설정)가 생성되고 참조 무결성의 핵심이다.
- Asset Serialization = Force Text(YAML). 씬/프리팹이 텍스트로 직렬화돼 diff·merge가 가능하다.
- `.meta`가 누락되면 참조가 깨지고 GUID가 바뀐다. 에셋과 `.meta`는 항상 함께 이동/커밋한다.

병합:

- 에디터에 동봉된 UnityYAMLMerge를 git mergetool로 지정해 `.meta`/`.unity`/`.prefab` 충돌을 해결한다.
- 그래도 씬/프리팹은 작게 분할하고(prefab 분리, additive scene), 편집 전에 파일을 잠가 동시 편집을 피한다.

AI 경계: 에셋/`.meta`는 명시 요청 없이 수정하지 않는다. GUID·바이너리 구조를 훼손하고 병합 불가 충돌을 만든다. 텍스트 코드·설정(`.cs`, `.asmdef`, manifest.json, 텍스트 ProjectSettings)만 안전하다.

Git LFS: 자주 바뀌는 대용량 바이너리(psd, 오디오, 비디오, fbx)를 `git lfs track`으로 `.gitattributes`에 등록해 리포 비대화를 막는다. 코드/텍스트는 LFS가 불필요하다.

## 빌드 재현성

고정할 것:

- Editor 버전: `ProjectSettings/ProjectVersion.txt` 첫 줄(EditorVersion + changeset). 다른 버전으로 열면 임포터·직렬화 포맷 차이로 손상될 수 있다. CI는 이 파일에 맞는 에디터 이미지를 쓴다.
- 패키지 락: packages-lock.json 커밋.
- ProjectSettings 전체와 커밋된 Build Profile 에셋.

"내 머신에선 되는데"를 막으려면 로컬 Library 캐시·로컬 패키지 경로·에디터에서만 컴파일되는 코드에 의존하지 않는다. 주기적으로 클린 체크아웃 + 클린 빌드를 CI에서 돌리고, asmdef 분리로 UnityEditor 참조가 빌드 전에 잡히게 한다. 로컬 `file:` 패키지와 Git 브랜치 참조 대신 고정 버전을 쓴다. 클린 체크아웃 첫 빌드는 패키지 복원과 첫 임포트로 오래 걸리므로 CI 타임아웃에 여유를 둔다.
