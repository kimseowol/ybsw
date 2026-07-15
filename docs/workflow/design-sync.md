# 기획서 변경 감지 (design-sync)

기획 진실 원본(Figma "RS - 상세기획서 for claude"의 **1차 프로토 프레임**, 2476:396)과
로컬 반영분의 차이를 기계적으로 잡는 절차. 도구는 `scripts/design-diff.ps1` (pwsh, Figma REST API).

## 기준선의 의미

`docs/design-sync/`의 덤프(원문 텍스트 + 구조)는 **"마지막으로 GD-003·PRD에 전파를 마친 시점"**의
기획서 상태다. 따라서 diff 출력 = "아직 개발에 반영되지 않은 기획 변경". 전파를 마쳤을 때만
`-Update`로 갱신하고 커밋한다 — 전파 없이 갱신하면 미반영 변경이 diff에서 사라진다.

## 절차

1. **프로브** — `pwsh scripts/design-diff.ps1 -Probe` (1콜, 수 초). Figma는 어떤 편집에도 파일
   version을 올리므로 버전 비교만으로 변경 여부가 확정된다. 동일하면 끝.
2. **diff** — `pwsh scripts/design-diff.ps1`. 변경 문장이 unified diff로 나온다 (exit 1).
   텍스트는 `1차-프로토.text.md`, 노드 추가/삭제/이동·크기는 `1차-프로토.structure.txt`에 걸린다.
3. **해석·전파** (사람/AI 판단 구간) — diff를 보고: GD-003 정리본 동기화(docs/design-draft/),
   관련 PRD 드리프트 검사·결정 기록, 코드 영향 평가. 기획 변경이 필요해 보이면 docs/design/
   불가침 규칙대로 수정하지 않고 보고만 한다.
4. **기준선 갱신** — 전파 완료 후 `-Update` 실행, `docs/design-sync/` 변경 커밋.

## 보조 기능

- `-ListVersions` — 버전 히스토리 (편집자·KST 시각·버전 id).
- `-AtVersion <id>` — diff/Update를 과거 버전 기준으로. "어제 뭐가 바뀌었나" 포렌식이나,
  기준선을 특정 시점으로 되돌릴 때 사용.

## 한계·주의

- 텍스트·구조(기하)만 덤프한다. 아트 이미지 교체 자체는 diff에 안 걸린다 — 다만 프로브(버전
  비교)는 모든 편집을 잡으므로 "버전은 바뀌었는데 diff가 빈" 경우가 시각 변경의 신호다.
- 토큰은 리포 루트 `.env`의 `FIGMA_TOKEN` (gitignore 대상) 또는 환경 변수. 커밋 금지.
- 레이어명은 템플릿 복붙이 흔해 신뢰할 수 없다 — 섹션 식별은 badge-text 텍스트로 한다.
- 같은 파일의 다른 프레임(룬·보스 등)은 현행 스펙이 아니다. 이 도구는 1차 프로토 프레임만 본다.
