# C# 코드 스타일·구조 (Unity 6)

기준 버전: Unity 6000.x (6.5까지 반영) · 최종 확인: 2026-07

목차
- 1. C# 언어 버전 상한 (가장 먼저 확인)
- 2. 네이밍
- 3. SerializeField와 인스펙터 노출
- 4. 포맷·레이아웃
- 5. 네임스페이스와 파일 조직
- 6. 클래스 멤버 순서
- 7. 접근 제한자와 불변성
- 8. Unity의 null 특수성 (일반 .NET과 다름, 반드시 지킨다)
- 9. record와 불변 데이터
- 10. MonoBehaviour·라이프사이클
- 11. 이벤트·델리게이트·async
- 12. 스타일 강제 도구
- AI가 자주 틀리는 지점 요약

Unity 6 프로젝트에서 C# 코드를 새로 쓰거나 리뷰할 때 참조한다. 언어 버전 상한, 네이밍, 직렬화, MonoBehaviour 규약, Unity 특유의 null 처리처럼 일반 .NET 관례와 갈리는 지점을 다룬다. 스타일이 팀마다 갈리는 항목은 "기존 코드베이스의 실제 스타일이 진실"이 최상위 규칙이다.

---

## 1. C# 언어 버전 상한 (가장 먼저 확인)

Unity 6의 기본 C# 언어 버전은 **C# 9.0**이다(6.3 LTS 동일). AI가 가장 자주 깨뜨리는 지점이 이 상한이므로 먼저 본다.

`Assets/csc.rsp`의 `-langversion`으로 상향은 가능하나 **하지 않는다**: 미지원 기능이 런타임/빌드에서 깨지고, IDE IntelliSense가 버전을 오인식하며, Burst 등 서브시스템과 충돌한다.

**사용 금지 (C# 10~12, 기본 설정에서 컴파일 실패 또는 미동작):**
- file-scoped namespace (`namespace Foo;`) (C# 10)
- global using (C# 10)
- record struct (C# 10)
- 비-record primary constructor (C# 12)
- collection expression (`[..]`, `[1, 2, 3]`) (C# 12)
- `required` 멤버 (C# 11)
- init-only setter: C# 9 문법이나 Unity 미지원(`IsExternalInit` 부재). shim 없이 동작 안 함.

**사용 가능 (C# 9 이하):**
- switch expression (C# 8)
- 패턴 매칭(type/property/relational) (C# 7~9)
- target-typed `new()`, target-typed 조건식 (C# 9)
- `record class` (C# 9): **원칙적으로 피한다.** positional record·init 프로퍼티는 `IsExternalInit` shim이 프로젝트에 선언돼 있어야 컴파일된다. shim 존재를 확인하기 전에는 생성하지 않는다(§9).

```csharp
// Don't: 컴파일 실패
namespace Game.Combat;
public record struct Damage(int Amount);

// Do
namespace Game.Combat
{
    public readonly struct Damage
    {
        public int Amount { get; }
        public Damage(int amount) => Amount = amount;
    }
}
```

---

## 2. 네이밍

**케이스:**
- **PascalCase**: 클래스·구조체·인터페이스·enum명, 메서드, 프로퍼티, public 필드, 네임스페이스, 상수.
- **camelCase**: 지역변수, 메서드 파라미터, private 필드.
- 인터페이스는 `I` 접두사 + 기능 형용사: `IDamageable`, `IKillable<T>`.

**멤버 접두사는 팀 스타일 문제다.** 하나를 골라 일관되게 쓴다. 기존 코드가 있으면 그 스타일을 따른다. 프로젝트에 정해진 스타일이 없을 때만 새로 정한다.
- Unity 내부 스타일: private `m_`, 상수 `k_`, static `s_`. IDE 없이 스코프를 즉시 판별한다.
- .NET 표준 스타일: private `_camelCase`. Rider/VS 기본 관례라 도구 마찰이 없다.
- 무접두사 스타일: 접두사를 생략하고 `this.`로 멤버/지역을 구분한다. 더 짧다.

**상수는 SCREAMING_CASE가 아니다.** C#/Unity에서 `MAX_ITEMS`는 관례가 아니다. PascalCase를 쓰고, 접두사 스타일이면 `k_MaxItems`.

```csharp
// Don't
const int MAX_ITEMS = 100;        // SCREAMING_CASE

// Do (무접두사 스타일)
private int health;
const int MaxItems = 100;

// Do (.NET 표준 스타일)
private int _health;
const int MaxItems = 100;

// Do (Unity 내부 스타일)
private int m_health;
const int k_MaxItems = 100;
```

**불리언은 동사 접두사로 질문형을 만든다**: `isDead`, `hasMultiplier`, `canJump`, `shouldRespawn`. 불리언 반환 메서드도 마찬가지: `IsGameOver()`, `HasStartedTurn()`.

**측정 단위를 이름에 넣는다**: `int d` 대신 `int elapsedTimeInDays`, `GetDistanceToTargetInMeters()`. 각도는 플래그 대신 `GetAngleInDegrees()` / `GetAngleInRadians()`로 분리한다.

**약어를 쓰지 않는다**(수식/루프 인덱스 제외). `mvmtSpeed` 대신 `movementSpeed`. 발음·검색 가능한 이름이 AI 코드 생성 정확도에도 유리하다.

**중복을 뺀다**: `Player` 클래스의 `PlayerScore`는 `Score`로.

**이니셜리즘 케이스**(MSFT 관례): 2글자 약어는 둘 다 대문자(`ID`, `IO`, `UI`), 3글자 이상은 Pascal(`Html`, `Xml`). 게임 도메인 약어(HP 등)의 케이스는 팀 합의 사항이다.

**이벤트명은 시점을 구분한다**: 발생 전은 현재분사(`OpeningDoor`), 후는 과거분사(`DoorOpened`).

---

## 3. SerializeField와 인스펙터 노출

**인스펙터에 값을 노출할 때 public 필드가 아니라 `[SerializeField] private`를 쓴다.** 외부 오브젝트가 값을 덮어쓰는 것을 막아 캡슐화가 낫다.

```csharp
// Don't
public PlayerStats stats;

// Do
[SerializeField] private PlayerStats stats;
```

**직렬화 규칙**: `static`/`const`/`readonly` 필드는 직렬화되지 않는다. 인스펙터로 튜닝할 값은 `const`가 아니라 `[SerializeField] private` 필드로 둔다.

**프로퍼티에는 `[SerializeField]`가 먹지 않는다.** auto-property의 백킹 필드를 직렬화하려면 `field:` 타깃을 쓴다.

```csharp
// Don't: 무효, 직렬화 안 됨
[SerializeField] public int Health { get; private set; }

// Do
[field: SerializeField] public int Health { get; private set; }
```
주의: `[field: SerializeField]`의 백킹 필드는 `<Health>k__BackingField` 이름으로 직렬화된다. 나중에 순수 필드로 리팩터하면 저장된 직렬화 데이터가 깨진다. 오래 유지할 게임플레이 데이터에는 일반 `[SerializeField] private` 필드가 안전하다.

관련 어트리뷰트(`[Range]`, `[Tooltip]`, `[Header]`, `[Space]`, `[HideInInspector]`, `[System.Serializable]`)는 필드 위 별도 줄에 둔다.

---

## 4. 포맷·레이아웃

- **중괄호는 Allman 스타일**: 여는 중괄호를 새 줄에 둔다.
- **들여쓰기는 스페이스**(4 또는 2, 팀 합의). 탭은 변환한다.
- **단일 문장이어도 중괄호를 생략하지 않는다.**
- `var`는 **우변에서 타입이 드러날 때만** 쓴다.

```csharp
// Do: 타입이 우변에 보임
var powerUps = new List<PowerUp>();

// Don't: 반환 타입이 안 보임
var powerUps = PowerUpManager.GetPowerUps();
```

- **표현식 본문 멤버(`=>`)는 단일 줄 read-only 프로퍼티에** 쓴다: `public int MaxHealth => maxHealth;`. 그 외는 `{ get; set; }` auto-property.
- 콤마 뒤 스페이스 1개, 괄호 안쪽 스페이스 없음, `if (`/`while (`처럼 키워드와 여는 괄호 사이 스페이스 1개, 비교연산자 앞뒤 스페이스.
- redundant·implicit 접근 한정자와 초기화는 생략한다(§7).

---

## 5. 네임스페이스와 파일 조직

- **블록 네임스페이스만 쓴다.** file-scoped namespace와 global using은 C# 10이라 컴파일되지 않는다(§1).
- 네임스페이스는 PascalCase, 특수문자·언더스코어 없이 `.`로 계층 구분: `Game.AI`, `Game.UI`.
- **파일당 MonoBehaviour는 하나, 파일명 = 클래스명.** Unity가 강제한다. 불일치하면 컴포넌트 부착·직렬화가 실패한다. 비-MonoBehaviour 내부 클래스는 허용된다.

---

## 6. 클래스 멤버 순서

권장 순서: **Fields → Properties → Events/Delegates → MonoBehaviour 메서드(Awake, OnEnable, Start, OnDisable, OnDestroy) → Public 메서드 → Private 메서드.** 상수는 Fields 그룹에 둔다.

상위 개념 메서드를 먼저, 세부 구현 메서드를 아래에 둔다(`ThrowBall()` 먼저, `CalculateTrajectory()`는 아래).

**`#region`을 쓰지 않는다.** 클래스가 커서 region으로 접어야 한다면 클래스를 쪼갠다.

---

## 7. 접근 제한자와 불변성

- **최소 공개**를 원칙으로 한다. 인스펙터 노출은 `[SerializeField] private`, 외부 읽기는 read-only 프로퍼티로 연다: `public int MaxHealth => maxHealth;` 또는 `{ get; private set; }`.
- **redundant·implicit 한정자와 초기화를 생략한다**: 타입 스코프의 `private`, `int x = 0`, `bool b = false`, `ref = null` 같은 자동 기본값 대입을 지운다.
- **`const` vs `static readonly`**: 값이 절대 안 변하는 원시·문자열이면 `const`, 참조 타입·런타임 계산이면 `static readonly`. 둘 다 직렬화되지 않으므로 인스펙터 튜닝값에는 쓰지 않는다.

---

## 8. Unity의 null 특수성 (일반 .NET과 다름, 반드시 지킨다)

Unity는 `UnityEngine.Object`의 `==`를 오버로드한다. **파괴된 오브젝트는 C# 참조가 살아 있어도 `== null`이 true**다("fake null"). `?.`·`??`는 이 오버로드를 우회해 파괴된 오브젝트를 "not null"로 오판하고 `MissingReferenceException`을 낸다.

**UnityEngine.Object 파생 타입에는 `?.`·`??`를 쓰지 않고 명시적 `!= null`/`== null`을 쓴다.** 분석기 `UNT0008`/`USP0002`가 이를 잡는다(§12). 단, 델리게이트/이벤트는 `UnityEngine.Object`가 아니므로 `?.Invoke()`는 정당하다(§11).

fake null 세부 의미, `ReferenceEquals`로 진짜 null 판별, `#nullable enable`과 `[SerializeField]` 충돌(CS8618·USP0016)은 runtime-architecture.md.

---

## 9. record와 불변 데이터

**`record`는 원칙적으로 피한다.** positional record와 init 프로퍼티는 `System.Runtime.CompilerServices.IsExternalInit` 타입을 프로젝트에 직접 선언해야 컴파일된다(Unity는 .NET 5 미지원). shim 존재를 확인하기 전에는 record를 생성하지 않는다. shim이 있어도 Unity 직렬화가 지원하지 않아 인스펙터에 뜨지 않으므로 MonoBehaviour·ScriptableObject·직렬화 데이터에는 쓰지 않는다. `record struct`는 아예 컴파일되지 않는다(§1).

record 대신 불변 데이터는 **`readonly struct` + get-only 프로퍼티 + 생성자**로 만든다. init-only setter를 못 쓰므로 생성자로 값을 세팅한다. 값 동등성이 필요하면 `IEquatable<T>`를 수동 구현한다.

작은 값 묶음은 `struct`, 크거나 참조 공유·상속이 필요하면 `class`로 한다. 사용자 정의 타입을 인스펙터에 중첩 노출하려면 `[System.Serializable]`을 붙이고 내부 필드를 public으로 둔다.

---

## 10. MonoBehaviour·라이프사이클

- **빈 생명주기 메서드를 제거한다.** 빈 `Update()`도 등록 오버헤드가 있다. 분석기 `UNT0001`이 잡는다. 이유·매니저 패턴은 performance.md.
- **`GetComponent`는 `Awake()`/`Start()`에서 1회 캐싱한다.** 매 프레임 호출 금지. 존재가 불확실하면 `TryGetComponent`. 캐싱 전략은 performance.md.
- 의존 컴포넌트는 `[RequireComponent]`로 강제한다.
- Awake/OnEnable/Start의 책임 분리와 실행 순서 보장은 runtime-architecture.md.
- **컴포지션·단일 책임을 우선**한다. 하나의 MonoBehaviour가 입력·이동·오디오·로직을 다 하는 것은 안티패턴이다. 역할별로 컴포넌트를 나눈다.

---

## 11. 이벤트·델리게이트·async

- **이벤트는 `System.Action`/`Action<T>`를 선호한다.** 대부분의 게임플레이 이벤트에 충분하다.
- 이벤트 발생은 null 조건 Invoke로: `DoorOpened?.Invoke();`. 델리게이트이므로 `?.`가 정당하다(§8의 UnityEngine.Object 규칙과 구분).
- 구독/해제 대칭(OnEnable/OnDisable), named 메서드 등록, UnityEvent·SO 이벤트 채널 선택은 runtime-architecture.md.
- **비동기 메서드에는 `Async` 접미사**를 붙인다. Awaitable/UniTask/코루틴 선택과 async 실행 모델은 runtime-architecture.md.

---

## 12. 스타일 강제 도구

- **`.editorconfig`**를 프로젝트 루트에 둔다(`root = true`). VS·Rider·VS Code가 네이티브 지원하므로 코드와 함께 버전관리하는 게 IDE 설정 공유보다 낫다. 예: `[*.cs] indent_style = space`, `indent_size = 4`, `trim_trailing_whitespace = true`, `insert_final_newline = true`.
- **`Microsoft.Unity.Analyzers`**(무료 Roslyn 분석기)로 Unity 안티패턴을 자동 검출한다: `UNT0001`(빈 메시지), `UNT0008`/`USP0002`(null 전파) 등. AI 생성 코드 검증에 특히 유효하다. asmdef 단위로 부착하거나(Unity 2020.2+ Roslyn 분석기 지원) VS Tools for Unity 번들을 쓴다.
- 커밋 전 `dotnet format`(.editorconfig 기반)과 분석기를 훅에서 돌리면 스타일이 자동 검증된다.

---

## AI가 자주 틀리는 지점 요약

1. C# 10~12 문법 생성(file-scoped namespace, global using, record struct, collection expression, primary constructor) → C# 9 상한 초과로 빌드 실패.
2. private 필드 `_` 접두사·상수 SCREAMING_CASE를 .NET 관례대로 이식 → 프로젝트 스타일과 불일치.
3. 직렬화 필드를 `public`으로 노출 → `[SerializeField] private`로 교정.
4. 프로퍼티에 `[SerializeField]` 직접 부착 → `[field: SerializeField]` 필요.
5. `UnityEngine.Object`에 `?.`/`??` 사용 → fake null 함정. `!= null` 사용.
6. record를 직렬화 타입에 사용 → 인스펙터에 안 뜸.
7. 빈 `Update()`/`Start()` 방치 → 제거.
