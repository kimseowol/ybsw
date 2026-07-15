param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('setup', 'precommit-context', 'deny-design-edit')]
    [string]$Name
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

switch ($Name) {
    'setup' {
        & (Join-Path $repoRoot 'scripts/setup.ps1') -Quiet
        exit $LASTEXITCODE
    }
    'precommit-context' {
        & (Join-Path $repoRoot 'scripts/hooks/precommit-context.ps1')
        exit $LASTEXITCODE
    }
    'deny-design-edit' {
        & (Join-Path $repoRoot 'scripts/hooks/deny-design-edit.ps1')
        exit $LASTEXITCODE
    }
}
