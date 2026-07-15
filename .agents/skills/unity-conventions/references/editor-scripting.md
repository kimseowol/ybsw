# Unity 에디터 확장 컨벤션 (Unity 6 / 6000.x)

기준 버전: Unity 6000.x (6.5까지 반영) · 최종 확인: 2026-07

목차
- UI 시스템: UI Toolkit vs IMGUI
- SerializedObject/SerializedProperty 경유 (undo·멀티편집·프리팹)
- 커스텀 인스펙터
- PropertyDrawer
- 데이터 바인딩과 값 변경 콜백
- UXML / USS와 UxmlElement 소스 생성
- EditorWindow 라이프사이클과 상태
- 에디터 전용 코드 격리
- 도메인 리로드 안전성과 static 리셋
- Undo · Dirty · Save · 프리팹 정합성
- 씬 뷰 도구화 (Handles · Gizmos · EditorTool · Overlay)
- 에셋 파이프라인 훅
- 메뉴 · 단축키 · 설정 지속성
- 서드파티 인스펙터(Odin) 공존

Unity 에디터를 확장하는 코드(커스텀 인스펙터, PropertyDrawer, EditorWindow, 씬 뷰 툴, 에셋 훅, 메뉴/설정)를 쓸 때 참조한다. UI 시스템 선택, `SerializedObject` 경유 원칙, 에디터 코드 격리, 도메인 리로드 안전성, Undo/프리팹 정합성이 핵심이다. 대상은 6000.x이며 버전 의존 사실에는 버전을 표기한다.

---

## UI 시스템: UI Toolkit vs IMGUI

- 새 에디터 UI는 UI Toolkit으로 작성한다. 오버라이드는 `CreateInspectorGUI`(인스펙터) / `CreatePropertyGUI`(드로어) / `CreateGUI`(EditorWindow)를 쓴다. `OnInspectorGUI`·`OnGUI`·`OnDrawGizmos`는 IMGUI 신호다.
- IMGUI는 deprecated가 아니다. 즉시모드 디버그 도구, 빠른 프로토타이핑, UI Toolkit 미지원 API, IMGUI 부모(커스텀 인스펙터·부모 드로어) 안에서 자식을 그릴 때는 IMGUI가 정당하다.
- **UI Toolkit 드로어는 IMGUI 컨텍스트 안에서 동작하지 않는다.** 인스펙터가 IMGUI(`OnInspectorGUI`)인데 자식 필드 드로어가 `CreatePropertyGUI`만 구현하면 렌더에 실패한다. 반대 방향(UI Toolkit 인스펙터 + IMGUI 드로어)은 Unity가 `IMGUIContainer`로 감싸 렌더된다. 어디서나 렌더돼야 하는 드로어는 IMGUI로 구현하거나 두 방식을 모두 구현한다.
- UI Toolkit 트리 안에서 IMGUI를 그려야 하면 `IMGUIContainer(onGUIHandler)` 요소를 쓴다. 이 요소는 자체 IMGUI 컨텍스트다.
- 기본 인스펙터 UI를 UI Toolkit 트리에 붙이려면 `InspectorElement.FillDefaultInspector`를 쓴다(IMGUI의 `DrawDefaultInspector` 대응).

---

## SerializedObject/SerializedProperty 경유 (undo·멀티편집·프리팹)

에디터에서 직렬화 필드를 바꿀 때는 항상 `SerializedObject`를 경유한다. `target.field`를 직접 대입하면 undo, 멀티오브젝트 편집, 프리팹 오버라이드 기록, dirty 처리를 전부 우회한다.

```csharp
// Don't: 멀티선택 시 첫 target만 바뀌고, 프리팹 오버라이드가 기록되지 않아
//        씬 재오픈 시 값이 되돌아가며, Ctrl+Z가 안 된다.
target.speed = 5f;
EditorUtility.SetDirty(target);

// Do: undo · dirty · 프리팹 오버라이드 · 멀티편집이 모두 자동 처리된다.
serializedObject.Update();
serializedObject.FindProperty("speed").floatValue = 5f;
serializedObject.ApplyModifiedProperties();
```

- 왕복 패턴은 `Update()` → 프로퍼티 수정 → `ApplyModifiedProperties()`다. undo를 넣지 않으려면 `ApplyModifiedPropertiesWithoutUndo()`.
- 멀티편집은 `new SerializedObject(targets)`(복수)로 연다. 값이 서로 다르면 `hasMultipleDifferentValues`가 참이고, 커스텀 UI는 이를 존중해야 믹스드 값이 정상 표시된다.
- 순회: 루트 자식 진입은 `NextVisible(true)` 최초 1회, 이후 `NextVisible(false)`. 중첩 필드는 `FindPropertyRelative("child")`. 배열은 `arraySize` / `GetArrayElementAtIndex(i)`. `FindProperty`(절대)와 `FindPropertyRelative`(상대)를 혼동하지 않는다.
- `[SerializeReference]` 필드는 `propertyType == ManagedReference`, `managedReferenceValue`로 다룬다(폴리모피즘·null 지원).

---

## 커스텀 인스펙터

- `[CustomEditor(typeof(T))]` + `Editor` 파생. 멀티선택 편집은 `canEditMultipleObjects`, 파생 클래스 적용은 `editorForChildClasses: true`.
- 같은 타입에 `[CustomEditor]`를 중복 정의하지 않는다. 어느 것이 이길지는 로드 순서에 의존해 불확정이다.
- IMGUI 스켈레톤: `OnEnable`에서 `FindProperty` 캐시 → `OnInspectorGUI`에서 `Update()` → 필드 → `ApplyModifiedProperties()`.
- `OnEnable`에서 이벤트를 구독하면 `OnDisable`에서 대칭 해제한다. 도메인 리로드·재선택마다 재호출되므로 해제 누락은 중복 구독으로 누적된다.
- target이 파괴·재선택되면 캐시한 프로퍼티가 무효가 된다. `target == null` 가드를 둔다.
- 에디터는 런타임을 단방향 참조한다. 런타임 코드가 에디터를 참조하지 않는다.

---

## PropertyDrawer

- 타입 대상은 `[CustomPropertyDrawer(typeof(MyType))]`, 어트리뷰트 대상은 `[CustomPropertyDrawer(typeof(MyAttribute))]`. 파생 타입까지 적용하려면 두 번째 인자 `useForChildren: true`. 데이터 없이 장식만 하려면 `DecoratorDrawer`.
- IMGUI 드로어는 `EditorGUI.BeginProperty` / `EndProperty`로 전체를 감싼다. 누락하면 프리팹 오버라이드 표시·컨텍스트 메뉴가 깨진다.

```csharp
public override void OnGUI(Rect pos, SerializedProperty prop, GUIContent label) {
    EditorGUI.BeginProperty(pos, label, prop);   // 필수
    // ... 필드 그리기
    EditorGUI.EndProperty();
}
```

- 여러 줄을 그리면 `GetPropertyHeight`를 정확히 override한다. 누락하면 다음 필드와 겹치거나 잘린다.
- **드로어 인스턴스는 배열의 여러 요소에 재사용된다.** 요소별 상태(펼침 여부 등)를 멤버 필드에 저장하면 요소 간 누수된다. 상태는 `property.isExpanded`나 `propertyPath`를 키로 한 딕셔너리에 저장한다.
- UI Toolkit 드로어는 `CreatePropertyGUI(SerializedProperty)`를 override해 `VisualElement`를 반환한다(기본 인스펙터가 커스텀 드로어에서 UI Toolkit을 쓰는 것은 2022.2+).

---

## 데이터 바인딩과 값 변경 콜백

- 에디터 바인딩과 런타임 데이터 바인딩은 다른 시스템이다. 에디터 인스펙터·툴은 `SerializedObject` 바인딩을 쓴다. 런타임 게임 UI는 `dataSource`/`DataBinding`(2023.2+)을 쓴다. 에디터 UI에 런타임 바인딩을 쓰면 undo·직렬화 정합성이 없다.
- 컨트롤에 `binding-path`를 지정하고 `rootVisualElement.Bind(serializedObject)`를 호출한다. 인스펙터의 `CreateInspectorGUI`는 반환 직후 자동 bind되므로 별도 `Bind` 호출이 필요 없다. EditorWindow는 자동 bind가 없어 수동 호출한다.
- 값 변경 반응은 콜백 종류를 구분한다:
  - `RegisterValueChangedCallback`은 bind 시점에 초기값으로 1회 발화한다. 사용자 변경만 감지하려는 용도에는 부적합하다.
  - `TrackPropertyValue(prop, cb)`는 단일 프로퍼티 변경을 추적한다.
  - `TrackSerializedObjectValue(so, cb)`는 오브젝트의 아무 프로퍼티나 변경 시 발화한다.
  - 커스텀 인스펙터의 반응 로직에는 `Track...` 계열이 초기화 발화 문제를 피한다.
- 바인딩이 이미 write를 담당하므로, 콜백에서 다시 값을 대입하지 않는다(undo 스택 오염·멀티편집 깨짐). 콜백은 프리뷰 갱신 같은 side-effect만 한다.

---

## UXML / USS와 UxmlElement 소스 생성

- `UxmlFactory<T>` / `UxmlTraits`는 deprecated다(2023.2 도입, 6000.x 표준). 커스텀 컨트롤은 `[UxmlElement]` 방식을 쓴다.

```csharp
// Don't (deprecated)
public class MyControl : VisualElement {
    public new class UxmlFactory : UxmlFactory<MyControl, UxmlTraits> {}
    public new class UxmlTraits : VisualElement.UxmlTraits { /* ... */ }
}

// Do: 소스 제너레이터가 UxmlSerializedData를 생성한다. class는 반드시 partial.
[UxmlElement]
public partial class MyControl : VisualElement {
    [UxmlAttribute] public float threshold { get; set; }
}
```

- `partial` 누락 시 소스 생성이 실패한다. 노출 속성에는 `[UxmlAttribute]`, 중첩 객체에는 `[UxmlObject]`. 구 `UxmlTraits.Init()`의 초기화 로직은 필드 기본값·프로퍼티 setter로 옮긴다.
- 구조(UXML)·스타일(USS)·로직(C#)을 분리한다. 인라인 style보다 USS 클래스를 쓴다(재사용·테마).
- **UXML/USS 에셋은 `SerializeField VisualTreeAsset`/`SerializeField StyleSheet`로 참조하고 에디터에서 할당한다.** 문자열 경로 하드코딩이나 `Resources.Load`는 경로 이동 시 깨진다. 에디터 코드가 로드해야 하면 `AssetDatabase.LoadAssetAtPath<T>`를 쓴다.
- 색을 하드코딩하는 대신 `--unity-colors-*` 같은 built-in USS 변수를 참조하면 라이트/다크 테마에 자동 대응한다.

---

## EditorWindow 라이프사이클과 상태

- 라이프사이클 순서는 진입 경로마다 다르다. `GetWindow` 수동 열기는 `Awake` → `OnEnable` → `CreateGUI`, 재컴파일 후에는 `OnEnable` → `CreateGUI`, 프로젝트 열 때 이미 열린 창은 `CreateGUI` → `Awake` → `OnEnable` 순이다. GUI 구축은 `rootVisualElement`가 준비된 `CreateGUI`에서 한다.
- 창을 하나만 재사용하려면 `EditorWindow.GetWindow<T>("Title")`, 여러 인스턴스는 `CreateInstance<T>()` + `Show()`.
- 이벤트(`Selection.selectionChanged`, `Undo.undoRedoPerformed` 등)는 도메인 리로드 시 소멸한다. `OnEnable`에서 구독하고 `OnDisable`에서 해제한다. 비대칭이면 리로드마다 중복 등록돼 콜백이 N배 발화하고 파괴된 창 참조 예외가 난다.
- `OnGUI`/`CreateGUI`에서 에셋 스캔 같은 무거운 작업을 하지 않는다(매 repaint 실행·블로킹). `EditorApplication.update` 델리게이트로 분할하거나 async, `EditorUtility.DisplayProgressBar`를 쓴다.
- 상태 지속 범위를 목적에 맞게 고른다:
  - 창 내부 UI 상태 → `[SerializeField]`(창이 직렬화돼 리로드를 견딤)
  - 세션 임시값 → `SessionState`(에디터 종료 시 소멸)
  - 영구 개인 설정 → `EditorPrefs`(머신 전역)
  - 에디터 전역 데이터 → `ScriptableSingleton<T>`(`Save()` 명시 호출 필요)

---

## 에디터 전용 코드 격리

참조 방향은 항상 Editor → Runtime이다. 런타임 코드가 `UnityEditor`를 참조하면 빌드가 CS0246으로 깨진다. 분리 수단 세 가지(`Editor` 폴더, Editor 전용 asmdef, `#if UNITY_EDITOR`)와 `versionDefines`/`autoReferenced`는 build-project.md. 에디터 확장 특유의 함정만 여기 둔다.

- **함정: asmdef가 있는 폴더 하위의 `"Editor"` 폴더는 predefined Editor 어셈블리로 들어가지 않는다.** 그 런타임 asmdef에 포함돼 `UnityEditor` 참조 시 빌드가 실패한다. 해결은 그 폴더에 Include Platforms = Editor only인 별도 asmdef를 만들거나 에디터 코드를 asmdef 밖으로 뺀다.
- 런타임 타입에 기즈모·검증을 붙일 때 `OnValidate`·`OnDrawGizmos` 안의 `UnityEditor` 호출은 `#if UNITY_EDITOR`로 감싼다.

---

## 도메인 리로드 안전성과 static 리셋

도메인 리로드를 끄면 static 상태가 플레이 실행 간 잔존한다. 런타임 스크립트의 static 리셋 패턴(`RuntimeInitializeOnLoadMethod` / `SubsystemRegistration`)은 runtime-architecture.md. 에디터 코드 관점만 여기 둔다.

- 에디터 스크립트의 플레이 진입 리셋은 `[InitializeOnEnterPlayMode]`를 쓴다.
- `[InitializeOnLoad]`/`[InitializeOnLoadMethod]`는 에디터 로드·도메인 리로드 직후, **에셋 임포트 완료 전에** 호출될 수 있다. 이 안에서 에셋을 로드하면 null이 반환될 수 있다. 에셋 접근이 필요하면 `EditorApplication.delayCall`이나 `AssetPostprocessor.OnPostprocessAllAssets`로 지연한다.
- 이들 훅에서 무거운 작업을 하지 않는다(매 도메인 리로드 실행돼 리로드가 느려진다).

---

## Undo · Dirty · Save · 프리팹 정합성

- 변경 **직전**에 `Undo.RecordObject(obj, name)`를 호출한다(변경 후 호출은 순서 오류). 복수는 `RecordObjects`, 전체 스냅샷은 `RegisterCompleteObjectUndo`, 생성/구조 변경은 `RegisterCreatedObjectUndo`·`Undo.AddComponent`·`Undo.DestroyObjectImmediate`.
- 세 API의 역할이 다르다:
  - `EditorUtility.SetDirty` = dirty만(undo 없음)
  - `Undo.RecordObject` = dirty + undo
  - `PrefabUtility.RecordPrefabInstancePropertyModifications` = 프리팹 인스턴스 오버라이드 기록
- **프리팹 인스턴스의 프로퍼티를 직접 바꾸고 `SetDirty`만 하면 오버라이드가 기록되지 않아 씬 저장·재오픈 시 값이 되돌아간다.** `RecordPrefabInstancePropertyModifications`를 함께 호출한다.
- 조합:
  - 씬 오브젝트 + undo → `Undo.RecordObject`(프리팹 인스턴스면 `RecordPrefabInstancePropertyModifications` 추가)
  - ScriptableObject → `Undo.RecordObject` + `EditorUtility.SetDirty`
  - `SerializedObject` 경로 → `ApplyModifiedProperties`가 undo·dirty·오버라이드를 자동 처리하므로 별도 호출 불필요(가장 안전, 권장)
- 프리팹 에셋 직접 편집은 `PrefabUtility.LoadPrefabContents` → 편집 → `SaveAsPrefabAsset` → `UnloadPrefabContents`. 인스턴스를 통해 에셋을 오염시키지 않는다.
- 저장은 특정 에셋만 `AssetDatabase.SaveAssetIfDirty(obj)`를 선호한다. `SaveAssets()`는 전체 dirty를 저장해 비용이 크므로 배치 후 1회만 호출한다.

---

## 씬 뷰 도구화 (Handles · Gizmos · EditorTool · Overlay)

- 경계: 런타임 기즈모는 `OnDrawGizmos`/`OnDrawGizmosSelected`(MonoBehaviour), 에디터 씬 상호작용은 `Editor.OnSceneGUI` + `Handles` API. Unity 6의 표준 씬 툴은 `EditorTool` + `Overlay`다.
- `EditorTool` 파생 + `[EditorTool("Name", typeof(TargetComponent))]`. 메인 메서드는 `OnToolGUI(EditorWindow)`(창 repaint마다 실행). 씬 뷰 위 플로팅 UI는 `[Overlay]` + `Overlay`/`ToolbarOverlay` 파생, `SceneView.AddOverlayToActiveView`로 등록.
- Handles로 값을 편집할 때 undo 정합성 패턴:

```csharp
EditorGUI.BeginChangeCheck();
var newVal = Handles.PositionHandle(pos, rot);
if (EditorGUI.EndChangeCheck()) {
    Undo.RecordObject(target, "Move");   // 변경 직전
    target.value = newVal;               // 또는 SerializedProperty 경유
    // 프리팹 인스턴스면 PrefabUtility.RecordPrefabInstancePropertyModifications
}
```

- `BeginChangeCheck`/`EndChangeCheck` 없이 매 프레임 대입하면 undo가 누락되고 불필요한 dirty가 발생한다.
- `SceneView.duringSceneGui` 구독은 `OnEnable`, 해제는 `OnDisable`로 대칭. 전역 `duringSceneGui`는 모든 구독자가 받으므로 중복 처리에 주의한다(활성 툴만 `OnToolGUI`를 받는 것과 다르다).
- 기즈모·핸들은 매 프레임 그려지므로 `new Vector3[]` 같은 할당을 피하고 캐시한다. `UnityEditor`를 참조하는 코드는 `#if UNITY_EDITOR`로 감싼다.

---

## 에셋 파이프라인 훅

- 역할 구분:
  - `AssetPostprocessor` = 임포트 파이프라인 훅. `OnPreprocess*`(임포트 전 설정), `OnPostprocess*`(임포트 후 수정), `OnPostprocessAllAssets`(임포트 완료 후 1회, 도메인 리로드 신호 파라미터 제공)
  - `ScriptedImporter` = 커스텀 파일 확장자를 에셋으로 임포트
  - `AssetModificationProcessor` = 저장·생성·삭제·이동 가로채기(`OnWillSaveAssets` 등). 임포트가 아니다.
- 임포트 중 다른 에셋을 참조하면 의존성을 명시 등록해야 재임포트가 결정적이다(`ctx.DependsOnSourceAsset(path)` 등). 미등록 시 stale·비결정적 결과.
- 훅에서 자신이 처리하는 에셋의 재임포트를 트리거하는 수정을 하지 않는다(무한 루프). 모든 에셋에 무조건 반응하지 말고 경로·확장자로 필터한다.
- 다수 임포트는 `StartAssetEditing()` … `StopAssetEditing()`로 묶는다. **`StopAssetEditing`은 `try/finally`로 보장한다.** 사이에서 예외로 빠져나가면 AssetDatabase가 무응답 상태가 된다.

```csharp
AssetDatabase.StartAssetEditing();
try {
    // 다수 에셋 생성/수정
} finally {
    AssetDatabase.StopAssetEditing();   // 예외에도 반드시 실행
}
```

- `AssetDatabase.Refresh()`는 전체 스캔을 유발하므로 남용하지 않는다. AssetDatabase는 메인 스레드 전용이다. `.meta`는 직접 수정하지 않고 `AssetImporter`·직렬화 API를 경유한다.

---

## 메뉴 · 단축키 · 설정 지속성

- `[MenuItem("Path/Item")]`은 정적 메서드에 메뉴를 추가한다. priority 인자로 정렬(11 이상 차이면 구분선), validate 함수는 같은 path에 두 번째 인자 `true` + bool 반환. path 끝 단축키 specifier는 `%`(Ctrl/Cmd) `#`(Shift) `&`(Alt) `_`(modifier 없음).
- 재바인딩 가능한 단축키는 `[Shortcut(...)]`(Shortcut Manager)를 쓴다. MenuItem 단축키보다 유연하고 충돌을 UI에서 해결한다.
- 컴포넌트 컨텍스트 메뉴는 `[ContextMenu("Name")]`, 필드 우클릭 액션은 `[ContextMenuItem(label, method)]`.
- `[SettingsProvider]`로 Project Settings/Preferences 페이지를 추가한다. `SettingsScope.Project`는 팀 공유, `SettingsScope.User`는 개인(Preferences).
- 지속성 선택:
  - 팀 공유 설정 → 에셋/ProjectSettings 직렬화(버전 관리 커밋)
  - 개인 설정 → `EditorPrefs`(머신 전역) 또는 `UserSettings`(사용자별, 보통 .gitignore)
  - 세션 임시 → `SessionState`
  - 절대 경로·머신 종속 값은 공유 에셋에 커밋하지 않는다.
- `EditorPrefs`·`SessionState`는 전역 문자열 키다. 회사·프로젝트 접두(`com.company.tool.key`)로 충돌을 피한다.
- 메뉴·SettingsProvider 코드도 에디터 전용 격리 대상이다(Editor 폴더/asmdef 밖에 두면 빌드가 깨진다).

---

## 서드파티 인스펙터(Odin) 공존

- Odin은 자체 드로어 시스템을 쓰며 Unity `PropertyDrawer`를 쓰지 않는다. Odin 드로어는 Odin 커스텀 에디터 안에서만 동작한다. Odin 커스텀 에디터는 `Editor` 대신 `OdinEditor`를 상속한다.
- Odin은 `[CustomEditor]`를 전역적으로 `OdinEditor`로 대체할 수 있어 순정 커스텀 에디터와 충돌·이중 그리기가 발생할 수 있다.
- 재사용 코드는 순정 Unity API(`SerializedObject`/`SerializedProperty`/`PropertyDrawer`)를 유지하고, Odin 의존은 조건부로 격리한다. asmdef `versionDefines`로 `ODIN_INSPECTOR` 심볼을 정의하고 `#if ODIN_INSPECTOR`로 감싸며 순정 fallback을 둔다.
- 프로젝트에 Sirenix/Odin 패키지가 실제로 있는지 확인한 뒤에만 Odin 관용구를 넣는다. `[ShowInInspector]`나 Odin custom serializer 필드는 순정 `FindProperty`로 못 찾을 수 있다.
