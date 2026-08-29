# loose-ends.ps1 -- cleanup after the 2026-08-17 loose-ends close-out. Produced
# by two branches: fix/cleanup-script-posix-flags (the --execute binding bug in
# the spec 007 and CSP cleanup scripts) and fix/loose-ends (the legacy
# JavaScript MIME types, plus the same flag shim for the remaining twelve
# scripts and a dry-run retrofit for the two that had none).
#
# Dry run is the default and always exits 0; --execute / -e is the only way to
# act. Nothing here is irreversible beyond deleting merged branches, which
# git branch -d already refuses if they are not merged, so there is no second
# flag.
#
# Kept deliberately (do NOT add here):
#   - tests/nativeweb.render.tests.unit/templates/csp/legacytypes.html. It is
#     the regression fixture for the MIME allowlist, not a probe: deleting it
#     deletes the only thing pinning the six legacy spellings.
#   - the scratch fixtures used to exercise the two retrofitted scripts
#     (%TEMP%\jq-inspect and a repo-root run_test.sh). Both were consumed by
#     the scripts under test during verification and no longer exist.

param(
    [Alias('e')][switch]$Execute,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

# PowerShell binds -Execute / -e; accept the POSIX spelling too so the interface
# is identical in both shells. This script exists partly because that shim was
# missing everywhere else, so it would be a poor joke to omit it here.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute' { $Execute = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$repo = 'C:\home\code\extrode.com\nativeweb'

# Safety gate: both branches must already be merged, which means being on dev.
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

Write-Host '-- 2. merged branches (git branch -d refuses if unmerged)'
foreach ($b in @('fix/cleanup-script-posix-flags', 'fix/loose-ends', 'chore/loose-ends-cleanup-script')) {
    Act "delete branch $b" { git -C $repo branch -d $b }
}

Write-Host '-- remaining state'
git -C $repo branch --list | Write-Host
git -C $repo worktree list | Write-Host
if (-not $Execute) { exit 0 }
