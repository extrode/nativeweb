# 006-cacheable-public-pages.ps1 -- cleanup after spec 006, written at close-out
# on 2026-08-03. Produced by the manzil.my feat/cacheable-public-pages branch,
# the NativeWeb feat/render-script-hashes and chore/spec-006-skeleton-parity
# branches, and the k6 close-out measurement.
#
# Dry run is the default and always exits 0; --execute / -e is the only way to
# act. Dropping the seeded perf rows from manzil_dev needs --delete-seed-data on
# top of --execute (two flags for the irreversible step).
#
# Kept deliberately (do NOT add here):
#   - the stamped skeleton copies were the T10 verification; they are listed
#     below because they are throwaway, but nothing else in $tmp is touched.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    [switch]$DeleteSeedData
)

$ErrorActionPreference = 'Stop'

$nativeWeb = 'C:\home\code\extrode-legacy-org.com\NativeWeb'
$manzil    = 'C:\home\code\extrode-legacy-org.com\manzil.my'
$tmp       = "$env:LOCALAPPDATA\Temp"

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

Write-Host '-- 1. worktrees (remove refuses a dirty tree, which is the behaviour we want)'
$baselineTree = "$manzil\.worktrees\dev-e2e-baseline"
if (Test-Path $baselineTree) {
    Act "git worktree remove $baselineTree" { git -C $manzil worktree remove $baselineTree }
}
Act "git worktree prune (manzil)" { git -C $manzil worktree prune }

Write-Host '-- 2. merged branches (git branch -d refuses if unmerged)'
Act "delete manzil branch feat/cacheable-public-pages" { git -C $manzil branch -d feat/cacheable-public-pages }
Act "delete NativeWeb branch feat/render-script-hashes" { git -C $nativeWeb branch -d feat/render-script-hashes }
Act "delete NativeWeb branch chore/spec-006-skeleton-parity" { git -C $nativeWeb branch -d chore/spec-006-skeleton-parity }

Write-Host '-- 3. throwaway stamped skeletons (T10 verification) and k6 scratch'
foreach ($dir in @("$tmp\skelcheck", "$tmp\skelchk2", "$tmp\skelchk3")) {
    if (Test-Path $dir) { Act "delete $dir" { Remove-Item -Recurse -Force $dir } }
}
foreach ($file in @("$tmp\k6-006.js", "$tmp\k6-diag.js")) {
    if (Test-Path $file) { Act "delete $file" { Remove-Item $file } }
}

Write-Host '-- 4. seeded perf rows in manzil_dev (needs --delete-seed-data as well)'
# The k6 run needed a public profile to measure. The user "perfk6" and its 40
# public bookmarks are still in manzil_dev; bookmarks cascade on the user
# delete. Second flag because this touches a database, not a file.
if ($DeleteSeedData -and $Execute) {
    Write-Host "DO   delete user 'perfk6' (and its 40 bookmarks, ON DELETE CASCADE) from manzil_dev"
    psql -h localhost -U manzil_app -d manzil_dev -c "DELETE FROM users WHERE username = 'perfk6'"
} else {
    Write-Host "would delete user 'perfk6' and its 40 public bookmarks from manzil_dev (--execute --delete-seed-data)"
}

Write-Host '-- remaining state'
git -C $nativeWeb branch --list | Write-Host
git -C $manzil branch --list | Write-Host
git -C $manzil worktree list | Write-Host
if (-not $Execute) { exit 0 }
