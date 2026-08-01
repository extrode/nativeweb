# architecture-v2.ps1 -- cleanup after spec 003 (architecture v2 + manzil.my
# extraction), written at close-out on 2026-08-01. Produced by the V1-V10
# branch sequence; lists everything that body of work left behind.
#
# Dry run is the default: a bare invocation prints exactly what it would do
# and changes nothing. --execute / -e is the only way to act. Deleting the
# one-shot helper scripts from the tree additionally needs --delete-sources
# on top of --execute (two flags for the irreversible step).
#
# Kept deliberately (do NOT add here):
#   - tools/convert-bookmark-endpoints.py / tools/convert-remaining-endpoints.py:
#     the V4 template-conversion scripts, kept for the IdP's own template
#     migration spec (003-tasks V4 note).
#   - scripts/extract-manzil.ps1: documents how manzil.my was produced.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    [switch]$DeleteSources
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $repoRoot

# Safety gate: branch deletion below assumes everything is merged into dev.
$branch = git branch --show-current
if ($Execute -and $branch -ne 'dev') {
    Write-Error "On '$branch', expected 'dev' -- switch first."
}

$mode = if ($Execute) { 'EXECUTE' } else { 'DRY RUN (pass --execute / -e to act)' }
Write-Host "== spec 003 cleanup [$mode] =="

# -- 1. Merged spec-003 branches (git branch -d refuses anything unmerged) --
$branches = @(
    'chore/style-enforcement', 'feat/render-engine', 'feat/bot-traps',
    'feat/security-baseline', 'feat/perf-ops', 'feat/static-pages',
    'feat/skeleton', 'feat/manzil-extraction', 'chore/remove-devmarks',
    'docs/tick-v5', 'docs/tick-v6', 'docs/tick-v7', 'docs/tick-v8',
    'docs/tick-v9', 'docs/tick-v10', 'chore/spec-003-closeout'
) | Where-Object { git branch --list $_ }
Write-Host "-- 1. delete $($branches.Count) merged branches: $($branches -join ', ')"
if ($Execute) {
    foreach ($b in $branches) {
        git branch -d $b
        if ($LASTEXITCODE -ne 0) { Write-Error "git branch -d $b failed (unmerged?)." }
    }
}

# -- 2. Session scratch under Temp\claude (worker briefs, review output,
#       stamped scratch products, pack-test nupkgs, log tails) --
$scratchRoot = 'C:\Users\user\AppData\Local\Temp\claude'
$scratch = @(
    "$scratchRoot\stamptest",
    "$scratchRoot\nw-packtest", "$scratchRoot\nw-packtest2"
) + (Get-ChildItem -File "$scratchRoot\*.txt" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(skeleton-|v9-|v10-|opus-review|extract-|m2-|m3-|nw-|manzil-|pub-)' } |
        ForEach-Object FullName)
$scratch = $scratch | Where-Object { Test-Path $_ }
Write-Host "-- 2. remove $($scratch.Count) scratch items under Temp\claude:"
$scratch | ForEach-Object { Write-Host "   $_" }
if ($Execute) { $scratch | ForEach-Object { Remove-Item -Recurse -Force $_ } }

# -- 3. One-shot helper scripts (tracked; --delete-sources required).
#       Stages the deletions only: commit them on a chore/ branch yourself,
#       never directly on dev. --
$helpers = @(
    'tools/wire-bot-traps.py', 'tools/wire-bot-traps-web.py',
    'tools/wire-trap-tests.py', 'tools/wire-web-trap-tests.py',
    'tools/rename-session-cookie-tests.py', 'tools/e2e-https.py'
) | Where-Object { Test-Path $_ }
Write-Host "-- 3. git rm $($helpers.Count) one-shot helpers (needs --execute --delete-sources):"
$helpers | ForEach-Object { Write-Host "   $_" }
if ($Execute -and $DeleteSources) {
    foreach ($h in $helpers) {
        git rm -q $h
        if ($LASTEXITCODE -ne 0) { Write-Error "git rm $h failed." }
    }
    Write-Host '   Staged; commit on a chore/ branch and merge with --no-ff.'
}
elseif ($Execute) {
    Write-Host '   Skipped (pass --delete-sources as well to stage the deletions).'
}

if (-not $Execute) {
    Write-Host 'Dry run complete; nothing changed.'
    exit 0
}

Write-Host '== remaining state =='
git branch | Write-Host
git status --short | Write-Host
exit 0
