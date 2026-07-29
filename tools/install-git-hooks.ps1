#Requires -Version 7.0
<#
.SYNOPSIS
    Installs a git hook in both repositories that runs the shared-class drift check.

.DESCRIPTION
    The point of the drift check is that it fires without you remembering to run it.
    This installs a hook into Com.H and Com.H.Data.Common that runs it for you.

    By default the hook WARNS and lets the commit through, because the normal workflow
    is to fix one repo, commit, then fix the other -- a blocking hook would reject the
    first of those two commits every time. Use -Blocking if you would rather be stopped
    (a blocked commit can still be forced through with: git commit --no-verify).

.PARAMETER Blocking
    Make the hook reject the commit when drift is detected, instead of warning.

.PARAMETER HookType
    Which git hook to install into. Defaults to pre-commit; pre-push is a reasonable
    alternative if you want the check only at publish time.

.PARAMETER Uninstall
    Remove previously installed hooks.

.EXAMPLE
    pwsh tools/install-git-hooks.ps1

.EXAMPLE
    pwsh tools/install-git-hooks.ps1 -HookType pre-push -Blocking
#>
[CmdletBinding()]
param(
    [switch]$Blocking,
    [ValidateSet('pre-commit', 'pre-push')]
    [string]$HookType = 'pre-commit',
    [switch]$Uninstall,
    [string]$ComHRoot,
    [string]$DataCommonRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolsDir = Split-Path -Parent $PSCommandPath
if (-not $DataCommonRoot) { $DataCommonRoot = Split-Path -Parent $toolsDir }
if (-not $ComHRoot) { $ComHRoot = Join-Path (Split-Path -Parent $DataCommonRoot) 'Com.H' }

$checkScript = (Join-Path $toolsDir 'check-shared-drift.ps1') -replace '\\', '/'

$marker = '# >>> com.h shared-class drift check >>>'

$failLine = if ($Blocking) {
    @'
  echo "  Commit rejected. Fix the drift, or bypass with: git commit --no-verify"
  echo ""
  exit 1
'@
} else {
    @'
  echo "  This is a warning only -- your commit is going through."
  echo "  Remember to mirror the change in the other repository."
  echo ""
'@
}

$hookBody = @"
#!/bin/sh
$marker
# Installed by Com.H.Data.Common/tools/install-git-hooks.ps1
# Remove with: pwsh tools/install-git-hooks.ps1 -Uninstall

SCRIPT="$checkScript"

# Silently skip if the sibling repo or the script is not present (fresh clone, CI, etc.)
[ -f "`$SCRIPT" ] || exit 0
command -v pwsh >/dev/null 2>&1 || exit 0

if ! pwsh -NoProfile -File "`$SCRIPT"; then
$failLine
fi

exit 0
"@

$targets = @(
    @{ Name = 'Com.H'; Root = $ComHRoot },
    @{ Name = 'Com.H.Data.Common'; Root = $DataCommonRoot }
)

foreach ($t in $targets) {
    $gitDir = Join-Path $t.Root '.git'
    if (-not (Test-Path $gitDir)) {
        Write-Host "  skipped $($t.Name) -- not a git repository ($($t.Root))" -ForegroundColor DarkYellow
        continue
    }

    $hooksDir = Join-Path $gitDir 'hooks'
    if (-not (Test-Path $hooksDir)) { New-Item -ItemType Directory -Path $hooksDir | Out-Null }
    $hookPath = Join-Path $hooksDir $HookType

    if ($Uninstall) {
        if ((Test-Path $hookPath) -and ((Get-Content $hookPath -Raw) -like "*$marker*")) {
            Remove-Item $hookPath -Force
            Write-Host "  removed $HookType from $($t.Name)" -ForegroundColor Green
        }
        else {
            Write-Host "  nothing to remove in $($t.Name)" -ForegroundColor DarkGray
        }
        continue
    }

    if (Test-Path $hookPath) {
        $existing = Get-Content $hookPath -Raw
        if ($existing -notlike "*$marker*") {
            Write-Host "  $($t.Name) already has a $HookType hook that is not ours -- leaving it alone." -ForegroundColor Yellow
            Write-Host "    Add this line to it manually if you want the check:" -ForegroundColor DarkGray
            Write-Host "    pwsh -NoProfile -File `"$checkScript`"" -ForegroundColor DarkGray
            continue
        }
    }

    # LF line endings, no BOM -- git runs these through sh
    [System.IO.File]::WriteAllText($hookPath, ($hookBody -replace "`r`n", "`n"), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "  installed $HookType in $($t.Name)" -ForegroundColor Green
}

if (-not $Uninstall) {
    $mode = if ($Blocking) { 'blocking' } else { 'warn-only' }
    Write-Host ""
    Write-Host "Hook mode: $mode. Re-run with -Blocking / without it to change." -ForegroundColor DarkGray
}
