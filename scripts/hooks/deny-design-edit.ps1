# PreToolUse 훅 (Codex 전용): docs/design/ 을 건드리는 파일 편집을 차단한다.
# Claude Code는 .claude/settings.json의 permissions.deny가 같은 역할을 한다 (규칙 원본: AGENTS.md 규칙 2).
# apply_patch 계열 편집 도구에만 물리므로 읽기·검색은 걸리지 않는다.
# 패치의 대상 경로 헤더("*** Update File: <경로>", "*** Move to: <경로>")만 본다 —
# 본문에 docs/design/ 문자열이 나오는 파일(AGENTS.md 등)의 편집을 오탐 차단하지 않기 위해서다.
$stdin = [Console]::In.ReadToEnd()
if ($stdin -match '(?i)(File|to):\s*"?(\./)?docs[/\\]design[/\\]') {
    @{ hookSpecificOutput = @{
        hookEventName            = 'PreToolUse'
        permissionDecision       = 'deny'
        permissionDecisionReason = 'docs/design/은 AI 수정 금지 (AGENTS.md 규칙 2). 기획 변경이 필요하면 사용자에게 보고하고, 초안은 docs/design-draft/에 쓴다.'
    } } | ConvertTo-Json -Depth 4
}
exit 0
