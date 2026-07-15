# Figma 기획서(1차 프로토 프레임)와 로컬 기준선의 기계 diff. 절차 원본: docs/workflow/design-sync.md
# 기준선(docs/design-sync/)은 "마지막으로 GD-003·PRD에 전파를 마친 시점"의 원문 덤프다.
# 사용: pwsh scripts/design-diff.ps1              # 현재 Figma vs 기준선 diff (변경 있으면 exit 1)
#       pwsh scripts/design-diff.ps1 -Probe       # 변경 여부만 (1콜, 버전 비교라 어떤 편집이든 잡힘)
#       pwsh scripts/design-diff.ps1 -Update      # 기준선 갱신 (전파 완료 시에만) + 커밋은 사람이
#       pwsh scripts/design-diff.ps1 -ListVersions
#       -AtVersion <id> : diff/Update를 특정 과거 버전 기준으로 (버전 id는 -ListVersions)
param(
    [switch]$Probe,
    [switch]$Update,
    [switch]$ListVersions,
    [string]$AtVersion
)
$ErrorActionPreference = 'Stop'

$fileKey  = 'KvUxCMJv4N1yOy6b2vqD2R'   # RS - 상세기획서 for claude
$frameId  = '2476:396'                  # 1차 프로토 프레임 (기획 진실 원본)
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$syncDir  = Join-Path $repoRoot 'docs\design-sync'
$textName = '1차-프로토.text.md'
$structName = '1차-프로토.structure.txt'
$stateFile = Join-Path $syncDir 'state.json'

function Get-Token {
    if ($env:FIGMA_TOKEN) { return $env:FIGMA_TOKEN }
    $envFile = Join-Path $repoRoot '.env'
    if (Test-Path $envFile) {
        foreach ($line in Get-Content $envFile) {
            if ($line -match '^\s*FIGMA_TOKEN\s*=\s*(\S+)') { return $Matches[1] }
        }
    }
    throw 'FIGMA_TOKEN이 없습니다. 환경 변수 또는 리포 루트 .env에 넣으세요 (.env는 gitignore 대상).'
}

$headers = @{ 'X-Figma-Token' = Get-Token }

function Invoke-Figma([string]$Path) {
    Invoke-RestMethod -Uri ('https://api.figma.com/v1' + $Path) -Headers $headers
}

function Format-Kst($value) {
    ([DateTime]$value).ToUniversalTime().AddHours(9).ToString('yyyy-MM-dd HH:mm') + ' KST'
}

if ($ListVersions) {
    $v = Invoke-Figma "/files/$fileKey/versions?page_size=30"
    foreach ($entry in $v.versions) {
        '{0}  id={1}  {2}' -f (Format-Kst $entry.created_at), $entry.id, $entry.user.handle
    }
    return
}

if ($Probe) {
    $meta = Invoke-Figma ("/files/$fileKey" + '?depth=1')
    "Figma 현재: version=$($meta.version), 수정 $(Format-Kst $meta.lastModified)"
    if (-not (Test-Path $stateFile)) { '기준선 없음 — -Update로 생성하세요.'; exit 1 }
    $state = Get-Content $stateFile -Raw | ConvertFrom-Json
    if ($state.version -eq $meta.version) { "기준선과 동일 버전 — 변경 없음."; exit 0 }
    "기준선(version=$($state.version), $($state.note))과 다름 — diff를 실행하세요."
    exit 1
}

# --- 프레임 원문 fetch + 정규화 ---
$versionQuery = if ($AtVersion) { "&version=$AtVersion" } else { '' }
$resp = Invoke-Figma "/files/$fileKey/nodes?ids=$frameId$versionQuery"
$root = $resp.nodes.$frameId.document
if ($null -eq $root) { throw "프레임 $frameId 를 찾지 못했습니다 (version='$AtVersion')." }

$origin = $root.absoluteBoundingBox
$structLines = [System.Collections.Generic.List[string]]::new()
$textLines = [System.Collections.Generic.List[string]]::new()

function Walk($node) {
    $bb = $node.absoluteBoundingBox
    if ($bb) {
        # 프레임 원점 기준 상대 좌표 — 캔버스에서 프레임 전체를 옮겨도 diff가 더러워지지 않게
        $structLines.Add(('{0}	{1}	{2}	{3},{4} {5}x{6}' -f $node.id, $node.type, $node.name,
            [Math]::Round($bb.x - $origin.x), [Math]::Round($bb.y - $origin.y),
            [Math]::Round($bb.width), [Math]::Round($bb.height)))
    }
    if ($node.type -eq 'TEXT' -and $node.characters) {
        $textLines.Add('[' + $node.id + '] ' + $node.name)
        foreach ($line in ($node.characters -split "`r?`n")) { $textLines.Add('  ' + $line) }
        $textLines.Add('')
    }
    foreach ($child in $node.children) { Walk $child }
}
Walk $root

$textContent = ($textLines -join "`n") + "`n"
$structContent = ($structLines -join "`n") + "`n"

if ($Update) {
    New-Item -ItemType Directory -Force $syncDir | Out-Null
    [IO.File]::WriteAllText((Join-Path $syncDir $textName), $textContent)
    [IO.File]::WriteAllText((Join-Path $syncDir $structName), $structContent)
    $state = [ordered]@{
        version = if ($AtVersion) { $AtVersion } else { $resp.version }
        lastModified = $resp.lastModified
        note = if ($AtVersion) { "과거 버전 $AtVersion 기준" } else { '갱신 당시 최신' }
        updatedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    }
    [IO.File]::WriteAllText($stateFile, (($state | ConvertTo-Json) + "`n"))
    "기준선 갱신 완료: $syncDir (version=$($state.version), 텍스트 노드 수·구조 라인 수: $($textLines.Count)·$($structLines.Count))"
    '전파가 끝난 상태라면 docs/design-sync 변경을 커밋하세요.'
    return
}

# --- diff (기본 동작) ---
if (-not (Test-Path (Join-Path $syncDir $textName))) { '기준선 없음 — 먼저 -Update로 생성하세요.'; exit 1 }
$tmpDir = Join-Path ([IO.Path]::GetTempPath()) 'design-sync-current'
New-Item -ItemType Directory -Force $tmpDir | Out-Null
[IO.File]::WriteAllText((Join-Path $tmpDir $textName), $textContent)
[IO.File]::WriteAllText((Join-Path $tmpDir $structName), $structContent)

$changed = $false
foreach ($name in @($textName, $structName)) {
    & git -c core.autocrlf=false --no-pager diff --no-index -- (Join-Path $syncDir $name) (Join-Path $tmpDir $name)
    if ($LASTEXITCODE -eq 1) { $changed = $true }
    elseif ($LASTEXITCODE -gt 1) { throw "git diff 실패 (exit $LASTEXITCODE)" }
}

if ($changed) {
    ''
    '변경이 있습니다. 해석·전파(GD-003 동기화, PRD 드리프트 검사) 후 -Update로 기준선을 갱신하세요.'
    exit 1
}
'기준선과 차이 없음.'
exit 0
