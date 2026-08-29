# ci-skeleton-and-scaffold.ps1 -- cleanup after the JauntyQ scaffolding store and
# the CI work that made the scaffolding and skeleton suites actually run. Written
# at close-out on 2026-08-25, from the NativeWeb feat/scaffold-jauntyq-store,
# feat/ci-skeleton-and-scaffold and fix/web-antiforgery-dedup branches.
#
# Dry run is the default and always exits 0. --execute / -e is the only way to
# act. Two things past that are irreversible and each needs its own second flag:
#   -RemoveStampedProduct   deletes C:\home\code\extrode-legacy-org.com\ciprobe (721 MB of
#                           untracked files, outside this repo)
#   -DropDatabases          drops ciprobe_dev and the ciprobe_app role
#
# Kept deliberately (do NOT add here):
#   - nativeweb_sample_dev and nativeweb_sample_test. These are not scratch: they
#     are the demo's local dev and test databases, documented in db/sample/README.md,
#     and the scaffolding integration and e2e JauntyQ tiers need them to run at
#     all. Dropping them turns a green local suite red until someone re-reads that
#     README. Ordinary dev infrastructure, not the residue of one body of work.
#   - db/sample/. Migrations, queries and the schema snapshot are committed source
#     that the build generates from, not a scratch pull.
#   - the 36-and-counting merged branches in this repo. That backlog refills every
#     time anything merges, so it has its own recurring script and naming them here
#     would duplicate it and go stale the same day:
#         pwsh -NoProfile ~/.claude/scripts/cleanup/merged-branch-backlog.ps1
#     Only the four branches this work produced are listed below.
#   - .worktrees/dev and .worktrees/feat-009-signin-consumer-seams. Neither was
#     created by this work. A concurrent session holds them, and removing a tree
#     out from under a running agent is worse than leaving it.
#   - tmp/ under the repo root. Scratch this work created itself, already removed;
#     it needs no script and no permission.

param(
    [Alias('e')][switch]$Execute,
    [switch]$RemoveStampedProduct,
    [switch]$DropDatabases,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

# PowerShell binds -Execute / -e; accept the POSIX spelling too so the interface
# is identical in both shells. It will NOT bind a kebab-case multi-word name, so
# the second flags are PascalCase only -- see scripts/cleanup/README.md.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute' { $Execute = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$repo = 'C:\home\code\extrode.com\nativeweb'
$stamped = 'C:\home\code\extrode-legacy-org.com\ciprobe'

# Safety gate. "Must be on dev" was the obvious rule and it is the wrong one here:
# .worktrees/dev holds `dev`, git allows a branch in one worktree at a time, so the
# main checkout CANNOT be on dev and an --execute run could never have started.
# Corrected 2026-08-25; see scripts/cleanup/backlog-new-product-and-008-review.ps1,
# which owns removing that worktree.
#
# What the gate protects is that nothing unmerged or uncommitted is destroyed, so
# check that directly: the tree is clean, and HEAD is already merged into dev.
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

Write-Host '-- 1. worktrees'
# This work created none. The prune is here so a stale registration from an
# earlier run does not survive unnoticed; it does not touch a live worktree.
Act 'git worktree prune' { git -C $repo worktree prune }

Write-Host '-- 2. merged branches (git branch -d refuses if unmerged)'
foreach ($b in @(
        'feat/scaffold-jauntyq-store',
        'feat/ci-skeleton-and-scaffold',
        'fix/web-antiforgery-dedup',
        'chore/ci-scaffold-cleanup-script')) {
    if (git -C $repo rev-parse --verify --quiet "refs/heads/$b") {
        Act "git branch -d $b" { git -C $repo branch -d $b }
    }
    else {
        Write-Host "  skip $b (no such branch)"
    }
}

Write-Host '-- 3. the stamped verification product (needs -RemoveStampedProduct)'
# tools/new-product.ps1 stamped this to prove the skeleton-stamp CI job end to
# end before any YAML was written. It is a full checkout with build output, it
# sits OUTSIDE this repo, and nothing tracks it -- so this is unrecoverable, and
# re-creating it is one new-product.ps1 invocation away.
if (Test-Path $stamped) {
    $size = '{0:N0} MB' -f ((Get-ChildItem $stamped -Recurse -File -ErrorAction SilentlyContinue |
                Measure-Object Length -Sum).Sum / 1MB)
    if ($RemoveStampedProduct) {
        Act "remove $stamped ($size)" { Remove-Item $stamped -Recurse -Force }
    }
    else {
        Write-Host "  skip $stamped ($size) -- pass -RemoveStampedProduct as well"
    }
}
else {
    Write-Host "  skip $stamped (not present)"
}

Write-Host '-- 4. the stamped product database and role (needs -DropDatabases)'
# Created by hand to migrate and test the stamped product. Nothing else uses
# them; the sample databases above are a different matter and are not here.
if ($DropDatabases) {
    Act 'drop database ciprobe_dev' {
        psql -U postgres -h 127.0.0.1 -c 'DROP DATABASE IF EXISTS ciprobe_dev'
    }
    Act 'drop role ciprobe_app' {
        psql -U postgres -h 127.0.0.1 -c 'DROP ROLE IF EXISTS ciprobe_app'
    }
}
else {
    Write-Host '  skip ciprobe_dev / ciprobe_app -- pass -DropDatabases as well'
}

Write-Host ''
Write-Host '-- remaining state'
Write-Host "branch:      $(git -C $repo branch --show-current)"
Write-Host "worktrees:   $((git -C $repo worktree list) -join '; ')"
Write-Host "merged refs: $((git -C $repo branch --merged dev | Where-Object { $_ -notmatch '^\*|dev$|main$' }).Count) still present"
Write-Host "stamped:     $(if (Test-Path $stamped) { 'present' } else { 'gone' })"
Write-Host "databases:   $(psql -U postgres -h 127.0.0.1 -Atc ""select string_agg(datname, ', ') from pg_database where datname like 'ciprobe%' or datname like '%sample%'"")"
