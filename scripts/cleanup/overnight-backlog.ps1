# overnight-backlog.ps1 -- cleanup after the 2026-08-17 overnight backlog run.
# Produced by three branches, merged into dev in this order:
#   fix/idp-correctness-backlog        htmx redirects, bounded e2e publish
#   feat/render-diagnostics-locations  NWT locations, NWT003 types, NWT016
#   chore/test-and-skeleton-hygiene    OpenApi pin removal, budget-script port
#
# Dry run is the default and always exits 0; --execute / -e is the only way to
# act. Nothing here is irreversible beyond deleting merged branches, and
# git branch -d already refuses an unmerged one, so there is no second flag.
#
# Kept deliberately (do NOT add here):
#   - The Release publish output under apps/web/bin/Release. It is the artifact
#     the e2e fixture reuses; deleting it costs the next run a cold NativeAOT
#     link, which measured 15m47s on this machine.
#   - tests/nativeweb.render.tests.unit/templates/csp/*.html. Every one is a
#     regression fixture, including crlf.html, which .gitattributes exempts
#     from LF normalization on purpose.

param(
    [Alias('e')][switch]$Execute,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

# PowerShell binds -Execute / -e; accept the POSIX spelling too so the interface
# is identical in both shells.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute' { $Execute = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$repo = 'C:\home\code\extrode.com\nativeweb'

# Safety gate: all three branches must already be merged, which means being on dev.
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
foreach ($b in @(
        'fix/idp-correctness-backlog',
        'feat/render-diagnostics-locations',
        'chore/test-and-skeleton-hygiene')) {
    Act "delete branch $b" { git -C $repo branch -d $b }
}

Write-Host '-- remaining state'
git -C $repo branch --list | Write-Host
git -C $repo worktree list | Write-Host
if (-not $Execute) { exit 0 }
