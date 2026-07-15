# 클론 후 1회 실행하는 개발 환경 셋업. 재실행해도 안전하다 (멱등).
# 1) .agents/skills(원본) -> .claude/skills 정션 생성 (관리자 권한 불필요)
# 2) git dubious ownership 감지 시 현재 사용자 safe.directory 등록
# 3) git 훅 경로 등록 (scripts/githooks - PRD 커밋/푸시 게이트)
# 4) Codex Windows sandbox 설정과 Claude/Codex 공용 훅 JSON 점검
# 스킬을 추가·삭제하면 재실행한다. Codex 훅은 최초 세션에서 /hooks 로 신뢰 승인이 필요하다.
# -Quiet: 성공 출력 억제. 경고·에러는 그대로 나온다.
param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
function Say($msg) { if (-not $Quiet) { Write-Host $msg } }
function Invoke-GitCapture([string[]]$GitArgs) {
    $output = & git -C $repoRoot @GitArgs 2>&1
    [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output   = @($output)
        Text     = (@($output) -join "`n")
    }
}
function Invoke-RepoGit([string[]]$GitArgs) {
    $result = Invoke-GitCapture $GitArgs
    if ($result.ExitCode -ne 0) {
        throw "git $($GitArgs -join ' ') failed with exit code $($result.ExitCode)`n$($result.Text)"
    }
    $result.Output
}
function Ensure-GitUsable {
    $status = Invoke-GitCapture @('status', '--short')
    if ($status.ExitCode -eq 0) {
        Say "OK    git status"
        return
    }

    if ($status.Text -notmatch 'dubious ownership') {
        throw "git status failed with exit code $($status.ExitCode)`n$($status.Text)"
    }

    Say "FIX   git safe.directory 등록: $repoRootGit"
    $safeDirectories = @(& git config --global --get-all safe.directory 2>$null)
    if ($safeDirectories -notcontains $repoRootGit) {
        & git config --global --add safe.directory $repoRootGit
        if ($LASTEXITCODE -ne 0) {
            throw "git config --global --add safe.directory $repoRootGit failed with exit code $LASTEXITCODE"
        }
    }

    $retry = Invoke-GitCapture @('status', '--short')
    if ($retry.ExitCode -ne 0) {
        throw "git status still failed after safe.directory registration`n$($retry.Text)"
    }
    Say "OK    git status (safe.directory 적용됨)"
}
function Ensure-CodexWindowsSandbox {
    if (-not $isWindowsHost) { return }

    # elevated 샌드박스는 별도 OS 사용자로 명령을 실행해 git이 dubious ownership으로 전부 실패한다.
    # 파일은 버전 관리된다 (클론 즉시 존재해야 첫 세션부터 적용됨). 없으면 체크아웃 이상이므로 재생성해 복구.
    $codexConfig = Join-Path $repoRoot '.codex\config.toml'
    if (-not (Test-Path $codexConfig)) {
        New-Item -ItemType Directory -Force (Split-Path $codexConfig) | Out-Null
        Set-Content -Path $codexConfig -Encoding UTF8 -Value @(
            '# 프로젝트 Codex 기본값 (신뢰 승인된 프로젝트에서만 적용됨).'
            '# Windows elevated 샌드박스는 별도 OS 사용자로 명령을 실행해 git이 dubious ownership으로 전부 실패한다.'
            '[windows]'
            'sandbox = "unelevated"'
        )
        Say "WRITE .codex/config.toml (windows.sandbox = unelevated)"
    } elseif ((Get-Content $codexConfig -Raw) -match '(?m)^\s*sandbox\s*=\s*"unelevated"') {
        Say "OK    .codex/config.toml windows.sandbox = unelevated"
    } else {
        Say "NOTE  .codex/config.toml이 unelevated가 아닙니다. Codex에서 git이 실패하면 이 설정을 확인하세요."
    }
}
function Test-PreToolHookOutput {
    $hookScript = Join-Path $repoRoot 'scripts\hooks\precommit-context.ps1'
    if (-not (Test-Path $hookScript)) {
        throw "필수 훅 스크립트가 없습니다: scripts/hooks/precommit-context.ps1"
    }

    $jsonText = & pwsh -NoProfile -File $hookScript -SelfTest
    if ($LASTEXITCODE -ne 0) {
        throw "precommit-context.ps1 self-test failed with exit code $LASTEXITCODE"
    }

    try {
        $output = $jsonText | ConvertFrom-Json -ErrorAction Stop
    } catch {
        throw "precommit-context.ps1이 유효한 JSON을 반환하지 않았습니다: $jsonText"
    }

    $specific = $output.hookSpecificOutput
    if ($null -eq $specific -or
        $specific.hookEventName -ne 'PreToolUse' -or
        [string]::IsNullOrWhiteSpace($specific.additionalContext) -or
        $specific.PSObject.Properties.Name -contains 'permissionDecision') {
        throw 'precommit-context.ps1 출력은 hookEventName + additionalContext만 포함해야 합니다 (Claude Code / Codex 공통 형식).'
    }

    Say 'OK    Claude Code / Codex PreToolUse JSON'
}

$repoRoot   = (Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path
$repoRootGit = $repoRoot -replace '\\', '/'
$isWindowsHost = $env:OS -eq 'Windows_NT'
$sourceRoot = Join-Path $repoRoot '.agents\skills'
$targetRoot = Join-Path $repoRoot '.claude\skills'
$gitignore  = Join-Path $repoRoot '.gitignore'
$blockStart = '# setup.ps1 정션 대상 (원본: .agents/skills)'

if (-not (Test-Path $sourceRoot)) { Write-Error ".agents/skills 가 없습니다: $sourceRoot" }
New-Item -ItemType Directory -Force $targetRoot | Out-Null

Ensure-GitUsable
Ensure-CodexWindowsSandbox
Test-PreToolHookOutput

foreach ($skill in Get-ChildItem $sourceRoot -Directory) {
    $link = Join-Path $targetRoot $skill.Name

    if (Test-Path $link) {
        $item = Get-Item $link -Force
        if ($item.LinkType -eq 'Junction') {
            Say "OK    .claude/skills/$($skill.Name) (이미 연결됨)"
        } else {
            Write-Warning ".claude/skills/$($skill.Name) 이 일반 폴더로 존재합니다. 원본이 .agents/skills 에 있는지 확인하고 폴더를 지운 뒤 재실행하세요."
            continue
        }
    } else {
        New-Item -ItemType Junction -Path $link -Target $skill.FullName | Out-Null
        Say "LINK  .claude/skills/$($skill.Name) -> .agents/skills/$($skill.Name)"
    }

    $entry = ".claude/skills/$($skill.Name)/"
    $lines = if (Test-Path $gitignore) { @(Get-Content $gitignore) } else { @() }
    if ($lines -notcontains $entry) {
        if ($lines -notcontains $blockStart) { Add-Content $gitignore "`n$blockStart" }
        Add-Content $gitignore $entry
        Say "IGNORE $entry 를 .gitignore에 추가"
    }
}

# 원본이 사라진 정션 정리
foreach ($link in Get-ChildItem $targetRoot -Directory -Force | Where-Object LinkType -eq 'Junction') {
    if (-not (Test-Path (Join-Path $sourceRoot $link.Name))) {
        cmd /c rmdir "$($link.FullName)"
        Say "UNLINK .claude/skills/$($link.Name) (원본 없음)"
    }
}

# 한글 문서 파일명(docs/prd 등)이 git 출력에서 8진수 이스케이프로 깨지면 훅의 경로 검사가 오탐한다
Invoke-RepoGit @('config', 'core.quotepath', 'false') | Out-Null
Say "GIT   core.quotepath = false"

# git 훅 등록 (PRD 커밋 게이트)
Invoke-RepoGit @('config', 'core.hooksPath', 'scripts/githooks') | Out-Null
$hooksPath = (Invoke-RepoGit @('config', '--get', 'core.hooksPath') | Select-Object -First 1)
if ($hooksPath -ne 'scripts/githooks') {
    throw "core.hooksPath 설정 실패: expected scripts/githooks, got '$hooksPath'"
}
Say "HOOKS core.hooksPath = scripts/githooks"

$requiredHooks = @('commit-msg', 'pre-push')
foreach ($hook in $requiredHooks) {
    $hookPath = Join-Path $repoRoot "scripts\githooks\$hook"
    if (-not (Test-Path $hookPath)) {
        throw "필수 git 훅 파일이 없습니다: scripts/githooks/$hook"
    }
    Say "OK    scripts/githooks/$hook"
}

$codexHooks = Join-Path $repoRoot '.codex\hooks.json'
if (Test-Path $codexHooks) {
    Say "OK    .codex/hooks.json"
    Say "NOTE  Codex 사용자는 최초 1회 /hooks 에서 프로젝트 훅을 trust 해야 합니다."
} else {
    Write-Warning ".codex/hooks.json 이 없습니다. Codex PreToolUse/SessionStart 훅은 동작하지 않습니다."
}

Say "DONE  setup.ps1 완료"
