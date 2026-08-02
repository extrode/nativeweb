# perf-tail.ps1 -- cleanup after the 2026-08-02 perf tail work
# (pool idle floor, Workstation GC, parallelization experiment, research docs).
# Dry run is the default and always exits 0; -Execute / -e is the only way to act.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute
)

$ErrorActionPreference = 'Stop'

$nativeWeb = 'C:\home\code\extrode-legacy-org.com\NativeWeb'
$manzil    = 'C:\home\code\extrode-legacy-org.com\manzil.my'
$tmp       = "$env:LOCALAPPDATA\Temp\tmp"

# Safety gate: both repos must be on dev with the work merged.
foreach ($repo in @($nativeWeb, $manzil)) {
    $branch = git -C $repo branch --show-current
    if ($branch -ne 'dev') {
        if ($Execute) { Write-Error "$repo is on '$branch', expected 'dev'; refusing." }
        else { Write-Host "NOTE: $repo is on '$branch', expected 'dev' (execute would refuse)." }
    }
}

function Act($desc, [scriptblock]$action) {
    if ($Execute) { Write-Host "DO   $desc"; & $action } else { Write-Host "would $desc" }
}

Write-Host '-- 1. merged branches (git branch -d refuses if unmerged)'
Act "delete manzil branch feat/perf-tail"          { git -C $manzil branch -d feat/perf-tail }
Act "delete NativeWeb branch docs/perf-tail-plan"  { git -C $nativeWeb branch -d docs/perf-tail-plan }

Write-Host '-- 2. review handoff diff (untracked)'
if (Test-Path "$manzil\inline-perf.diff") { Act "delete $manzil\inline-perf.diff" { Remove-Item "$manzil\inline-perf.diff" } }

Write-Host '-- 3. temp artifacts'
if (Test-Path "$tmp\manzil-1vu.js") { Act "delete $tmp\manzil-1vu.js" { Remove-Item "$tmp\manzil-1vu.js" } }

Write-Host '-- remaining state'
git -C $nativeWeb branch --list | Write-Host
git -C $manzil branch --list | Write-Host
if (-not $Execute) { exit 0 }
