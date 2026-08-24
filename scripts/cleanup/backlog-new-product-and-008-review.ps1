# backlog-new-product-and-008-review.ps1 -- cleanup after two backlog items
# closed on 2026-08-25: the new-product.ps1 build-output fix and the spec 008
# acceptance review.
#
# Dry run is the default and always exits 0. --execute / -e is the only way to
# act. One thing past that is not merely a delete and takes its own second flag:
#   -RemoveDevWorktree      removes .worktrees/dev, which is what pins `dev` out
#                           of the main checkout (see the gate note below)
# The branch deletions need no second flag: all are merged into dev, and
# `git branch -d` refuses anything that is not.
#
# Kept deliberately (do NOT add here):
#   - the wider merged-branch backlog. It refills every time anything merges and
#     has its own recurring script; only this work's branches are below:
#         pwsh -NoProfile ~/.claude/scripts/cleanup/merged-branch-backlog.ps1
#   - C:\home\code\extrode-legacy-org.com\ciprobe, ciprobe_dev and ciprobe_app. Those belong
#     to the previous body of work and are already in
#     scripts/cleanup/ci-skeleton-and-scaffold.ps1, which has not been run yet.
#     Listing them twice would mean two scripts racing the same delete.
#   - .worktrees/feat-009-signin-consumer-seams. Not created by this work, and as
#     of 2026-08-25 it holds untracked docs/assets/ and docs/index.html -- so a
#     session is still using it, and removing a tree out from under a running
#     agent is worse than leaving it. Its branch IS merged; the tree is not ours.
#   - tmp/ under the repo root. Scratch this work created itself, already gone.

param(
    [Alias('e')][switch]$Execute,
    [switch]$RemoveDevWorktree,
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

# Safety gate. "Must be on dev" was the obvious rule and it is the wrong one HERE:
# .worktrees/dev holds `dev`, git allows a branch in one worktree at a time, so
# the main checkout CANNOT be on dev and an --execute run could never start. That
# is also the thing -RemoveDevWorktree below exists to undo, which would have made
# the gate refuse the only action that clears it.
#
# What the gate was actually protecting is that nothing unmerged or uncommitted is
# destroyed, so check that directly: the tree is clean, and HEAD is already merged
# into dev. `git branch -d` re-checks the merge for each branch anyway.
$branch = git -C $repo branch --show-current
if (git -C $repo status --porcelain) {
    if ($Execute) { Write-Error "$repo has uncommitted changes; refusing." }
    else { Write-Host "NOTE: $repo has uncommitted changes (execute would refuse)." }
}
git -C $repo merge-base --is-ancestor HEAD dev
if ($LASTEXITCODE -ne 0) {
    if ($Execute) { Write-Error "$repo is on '$branch', which is not merged into dev; refusing." }
    else { Write-Host "NOTE: $repo is on '$branch', not merged into dev (execute would refuse)." }
}

function Act($desc, [scriptblock]$action) {
    if ($Execute) { Write-Host "DO   $desc"; & $action } else { Write-Host "would $desc" }
}

Write-Host '-- 1. the dev worktree (needs -RemoveDevWorktree)'
# .worktrees/dev was created by a concurrent session on 2026-08-25 and is what
# pins `dev` out of the main checkout, so every merge since has had to go through
# `git -C .worktrees/dev merge --no-ff` and the main tree has been left sitting on
# whichever branch merged last. Removing it puts `git switch dev` back.
# Verified clean before this was written -- `git -C .worktrees/dev status
# --porcelain` was empty -- and re-checked below, because "clean when I looked" is
# not "clean when you run it".
$devTree = Join-Path $repo '.worktrees\dev'
if (Test-Path $devTree) {
    $dirty = git -C $devTree status --porcelain
    if ($dirty) {
        Write-Host "  skip $devTree -- it now has uncommitted or untracked files:"
        $dirty | ForEach-Object { Write-Host "    $_" }
    }
    elseif ($RemoveDevWorktree) {
        Act "git worktree remove .worktrees/dev (then: git -C $repo switch dev)" {
            git -C $repo worktree remove .worktrees/dev
        }
    }
    else {
        Write-Host "  skip $devTree (clean) -- pass -RemoveDevWorktree as well"
    }
}
else {
    Write-Host "  skip $devTree (not present)"
}

Write-Host '-- 2. merged branches (git branch -d refuses if unmerged)'
foreach ($b in @(
        'fix/new-product-excludes-build-output',
        'docs/008-spec-status',
        'chore/backlog-cleanup-script',
        'chore/cleanup-script-gate')) {
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
