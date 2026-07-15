# 외부 라이브러리 도입 정책

외부 라이브러리는 기능이 처음 필요해지는 작업에서 도입한다. 패키지만 미리 설치하거나 여러 라이브러리로 같은 책임을 표현하지 않는다. 도메인 규칙과 결정적 실행 순서는 프로젝트 코드가 소유하고, 외부 라이브러리는 저작·입출력·표현처럼 교체 가능한 경계에서만 사용한다.

## 공통 규칙

- 첫 실제 소비자와 같은 변경에서 설치한다.
- Git 패키지는 릴리스 태그가 가리키는 커밋 해시로 고정하고 `Packages/packages-lock.json`을 커밋한다.
- 필요한 asmdef만 패키지를 참조한다. `Core`, `Combat`, `Crowd`, 스킬 VM에는 표현·저작 라이브러리를 전파하지 않는다.
- 도입한 범위에서는 대체한 수동 구현을 제거한다. 구 경로와 새 경로를 fallback으로 병행하지 않는다.
- 라이브러리를 감싸는 범용 facade는 만들지 않는다. 반복되는 프로젝트 고유 계약이 확인될 때만 좁은 어댑터를 둔다.
- 새 라이브러리를 검토할 때는 현재 문서의 기존 선택과 책임이 겹치는지 먼저 확인한다.

## 현재 선택

### LitMotion

- 상태: 도입됨 (`2.0.2`, commit `0b4c588ee75a07198841d92aab653e6b39445089`)
- 허용 경계: `Tharsis.Presentation`
- 용도: 유한한 UI·VFX 보간, 반복, 시퀀스와 명시적 motion 수명 관리
- 금지: 스킬 쿨다운, 상태 수명, 투사체 판정, 몬스터 이동, Flow Field, 결정적 스킬 스케줄러
- 수명 규칙: 소유 컴포넌트가 handle을 보관하고 비활성화·폐기 시 취소한다. 일시정지 중 진행해야 하는 UI는 ignore-time-scale scheduler를 명시한다.
- 중복 금지: DOTween을 함께 도입하지 않는다. Inspector 중심의 DOTween Pro 워크플로가 제품 요구가 될 때만 둘 중 하나를 다시 선택한다.

### Alchemy

- 상태: Inspector 기능만 도입됨 (`2.1.0-tharsis.1`). 공식 `2.1.0` commit `d9f5a2b8f9b7e79ac7fc7eb188b607bd60910398`을 embedded package로 고정한다.
- 로컬 패치: Unity 6000.3~6000.5에서 교체된 Hierarchy `EntityId`와 `AdvancedDropdown` API만 조건부 대응한다. 패치 목록은 `Packages/com.annulusgames.alchemy/THARSIS_PATCHES.md`가 소유하며, upstream 릴리스가 같은 호환성을 제공하면 embedded package를 공식 Git 패키지로 되돌린다.
- 허용 경계: 기획자·아티스트가 편집하는 authoring asset이 속한 어셈블리. 현재 gameplay 콘텐츠 저작은 `Tharsis.Content`만 참조한다.
- 용도: 필드 그룹, 조건부 표시, 라벨, 설명, 입력 검증과 안전한 Editor action 진입점
- 금지: `AlchemySerialize`를 런타임 데이터 형식이나 범용 직렬화 계층으로 사용하지 않는다. Inspector 편의를 이유로 도메인 타입을 다형 객체 그래프로 바꾸지 않는다.
- 확장 규칙: 실제 authoring asset이 Alchemy 기능을 사용할 때만 해당 asmdef 참조를 추가한다.

## 보류 중인 선택과 도입 트리거

| 후보 | 현재 판단 | 도입을 다시 검토할 조건 | 허용할 경계 |
|---|---|---|---|
| Unity Awaitable / UniTask | 내장 `Awaitable` 우선, UniTask 미도입 | Addressables·씬 전환·저장·네트워크 작업의 취소와 병렬 합류가 반복됨 | Infrastructure·화면 전환 |
| Newtonsoft JSON | 미도입 | 저장 버전 마이그레이션, 외부 import/export, 다형 DTO가 실제로 필요함 | Save·Importer·Network DTO |
| VYaml | 미도입 | 기획자가 Unity 밖에서 YAML을 직접 저작해야 한다는 워크플로가 확정됨 | Editor importer |
| R3 | 미도입 | UI read model이 여러 상태·이벤트를 조합하고 수동 구독 수명이 반복 문제를 만듦 | Presentation UI 내부 |
| Yarn Spinner | 미도입 | 분기 대화와 현지화 대사가 실제 콘텐츠 축이 됨 | 독립 Narrative 어셈블리 |
| DOTween | 미도입 | LitMotion보다 DOTween Pro의 시각 저작 워크플로가 반드시 필요함 | LitMotion을 제거한 Presentation |

## 데이터 경계

포맷이나 Inspector 라이브러리가 런타임 모델을 소유하지 않는다.

```text
ScriptableObject / 전용 텍스트 / 외부 DTO
                  ↓ 검증·컴파일
          불변 런타임 데이터
                  ↓
   Combat / Skills / Crowd / Progression
```

- Unity에서 편집하는 일반 콘텐츠는 ScriptableObject를 우선한다.
- 스킬 동작의 단일 원본은 기존 스킬 언어다.
- JSON은 저장·교환 경계에만 사용하고 Unity 오브젝트나 MonoBehaviour를 직접 직렬화하지 않는다.
- Yarn 파일은 대화만 소유하며 전투·보상·진행은 타입이 명확한 게임 포트를 호출한다.

## 참고

- [LitMotion 공식 문서](https://annulusgames.github.io/LitMotion/)
- [Alchemy 공식 문서](https://annulusgames.github.io/Alchemy/)
- [UniTask](https://github.com/Cysharp/UniTask)
- [R3](https://github.com/Cysharp/R3)
- [VYaml](https://github.com/hadashiA/VYaml)
- [Yarn Spinner for Unity](https://yarnspinner.dev/docs/unity/02-installation-and-setup/)
- [Unity Newtonsoft JSON](https://docs.unity.cn/Packages/com.unity.nuget.newtonsoft-json%403.2/manual/index.html)
