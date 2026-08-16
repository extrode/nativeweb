# jauntyq-probe.ps1 -- cleanup after the 2026-08-02 JauntyQ result-set probe
# (parser harness + generator probe behind docs/jauntyq/2026-08-02-001-result-set-features.md).
# Dry run is the default and always exits 0; -Execute / -e is the only way to act.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

# PowerShell binds -Execute / -e; accept the POSIX spellings too so the
# interface is identical in both shells. Without this it strips one leading
# dash and rejects the very spelling this script's header documents.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute'                   { $Execute = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$ErrorActionPreference = 'Stop'

$nativeWeb = 'C:\home\code\extrode-legacy-org.com\NativeWeb'
$tmp       = "$env:LOCALAPPDATA\Temp\tmp"

# Safety gate: repo must be on dev with the docs branch merged.
$branch = git -C $nativeWeb branch --show-current
if ($branch -ne 'dev') {
    if ($Execute) { Write-Error "$nativeWeb is on '$branch', expected 'dev'; refusing." }
    else { Write-Host "NOTE: $nativeWeb is on '$branch', expected 'dev' (execute would refuse)." }
}

function Act($desc, [scriptblock]$action) {
    if ($Execute) { Write-Host "DO   $desc"; & $action } else { Write-Host "would $desc" }
}

Write-Host '-- 1. merged branch (git branch -d refuses if unmerged)'
if (git -C $nativeWeb branch --list docs/jauntyq-result-set-request) {
    Act "delete NativeWeb branch docs/jauntyq-result-set-request" { git -C $nativeWeb branch -d docs/jauntyq-result-set-request }
} else { Write-Host 'skip branch docs/jauntyq-result-set-request (already deleted)' }

Write-Host '-- 2. scratch probe trees under Temp'
foreach ($dir in @("$tmp\jauntyq-probe", "$tmp\jauntyq-genprobe")) {
    if (Test-Path $dir) { Act "delete $dir" { Remove-Item -Recurse -Force $dir } }
    else { Write-Host "skip $dir (already deleted)" }
}

Write-Host '-- remaining state'
git -C $nativeWeb branch --list 'docs/jauntyq*' | Write-Host
if (-not $Execute) { exit 0 }
