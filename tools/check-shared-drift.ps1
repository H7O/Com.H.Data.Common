#Requires -Version 7.0
<#
.SYNOPSIS
    Detects drift between the shared classes duplicated in Com.H and Com.H.Data.Common.

.DESCRIPTION
    Com.H.Data.Common carries private (internal) copies of several Com.H utility classes so
    that it can ship with no NuGet dependencies. That duplication is deliberate, but it is
    easy to fix a bug in one repo and forget the other. This script catches that.

    It does NOT require the two copies to be identical -- they legitimately differ (access
    modifiers, target-framework guards, and members that only make sense in one library).
    Instead it records an APPROVED baseline hash for each side and reports when one side
    changes without the other. That is precisely the "I forgot to backport" failure mode.

    Comparison is whitespace- and line-wrapping-insensitive: source is stripped of comments,
    using/namespace directives and #region markers, access modifiers are folded to a single
    token, and the remainder is split into statement units. Reformatting a file therefore
    does not raise a false alarm, but changing what it does will.

    Code and documentation are hashed separately, so an XML-doc improvement is reported as
    informational rather than as a build-breaking drift.

.PARAMETER Approve
    Re-baseline the manifest to the current state of both repos. Do this after you have
    verified that the two copies agree.

.PARAMETER Diff
    Show the normalized statement-level differences between the two copies of one shared
    class (by name, e.g. -Diff LinqExtensions).

.PARAMETER ComHRoot
    Path to the Com.H repository. Defaults to the sibling directory ..\Com.H

.PARAMETER DataCommonRoot
    Path to the Com.H.Data.Common repository. Defaults to this script's repository.

.EXAMPLE
    pwsh tools/check-shared-drift.ps1
    Check every shared class and exit non-zero on drift.

.EXAMPLE
    pwsh tools/check-shared-drift.ps1 -Diff DataMapper
    Show what actually differs between the two DataMapper copies.

.EXAMPLE
    pwsh tools/check-shared-drift.ps1 -Approve
    Accept the current state of both repos as the new baseline.
#>
[CmdletBinding(DefaultParameterSetName = 'Check')]
param(
    [Parameter(ParameterSetName = 'Approve')]
    [switch]$Approve,

    [Parameter(ParameterSetName = 'Diff')]
    [string]$Diff,

    [string]$ComHRoot,
    [string]$DataCommonRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

$toolsDir = Split-Path -Parent $PSCommandPath
if (-not $DataCommonRoot) { $DataCommonRoot = Split-Path -Parent $toolsDir }
if (-not $ComHRoot) { $ComHRoot = Join-Path (Split-Path -Parent $DataCommonRoot) 'Com.H' }

$manifestPath = Join-Path $toolsDir 'shared-classes.json'

if (-not (Test-Path $manifestPath)) {
    Write-Host "Manifest not found: $manifestPath" -ForegroundColor Red
    exit 2
}
if (-not (Test-Path $ComHRoot)) {
    Write-Host "Com.H repository not found at: $ComHRoot" -ForegroundColor Red
    Write-Host "Pass -ComHRoot <path> if it lives somewhere else." -ForegroundColor DarkGray
    exit 2
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

# ---------------------------------------------------------------------------
# Normalization
# ---------------------------------------------------------------------------

# Splits C# source into code and comment text. Implemented as a small character
# scanner rather than a regex so that '//' or '/*' appearing inside string and
# character literals (regex patterns in these files contain both) is not mistaken
# for the start of a comment.
function Split-CSharpSource {
    param([string]$Source)

    $code = [System.Text.StringBuilder]::new($Source.Length)
    $docs = [System.Text.StringBuilder]::new()
    $literals = [System.Collections.Generic.List[string]]::new()

    # String and character literals are lifted out and replaced by ~S<n>~ placeholders so
    # that the aggressive whitespace normalization below cannot alter their contents. They
    # are put back before hashing, so a changed message or regex pattern still registers.
    $lift = {
        param([string]$text)
        $literals.Add($text)
        [void]$code.Append("~S$($literals.Count - 1)~")
    }

    $i = 0
    $n = $Source.Length
    while ($i -lt $n) {
        $c = $Source[$i]
        $next = if ($i + 1 -lt $n) { $Source[$i + 1] } else { [char]0 }

        # line comment
        if ($c -eq '/' -and $next -eq '/') {
            $end = $Source.IndexOfAny([char[]]@("`r", "`n"), $i)
            if ($end -lt 0) { $end = $n }
            [void]$docs.Append($Source.Substring($i, $end - $i)).Append("`n")
            $i = $end
            continue
        }
        # block comment
        if ($c -eq '/' -and $next -eq '*') {
            $end = $Source.IndexOf('*/', $i + 2)
            if ($end -lt 0) { $end = $n } else { $end += 2 }
            [void]$docs.Append($Source.Substring($i, $end - $i)).Append("`n")
            $i = $end
            continue
        }
        # verbatim string  @"..."  ("" escapes a quote)
        if ($c -eq '@' -and $next -eq '"') {
            $lit = [System.Text.StringBuilder]::new('@"')
            $i += 2
            while ($i -lt $n) {
                if ($Source[$i] -eq '"') {
                    if ($i + 1 -lt $n -and $Source[$i + 1] -eq '"') { [void]$lit.Append('""'); $i += 2; continue }
                    [void]$lit.Append('"'); $i++; break
                }
                [void]$lit.Append($Source[$i]); $i++
            }
            & $lift $lit.ToString()
            continue
        }
        # regular string literal (covers $"..." because the $ is emitted separately)
        if ($c -eq '"') {
            $lit = [System.Text.StringBuilder]::new('"')
            $i++
            while ($i -lt $n) {
                if ($Source[$i] -eq '\' -and $i + 1 -lt $n) { [void]$lit.Append($Source[$i]).Append($Source[$i + 1]); $i += 2; continue }
                if ($Source[$i] -eq '"') { [void]$lit.Append('"'); $i++; break }
                [void]$lit.Append($Source[$i]); $i++
            }
            & $lift $lit.ToString()
            continue
        }
        # character literal
        if ($c -eq "'") {
            $lit = [System.Text.StringBuilder]::new("'")
            $i++
            while ($i -lt $n) {
                if ($Source[$i] -eq '\' -and $i + 1 -lt $n) { [void]$lit.Append($Source[$i]).Append($Source[$i + 1]); $i += 2; continue }
                if ($Source[$i] -eq "'") { [void]$lit.Append("'"); $i++; break }
                [void]$lit.Append($Source[$i]); $i++
            }
            & $lift $lit.ToString()
            continue
        }

        [void]$code.Append($c)
        $i++
    }

    return [pscustomobject]@{
        Code     = $code.ToString()
        Docs     = $docs.ToString()
        Literals = $literals
    }
}

# Reduces C# source to an order-preserving list of statement units that is insensitive
# to indentation, line wrapping, namespace style and public/internal differences.
function Get-NormalizedUnits {
    param(
        [string]$Code,
        [System.Collections.Generic.List[string]]$Literals
    )

    $t = $Code

    # NOTE: every line-anchored pattern below tolerates a trailing \r -- these files are
    # CRLF, and in .NET multiline mode '$' matches before the \n, leaving the \r in place.

    # directives that carry no meaning for this comparison
    $t = $t -replace '(?m)^[ \t]*#(region|endregion)[^\r\n]*\r?$', ''
    # keep conditional-compilation directives, but terminate them so they split cleanly
    $t = $t -replace '(?m)^[ \t]*(#(?:if|else|elif|endif)[^\r\n]*?)[ \t]*\r?$', '$1;'
    # using directives and namespace declarations (block or file-scoped)
    $t = $t -replace '(?m)^[ \t]*using[ \t]+[^;\r\n]+;[ \t]*\r?$', ''
    $t = $t -replace '(?m)^[ \t]*namespace[ \t]+[^\r\n{;]+;?[ \t]*\r?$', ''
    # the whole point of the duplication: one side is public, the other internal
    $t = $t -replace '\b(public|internal)\b', '<vis>'

    # collapse whitespace, then drop it around punctuation entirely so that differences in
    # operator spacing and line wrapping ("a?b:c" vs "a ? b : c") do not read as drift.
    # Safe because string/char literals are currently placeholders.
    $t = $t -replace '\s+', ' '
    $t = $t -replace '\s*([^\w\s])\s*', '$1'

    $units = $t -split '(?<=[;{}])' |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and $_ -ne '{' -and $_ -ne '}' -and $_ -ne ';' }

    # put the literals back so their contents are part of the comparison
    if ($Literals -and $Literals.Count -gt 0) {
        $units = $units | ForEach-Object {
            [regex]::Replace($_, '~S(\d+)~', {
                param($m)
                $idx = [int]$m.Groups[1].Value
                if ($idx -lt $Literals.Count) { $Literals[$idx] } else { $m.Value }
            })
        }
    }

    return @($units)
}

function Get-TextHash {
    param([string[]]$Units)
    $joined = ($Units -join "`n")
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($joined)
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return -join ($hash | ForEach-Object { $_.ToString('x2') })
}

function Get-FileFingerprint {
    param([string]$Path)

    if (-not (Test-Path $Path)) { return $null }

    $raw = Get-Content $Path -Raw -Encoding UTF8
    if ($null -eq $raw) { $raw = '' }
    $raw = $raw -replace "^﻿", ''

    $split = Split-CSharpSource -Source $raw
    $units = Get-NormalizedUnits -Code $split.Code -Literals $split.Literals
    $docUnits = @(($split.Docs -replace '\s+', ' ').Trim())

    return [pscustomobject]@{
        Units    = $units
        CodeHash = Get-TextHash -Units $units
        DocHash  = Get-TextHash -Units $docUnits
    }
}

# ---------------------------------------------------------------------------
# Gather current state
# ---------------------------------------------------------------------------

$rows = foreach ($entry in $manifest.sharedClasses) {
    $comHPath = Join-Path $ComHRoot $entry.comH
    $dcPath = Join-Path $DataCommonRoot $entry.dataCommon

    $a = Get-FileFingerprint -Path $comHPath
    $b = Get-FileFingerprint -Path $dcPath

    [pscustomobject]@{
        Name     = $entry.name
        Note     = $entry.note
        ComHPath = $comHPath
        DcPath   = $dcPath
        ComH     = $a
        Dc       = $b
        Baseline = $entry
    }
}

# ---------------------------------------------------------------------------
# -Diff
# ---------------------------------------------------------------------------

if ($PSCmdlet.ParameterSetName -eq 'Diff') {
    $row = $rows | Where-Object { $_.Name -eq $Diff } | Select-Object -First 1
    if (-not $row) {
        Write-Host "Unknown shared class '$Diff'. Known names:" -ForegroundColor Red
        $rows | ForEach-Object { Write-Host "  $($_.Name)" -ForegroundColor DarkGray }
        exit 2
    }
    if (-not $row.ComH -or -not $row.Dc) {
        Write-Host "One of the two files is missing; cannot diff." -ForegroundColor Red
        exit 2
    }

    Write-Host ""
    Write-Host "Normalized statement diff for $($row.Name)" -ForegroundColor Cyan
    Write-Host "  <  $($row.ComHPath)" -ForegroundColor DarkGray
    Write-Host "  >  $($row.DcPath)" -ForegroundColor DarkGray
    if ($row.Note) { Write-Host "  note: $($row.Note)" -ForegroundColor DarkGray }
    Write-Host ""

    $delta = Compare-Object -ReferenceObject $row.ComH.Units -DifferenceObject $row.Dc.Units -SyncWindow ([int]::MaxValue)
    if (-not $delta) {
        Write-Host "  The two copies are equivalent after normalization." -ForegroundColor Green
        exit 0
    }
    foreach ($d in $delta) {
        if ($d.SideIndicator -eq '<=') { Write-Host "  <  $($d.InputObject)" -ForegroundColor Yellow }
        else { Write-Host "  >  $($d.InputObject)" -ForegroundColor Magenta }
    }
    Write-Host ""
    Write-Host "  $($delta.Count) differing statement unit(s)." -ForegroundColor DarkGray
    exit 0
}

# ---------------------------------------------------------------------------
# -Approve
# ---------------------------------------------------------------------------

if ($PSCmdlet.ParameterSetName -eq 'Approve') {
    foreach ($row in $rows) {
        if (-not $row.ComH -or -not $row.Dc) {
            Write-Host "Cannot approve '$($row.Name)': a file is missing." -ForegroundColor Red
            exit 2
        }
        $row.Baseline.comHCodeHash = $row.ComH.CodeHash
        $row.Baseline.comHDocHash = $row.ComH.DocHash
        $row.Baseline.dataCommonCodeHash = $row.Dc.CodeHash
        $row.Baseline.dataCommonDocHash = $row.Dc.DocHash
    }
    $manifest.approvedOn = (Get-Date).ToString('yyyy-MM-dd')
    $manifest | ConvertTo-Json -Depth 8 | Set-Content $manifestPath -Encoding UTF8
    Write-Host "Baseline updated for $($rows.Count) shared classes -> $manifestPath" -ForegroundColor Green
    exit 0
}

# ---------------------------------------------------------------------------
# Check
# ---------------------------------------------------------------------------

$drift = @()
$review = @()
$docsOnly = @()
$missing = @()

Write-Host ""
Write-Host "Shared-class drift check" -ForegroundColor Cyan
Write-Host "  Com.H             $ComHRoot" -ForegroundColor DarkGray
Write-Host "  Com.H.Data.Common $DataCommonRoot" -ForegroundColor DarkGray
if ($manifest.PSObject.Properties.Name -contains 'approvedOn') {
    Write-Host "  baseline approved $($manifest.approvedOn)" -ForegroundColor DarkGray
}
Write-Host ""

foreach ($row in $rows) {
    $name = $row.Name.PadRight(22)

    if (-not $row.ComH -or -not $row.Dc) {
        $which = if (-not $row.ComH) { $row.ComHPath } else { $row.DcPath }
        Write-Host "  [MISSING] $name file not found: $which" -ForegroundColor Red
        $missing += $row
        continue
    }

    $comHCodeChanged = $row.ComH.CodeHash -ne $row.Baseline.comHCodeHash
    $dcCodeChanged = $row.Dc.CodeHash -ne $row.Baseline.dataCommonCodeHash
    $comHDocChanged = $row.ComH.DocHash -ne $row.Baseline.comHDocHash
    $dcDocChanged = $row.Dc.DocHash -ne $row.Baseline.dataCommonDocHash

    if ($comHCodeChanged -and -not $dcCodeChanged) {
        Write-Host "  [DRIFT]   $name changed in Com.H only -- backport to Com.H.Data.Common" -ForegroundColor Red
        $drift += $row
    }
    elseif ($dcCodeChanged -and -not $comHCodeChanged) {
        Write-Host "  [DRIFT]   $name changed in Com.H.Data.Common only -- forward-port to Com.H" -ForegroundColor Red
        $drift += $row
    }
    elseif ($comHCodeChanged -and $dcCodeChanged) {
        Write-Host "  [REVIEW]  $name changed on both sides -- confirm they agree, then -Approve" -ForegroundColor Yellow
        $review += $row
    }
    elseif ($comHDocChanged -or $dcDocChanged) {
        $side = if ($comHDocChanged -and $dcDocChanged) { 'both' } elseif ($comHDocChanged) { 'Com.H' } else { 'Com.H.Data.Common' }
        Write-Host "  [DOCS]    $name comments/XML docs changed in $side (code unchanged)" -ForegroundColor DarkYellow
        $docsOnly += $row
    }
    else {
        Write-Host "  [OK]      $name in sync" -ForegroundColor Green
    }
}

Write-Host ""

$problems = $drift.Count + $missing.Count

if ($problems -eq 0 -and $review.Count -eq 0 -and $docsOnly.Count -eq 0) {
    Write-Host "All $($rows.Count) shared classes match their approved baseline." -ForegroundColor Green
    exit 0
}

if ($drift.Count -gt 0) {
    Write-Host "$($drift.Count) shared class(es) drifted. Inspect with:" -ForegroundColor Red
    foreach ($row in $drift) {
        Write-Host "    pwsh tools/check-shared-drift.ps1 -Diff $($row.Name)" -ForegroundColor DarkGray
    }
    Write-Host ""
}
if ($review.Count -gt 0) {
    Write-Host "$($review.Count) changed on both sides -- verify, then run with -Approve." -ForegroundColor Yellow
    Write-Host ""
}
if ($docsOnly.Count -gt 0) {
    Write-Host "$($docsOnly.Count) had documentation-only changes (not a build failure)." -ForegroundColor DarkYellow
    Write-Host "Run with -Approve once you are happy with them." -ForegroundColor DarkGray
    Write-Host ""
}

exit ([int]($problems -gt 0))
