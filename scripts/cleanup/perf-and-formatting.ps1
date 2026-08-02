# perf-and-formatting.ps1 -- cleanup after the 2026-08-02 perf/formatting body of work
# (memory soak, paged-list SQL fix, formatter conditional-partial fix, before/after perf runs).
# Dry run is the default and always exits 0; -Execute / -e is the only way to act.
# Killing the perf server process needs -StopPerfInstance on top of -Execute.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    # Second flag for the only irreversible-ish action beyond file/branch removal:
    # stopping the manzil perf instance listening on :5346. The UI instance on
    # :5345 is deliberately never touched (the user may be browsing it).
    [switch]$StopPerfInstance
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
Act "delete NativeWeb branch fix/formatter-conditional-partial" { git -C $nativeWeb branch -d fix/formatter-conditional-partial }
Act "delete manzil branch fix/paged-list-aggregation"           { git -C $manzil branch -d fix/paged-list-aggregation }
Act "delete manzil branch fix/formatted-html"                   { git -C $manzil branch -d fix/formatted-html }

Write-Host '-- 2. scratch files in repos'
Act "delete $manzil\paged-list.diff (review handoff, untracked)" { Remove-Item "$manzil\paged-list.diff" }

Write-Host '-- 3. temp artifacts (emitted-generator dirs, backups, k6 scripts/logs, stamped probe)'
$tempItems = @(
    (Get-ChildItem -Directory "$tmp" -Filter 'manzil-gen*' -ErrorAction SilentlyContinue | ForEach-Object FullName)
    "$tmp\skelprobe"
    "$tmp\skelprobe-gen"
    "$tmp\templates-current"
    "$tmp\manzil-perf.js"
    "$tmp\manzil-soak.js"
    "$tmp\manzil-soak-mem.log"
) | Where-Object { $_ -and (Test-Path $_) }
foreach ($item in $tempItems) {
    Act "delete $item" { Remove-Item -Recurse -Force $item }
}

Write-Host '-- 4. perf seed data in manzil_dev (user sy: 1000 example.com bookmarks, perf-tag-* tags)'
$sql = @"
DELETE FROM bookmark_tags bt USING bookmarks b
  WHERE bt.bookmark_id = b.id AND b.url LIKE 'https://example.com/article/%';
DELETE FROM bookmarks WHERE url LIKE 'https://example.com/article/%';
DELETE FROM tags WHERE name LIKE 'perf-tag-%';
"@
if ($Execute) { Write-Host 'DO   delete perf seed rows from manzil_dev'; $sql | psql -d manzil_dev -v ON_ERROR_STOP=1 }
else { Write-Host 'would delete perf seed rows from manzil_dev (example.com bookmarks, perf-tag-* tags, their links)' }

Write-Host '-- 5. perf server instance (:5346) -- needs -StopPerfInstance'
$perfConn = Get-NetTCPConnection -LocalPort 5346 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($perfConn) {
    if ($Execute -and $StopPerfInstance) { Write-Host "DO   stop manzil perf instance PID $($perfConn.OwningProcess)"; Stop-Process -Id $perfConn.OwningProcess -Force }
    elseif ($Execute) { Write-Host "SKIP perf instance PID $($perfConn.OwningProcess) still listening on :5346 (pass -StopPerfInstance)" }
    else { Write-Host "would stop manzil perf instance PID $($perfConn.OwningProcess) on :5346 (only with -StopPerfInstance)" }
} else {
    Write-Host 'no listener on :5346'
}

Write-Host '-- remaining state'
git -C $nativeWeb branch --list | Write-Host
git -C $manzil branch --list | Write-Host
if (-not $Execute) { exit 0 }
