# PreToolUse 훅 (Claude Code / Codex 공용): git commit 실행 직전, 변경된 코드·에셋과 PRD 매핑을
# 수집해 자가 점검 지시를 컨텍스트로 주입한다. 차단·승인 결정을 반환하지 않는다.
param([switch]$SelfTest)

$ErrorActionPreference = 'SilentlyContinue'

function Write-HookContext([string]$Context) {
    @{ hookSpecificOutput = @{ hookEventName = 'PreToolUse'; additionalContext = $Context } } |
        ConvertTo-Json -Depth 4 -Compress
}

if ($SelfTest) {
    Write-HookContext 'Claude Code / Codex PreToolUse context smoke test'
    exit 0
}

$stdin = [Console]::In.ReadToEnd()
if ($stdin -notmatch 'git\s+commit') { exit 0 }

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$repoRootGit = $repoRoot -replace '\\', '/'
function Invoke-RepoGit {
    & git -c "safe.directory=$repoRootGit" -C $repoRoot @args
}

$null = Invoke-RepoGit rev-parse --verify HEAD 2>$null
$diffArgs = if ($LASTEXITCODE -eq 0) { @('diff', 'HEAD', '--name-only', '--') } else { @('diff', '--cached', '--name-only', '--') }

$changed = @(Invoke-RepoGit @diffArgs 2>$null | Where-Object { $_ })
$csChanged = @($changed | Where-Object { $_ -match '^Assets/Scripts/.*\.cs$' })
$assetChanged = @($changed | Where-Object { $_ -match '^Assets/(Art|Settings|Scenes)/' })
if ($csChanged.Count -eq 0 -and $assetChanged.Count -eq 0) { exit 0 }

$prdChanged = @(Invoke-RepoGit @diffArgs 'docs/prd/*.md' 2>$null | Where-Object { $_ })

$map = foreach ($f in $csChanged) {
    $path = Join-Path $repoRoot $f
    $tag = (Get-Content $path -TotalCount 3 | Select-String -Pattern 'PRD-\d+' | Select-Object -First 1).Matches.Value
    if (-not $tag) { $tag = '태그 없음' }
    "  $f -> $tag"
}

$ctx = @"
[PRD 자가 점검] 커밋 전에 확인하라:
변경된 코드와 연결 PRD:
$(if ($map) { $map -join "`n" } else { '  없음' })
변경된 시각·설정·씬 에셋:
$(if ($assetChanged) { ($assetChanged | ForEach-Object { "  $_" }) -join "`n" } else { '  없음' })
함께 변경된 PRD 문서: $(if ($prdChanged) { $prdChanged -join ', ' } else { '없음' })

1. 기능 의도·경계 또는 외부 동작이 바뀌는가? 아니면 내부 구조만 바뀌는가? 먼저 분류하라.
2. 기능 의도·경계 또는 외부 동작이 바뀌었는데 결정 기록을 아직 추가하지 않았다면 지금 추가하고 함께 스테이징하라.
3. 외부 동작이 같은 내부 리팩토링·주석·포맷이면 PRD를 갱신하지 말고 커밋 메시지에 "[prd-skip] 사유"를 포함하라.
4. 새 PRD는 기존 PRD로 설명할 수 없는 독립 why가 있을 때만 만든다. 여러 파일·PRD를 건드리거나 공유 클래스를 만든다는 사실만으로 생성하지 마라.
5. --no-verify 우회는 금지.
"@

Write-HookContext $ctx
exit 0
