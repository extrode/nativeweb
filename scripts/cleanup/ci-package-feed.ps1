# ci-package-feed.ps1 -- cleanup after the CI package-feed fix, 2026-08-17.
# Produced by one branch, merged into dev:
#   fix/ci-package-feed   nuget.config + GitHub Packages auth + CI on dev/main
#
# Dry run is the default and always exits 0; --execute / -e is the only way to
# act. Deleting the probe directories is irreversible in the sense that they
# have to be re-downloaded, so it sits behind its own second flag.
#
# Kept deliberately (do NOT add here):
#   - ~/.nuget/packages/jauntyq.*. Tempting, because emptying it is how you
#     reproduce the original bug. But it is the shared global cache, so clearing
#     it slows every restore on this machine, and the isolated --packages probe
#     below reproduces the same thing without touching it.
#   - The Release publish output under apps/web/bin/Release. The e2e fixture
#     reuses it; a cold NativeAOT relink measured 15m47s here.

param(
    [Alias('e')][switch]$Execute,
    [switch]$DeleteProbeDirs,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

# PowerShell binds -Execute / -e; accept the POSIX spelling too so the interface
# is identical in both shells.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute' { $Execute = $true }
        '--delete-probe-dirs' { $DeleteProbeDirs = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$repo = 'C:\home\code\extrode-legacy-org.com\NativeWeb'

# Safety gate: the branch below must already be merged, which means being on dev.
$branch = git -C $repo branch --show-current
if ($branch -ne 'dev') {
    if ($Execute) { Write-Error "$repo is on '$branch', expected 'dev'; refusing." }
    else { Write-Host "NOTE: $repo is on '$branch', expected 'dev' (execute would refuse)." }
}

function Act($desc, [scriptblock]$action) {
    if ($Execute) { Write-Host "DO   $desc"; & $action } else { Write-Host "would $desc" }
}

Write-Host '-- 1. worktrees'
# None were created; the prune catches a stale registration from an earlier run.
Act 'git worktree prune' { git -C $repo worktree prune }

Write-Host '-- 2. merged branch (git branch -d refuses if unmerged)'
Act 'delete branch fix/ci-package-feed' { git -C $repo branch -d 'fix/ci-package-feed' }

Write-Host '-- 3. restore probe directories'
# Written by the isolated `dotnet restore --packages` runs that reproduced
# NU1101 and then the 401. Each is a partial package cache, useful only while
# diagnosing; they are re-created on demand.
$probes = @("$env:TEMP\nw-feed-probe", "$env:TEMP\nw-feed-probe2")
foreach ($p in $probes) {
    if (-not (Test-Path $p)) { Write-Host "  (absent) $p"; continue }
    if ($DeleteProbeDirs) { Act "remove $p" { Remove-Item -Recurse -Force $p } }
    else { Write-Host "  would remove $p (needs --delete-probe-dirs as well)" }
}

Write-Host '-- remaining state'
git -C $repo branch --list 'fix/*' | Write-Host
git -C $repo worktree list | Write-Host
if (-not $Execute) { exit 0 }
