# ybsw

기니피그 레이싱 게임 — Unity 6 (URP) 게임 클라이언트. 게임 코드는 `Assets/Scripts/` 아래 도메인별 폴더.

## 개발 프로세스

기획 문서(docs/design/) → 개발 PRD(docs/prd/) → 코드(Assets/Scripts/) 세 축을 연결해 개발한다.

PRD는 why 전용 문서다: 의도, 경계, 결정 기록, 열린 질문만 담는다. 수치·알고리즘·구현 방법은 적지 않는다 — how의 원본은 코드다.

현재 상태를 분석할 때는 코드가 진실이다. 승인된 PRD를 구현할 때는 PRD가 목표고 코드가 변경 대상이다. 두 모드를 혼동하지 않는다.

작업 시작 전 기능 의도·경계·외부 동작이 바뀌는지 먼저 판정한다. 내부 구조만 바꾸는 동작 불변 리팩토링은 태그 PRD를 회귀 제약으로 읽되 새 PRD를 만들거나 기존 PRD·INDEX 상태를 갱신하지 않는다. 새 PRD는 기존 PRD로 설명할 수 없는 독립 why가 생길 때만 만든다 — 여러 PRD의 파일을 건드리거나 공유 클래스를 추출한다는 사실만으로는 부족하다.

### 규칙

1. 기능 작업은 `docs/prd/`의 **승인** 상태 PRD 기반으로만 진행한다. 없으면 `docs/workflow/prd.md`의 생성 절차로 먼저 만들고 승인을 받는다. 사용자가 PRD를 언급하지 않아도 이 규칙은 적용된다.
2. **`docs/design/`은 AI 수정 금지 (read-only).** 기획 변경이 필요해 보이면 고치지 말고 사용자에게 보고한다. AI가 기획 초안을 쓸 때는 `docs/design-draft/`에 쓰고 사람이 옮긴다.
3. **결정 기록 실시간 append**: 구현 대화 중 방향이 정해질 때마다 — 사용자 피드백이든 AI 판단이든 — 그 자리에서 해당 PRD의 결정 기록에 `날짜 (출처): 결정 — 이유` 형식으로 추가한다. 작업 후 몰아 쓰지 않는다.
4. 코드를 수정하기 전에 파일 상단 `// PRD-###` 태그의 PRD를 읽는다. 새 스크립트에도 태그를 붙인다. 태그는 파일당 1개(주 소유 PRD)다. 여러 PRD가 소비하는 코드에 구현과 독립된 고유 why가 있으면 독립 PRD로 분리하고, 단순 추출·배선 코드는 기존 의도를 소유한 PRD 하나를 태그한다.
5. `docs/INDEX.md`(3축 추적 매트릭스)는 PRD 생성·상태 변경 시 함께 갱신한다.

### 하네스 구성

절차 원본은 `docs/workflow/prd.md` 하나다 (생성/구현/동기화 검사). 스킬 원본은 `.agents/skills/` (Codex가 직접 읽음), Claude Code용 `.claude/skills/`는 정션이다.

- **클론 후 `scripts/setup.ps1`을 1회 실행** — 스킬 정션 생성 + git 훅(커밋 게이트·pre-push) 등록. Claude Code·Codex의 SessionStart 훅이 매 세션 자동 실행하므로(멱등) 에이전트 작업에서는 누락되지 않는다.
- **커밋 게이트**: `Assets/Scripts`의 C# 변경은 (1) 파일당 `// PRD-###` 태그 정확히 1개, (2) `docs/prd` 갱신 동반 스테이징을 요구한다. 동작이 안 바뀌는 변경(리팩토링·주석·포맷)은 커밋 메시지에 `[prd-skip] 사유`로 통과한다. **`--no-verify` 우회 금지.**
- **push 게이트**: `scripts/check-prd.ps1` 기계 검사(태그 규약·PRD 실존·code 경로 실존)를 통과해야 push 된다. 검사는 수동 실행도 가능하다.
- Codex는 최초 세션에서 `/hooks`로 프로젝트 훅을 신뢰 승인해야 한다.
- **Unity MCP** (CoplayDev unity-mcp): `.mcp.json`(Claude Code)과 `.codex/config.toml`(Codex)에 프로젝트 레벨로 설정돼 있다. Unity 에디터가 열려 있어야 서버가 동작한다.

## 검증

- `Assets/Scripts` 변경은 커밋 전에 Unity에서 검증한다 — 컴파일 클린, 관련 테스트 그린, 동작이 바뀌는 변경은 실제 동작 확인까지.
- 검증 수단은 Unity MCP다: 컴파일 확인은 `refresh_unity` → `read_console`(error), 테스트는 `run_tests`(EditMode/PlayMode) → `get_test_job`, 실동작 확인은 `manage_editor` play + 콘솔 + `manage_camera` 스크린샷, 플레이 중 상태 질의는 `execute_code`(Roslyn 설치됨). 에디터 준비 여부는 `mcpforunity://editor/state`의 `ready_for_tools`로 게이트한다.
- 완료 보고에는 무엇을 어떻게 검증했는지를 포함한다. 검증하지 못한 것은 하지 못했다고 말한다 — 침묵 통과 금지.
- 기능 구현에는 그 기능의 회귀 테스트가 동반된다. 테스트는 `Assets/Tests`에 쌓는다 — 검증의 축적은 문서가 아니라 테스트 자산이 담당한다.

## 문서 ID
- 기획: `GD-###` (기획자 소유). PRD: `PRD-###`. 상태: 초안 → 승인 → 구현중 → 완료 / 드리프트.

## 코드 컨벤션
- **Assets/Scripts 코드 작업 전에 unity-conventions 스킬을 먼저 읽는다** — 범용 Unity 규약(fake null, 폐기 API, GC, C# 버전 상한 등). 이 프로젝트의 Unity 버전은 6000.5이므로 6.5 규칙까지 전부 적용된다.
- 프로젝트 고유 스타일 컨벤션은 아직 없다. 코드베이스가 쌓이면 실코드에서 도출해 project-conventions 스킬로 추가한다. 그 전까지는 unity-conventions와 기존 코드의 스타일을 따른다.
- 커밋 메시지는 commit-conventions 스킬을 따른다.
- Unity 에셋(.unity, .prefab, .asset, .meta)은 명시적 요청 없이 수정하지 않는다.
