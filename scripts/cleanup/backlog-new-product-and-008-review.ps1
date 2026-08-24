# backlog-new-product-and-008-review.ps1 -- cleanup after two backlog items
# closed on 2026-08-25: the new-product.ps1 build-output fix and the spec 008
# acceptance review.
#
# Dry run is the default and always exits 0. --execute / -e is the only way to
# act. Nothing here is irreversible past that, so there is no second flag: both
# branches are merged into dev and `git branch -d` refuses anything that is not.
#
# Kept deliberately (do NOT add here):
#   - the wider merged-branch backlog. It refills every time anything merges and
#     has its own recurring script; only this work's two branches are below:
#         pwsh -NoProfile ~/.claude/scripts/cleanup/merged-branch-backlog.ps1
#   - C:\home\code\extrode-legacy-org.com\ciprobe, ciprobe_dev and ciprobe_app. Those belong
#     to the previous body of work and are already in
#     scripts/cleanup/ci-skeleton-and-scaffold.ps1, which has not been run yet.
#     Listing them twice would mean two scripts racing the same delete.
#   - .worktrees/dev and .worktrees/feat-009-signin-consumer-seams. Neither was
#     created by this work, and .worktrees/dev is how these branches were merged.
#   - tmp/ under the repo root. Scratch this work created itself, already gone.

param(
    [Alias('e')][switch]$Execute,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

# PowerShell binds -Execute / -e; accept the POSIX spelling too so the interface
# is identical in both shells -- see ~/.claude/scripts/cleanup/README.md.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute' { $Execute = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$repo = 'C:\home\code\extrode-legacy-org.com\NativeWeb'

# Safety gate: everything below assumes the branches are already merged, which
# means being on dev. A dry run says so and carries on; --execute refuses.
$branch = git -C $repo branch --show-current
if ($branch -ne 'dev') {
    if ($Execute) { Write-Error "$repo is on '$branch', expected 'dev'; refusing." }
    else { Write-Host "NOTE: $repo is on '$branch', expected 'dev' (execute would refuse)." }
}

function Act($desc, [scriptblock]$action) {
    if ($Execute) { Write-Host "DO   $desc"; & $action } else { Write-Host "would $desc" }
}

Write-Host '-- 1. merged branches (git branch -d refuses if unmerged)'
foreach ($b in @(
        'fix/new-product-excludes-build-output',
        'docs/008-spec-status',
        'chore/backlog-cleanup-script')) {
    if (git -C $repo rev-parse --verify --quiet "refs/heads/$b") {
        Act "git branch -d $b" { git -C $repo branch -d $b }
    }
    else {
        Write-Host "  skip $b (no such branch)"
    }
}

Write-Host ''
Write-Host '-- remaining state'
Write-Host "branch:      $(git -C $repo branch --show-current)"
Write-Host "merged refs: $((git -C $repo branch --merged dev | Where-Object { $_ -notmatch '^\*|dev$|main$' }).Count) still present"
