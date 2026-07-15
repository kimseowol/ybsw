# PRD 기계 검사: 동기화 검사(docs/workflow/prd.md 3절)의 기계적 절반.
# 위반이 있으면 exit 1 — pre-push 훅이 이 스크립트로 push를 막는다.
# 의미 수준 판정(경계 위반, 의도 모순)은 여기서 하지 않는다 — 그건 동기화 검사에서 에이전트/사람 몫.
$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = (Resolve-Path $scriptRoot).Path
$repoRootGit = $repoRoot -replace '\\', '/'
$violations = @()

# 1. 태그 규약: 파일당 PRD 태그 정확히 1개(상단 3줄), 태그가 가리키는 PRD 문서 실존
$prdDir = Join-Path $repoRoot 'docs/prd'
$prdIds = @(Get-ChildItem $prdDir -Filter 'PRD-*.md' | ForEach-Object { ($_.Name | Select-String 'PRD-\d+').Matches.Value })
$csFiles = Get-ChildItem (Join-Path $repoRoot 'Assets/Scripts') -Recurse -Filter *.cs
foreach ($f in $csFiles) {
    $tags = @((Get-Content $f.FullName -TotalCount 3 | Select-String -Pattern 'PRD-\d+' -AllMatches).Matches.Value | Sort-Object -Unique)
    $rel = [IO.Path]::GetRelativePath($repoRoot, $f.FullName) -replace '\\', '/'
    if ($tags.Count -eq 0)     { $violations += "태그 없음: $rel" }
    elseif ($tags.Count -gt 1) { $violations += "다중 태그($($tags -join ', ')): $rel — 주 소유 PRD 하나만 허용; 독립 why가 있을 때만 PRD 분리" }
    foreach ($tag in $tags) {
        if ($prdIds -notcontains $tag) { $violations += "태그가 없는 PRD를 가리킴: $rel -> $tag" }
    }
}

# 2. PRD frontmatter code: 경로 실존 + updated가 최신 결정 기록보다 뒤처지지 않음
foreach ($doc in Get-ChildItem $prdDir -Filter 'PRD-*.md') {
    $inFm = $false; $inCode = $false
    $docLines = @(Get-Content $doc.FullName)
    $updatedMatch = $docLines | Select-String -Pattern '^updated:\s*(\d{4}-\d{2}-\d{2})$' | Select-Object -First 1
    $updated = if ($updatedMatch) { $updatedMatch.Matches[0].Groups[1].Value } else { $null }
    $decisionDates = @($docLines | Select-String -Pattern '^- (\d{4}-\d{2}-\d{2}) ' | ForEach-Object {
        $_.Matches[0].Groups[1].Value
    })
    foreach ($line in $docLines) {
        if ($line -eq '---') { if (-not $inFm) { $inFm = $true; continue } else { break } }
        if (-not $inFm) { continue }
        if ($line -match '^code:') { $inCode = $true; continue }
        if ($inCode -and $line -match '^\s+-\s+(.+)$') {
            $path = $Matches[1].Trim()
            if (-not (Test-Path (Join-Path $repoRoot $path))) {
                $violations += "code 경로 없음: $($doc.Name) -> $path"
            }
        } elseif ($line -notmatch '^\s') { $inCode = $false }
    }

    $latestDecision = $decisionDates | Sort-Object -Descending | Select-Object -First 1
    if ($latestDecision -and (-not $updated -or $updated -lt $latestDecision)) {
        $violations += "updated 날짜 드리프트: $($doc.Name) -> updated=$updated, 최신 결정=$latestDecision"
    }
}

# 3. INDEX code 경로 실존. 괄호 설명은 제외하고 Assets/... 토큰만 검사한다.
$indexPath = Join-Path $repoRoot 'docs/INDEX.md'
foreach ($line in Get-Content $indexPath) {
    if ($line -notmatch '^\|.*PRD-') { continue }
    foreach ($match in [regex]::Matches($line, 'Assets/[A-Za-z0-9_./-]+')) {
        $path = $match.Value
        if (-not (Test-Path (Join-Path $repoRoot $path))) {
            $violations += "INDEX code 경로 없음: $path"
        }
    }
}

# 4. [prd-skip] 커밋 목록 (정보 제공 — 남용 여부는 동기화 검사에서 판정. 실패해도 검사를 막지 않는다)
$skips = @(& git -c "safe.directory=$repoRootGit" -C $repoRoot log --grep='\[prd-skip\]' --oneline 2>$null)

if ($violations.Count -gt 0) {
    Write-Output "[check-prd] 위반 $($violations.Count)건:"
    $violations | ForEach-Object { Write-Output "  $_" }
} else {
    Write-Output "[check-prd] 기계 검사 통과 (cs $($csFiles.Count)개, PRD $($prdIds.Count)개)"
}
if ($skips.Count -gt 0) {
    Write-Output "[check-prd] [prd-skip] 커밋 $($skips.Count)건 (동기화 검사 시 남용 점검):"
    $skips | ForEach-Object { Write-Output "  $_" }
}

if ($violations.Count -gt 0) { exit 1 }
exit 0
