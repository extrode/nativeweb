# 010-documentation-and-tutorials.ps1 -- cleanup after spec 010, the getting-started
# and tutorial documentation work. Written 2026-08-25 from the NativeWeb
# docs/010-documentation-and-tutorials branch.
#
# Everything this work produced under the repo's own tmp/ is absent from this
# script by design: per AGENTS.md that is scratch I created and mine to delete,
# and it has been -- 60 MB clean-clone probe, 658 MB tmp/jauntyq sibling probe,
# 714 MB tmp/probe stamped product. Only the tmp/probe removal is worth naming,
# because an earlier draft of this script listed it and offered a
# -RemoveStampedProduct flag for it. That was wrong twice over: the flag asked
# permission for something already granted, and putting a stamped product under
# tmp/ in the first place is what made it look like it needed one.
#
# What is left is outside tmp/ and therefore needs your hand on it.
#
# Dry run is the default and always exits 0. --execute / -e is the only way to
# act. One thing past that is irreversible and needs its own second flag:
#   -DropDatabases          drops probe_dev, the probe_app role, and the 25
#                           nativeweb_mig* / nativeweb_race* / nativeweb_rec
#                           scratch databases left by earlier migration work
#
# Kept deliberately (do NOT add here):
#   - nativeweb_dev, nativeweb_sample_dev, nativeweb_sample_test and the
#     nativeweb_app role. Ordinary local dev infrastructure, documented in
#     db/sample/README.md; the scaffolding integration and e2e JauntyQ tiers do
#     not run without the sample pair, and docs/getting-started.md Path B tells a
#     reader to create nativeweb_dev by hand. Dropping any of them turns a green
#     local suite red and makes the guide this work just wrote wrong.
#   - the merged-branch backlog. It refills on every merge and has its own
#     recurring script:
#         pwsh -NoProfile ~/.claude/scripts/cleanup/merged-branch-backlog.ps1
#     Only the branch this work produced is listed below.
#   - docs/ itself, obviously, and db/sample/. Committed source.

param(
    [Alias('e')][switch]$Execute,
    [switch]$DropDatabases,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

# rev-parse --verify --quiet and branch -d are both expected to exit non-zero,
# and whether a non-zero native exit throws under 'Stop' has moved between
# PowerShell versions. Pin it, or the handling below silently never runs.
$PSNativeCommandUseErrorActionPreference = $false

# PowerShell binds -Execute / -e; accept the POSIX spelling too so the interface
# is identical in both shells. It will NOT bind a kebab-case multi-word name, so
# the second flags are PascalCase only -- see scripts/cleanup/README.md.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute' { $Execute = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$repo = 'C:\home\code\extrode-legacy-org.com\NativeWeb'
$failed = $false

# Context, reported and NOT refused on -- deliberately, and changed from the
# refusing form on 2026-08-25 after it blocked a legitimate run.
#
# A blanket "clean tree, merged into dev" gate is right for a script that deletes
# code. This one no longer does: the stamped product came out of it, so what is
# left is one `git branch -d` and some scratch databases. `git branch -d` already
# refuses an unmerged branch, and it does so by comparing commits rather than by
# the script comparing strings -- it is a better version of the gate than
# anything written here. Databases hold no unmerged work at all.
#
# So the blanket gate protected nothing and cost something real: this script was
# written ON the 010 branch, so an --execute run could not succeed until 010
# merged, which is to say the script refused every run of its own working life.
# That is the failure mode scripts/cleanup/README.md warns about -- a gate that
# turns the expected case into a refusal.
$branch = git -C $repo branch --show-current
if (git -C $repo status --porcelain) {
    Write-Host "note: $repo has uncommitted changes. Nothing below touches the working tree."
}
git -C $repo merge-base --is-ancestor HEAD dev
if ($LASTEXITCODE -ne 0) {
    Write-Host "note: $repo is on '$branch', not merged into dev. branch -d below will refuse it."
}

function Act($desc, [scriptblock]$action) {
    if ($Execute) { Write-Host "DO   $desc"; & $action } else { Write-Host "would $desc" }
}

Write-Host '-- 1. worktrees'
# This work created none. The prune clears a stale registration if one survived
# an earlier run; it does not touch a live worktree.
Act 'git worktree prune' { git -C $repo worktree prune }

Write-Host '-- 2. merged branches (git branch -d refuses if unmerged)'
foreach ($b in @('docs/010-documentation-and-tutorials')) {
    git -C $repo rev-parse --verify --quiet "refs/heads/$b" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  skip $b (no such branch)"
        continue
    }

    Act "git branch -d $b" {
        git -C $repo branch -d $b
        if ($LASTEXITCODE -ne 0) {
            # Deliberately not naming a cause. `branch -d` refuses for at least
            # two reasons -- unmerged commits, and the branch being checked out
            # in some worktree -- and it prints which on the line above this one.
            # An earlier version asserted "not merged" and was wrong on the first
            # real run: the branch was simply the one standing in the main tree.
            Write-Host '  refused (git said why, above). Left alone.'
            $script:failed = $true
        }
    }
}

Write-Host '-- 3. scratch databases and roles (needs -DropDatabases)'
# probe_dev / probe_app backed the tmp/probe stamped product, which verified
# docs/getting-started.md Path A end to end -- the stamp, the migration, and all
# three of its test tiers. The checkout is gone; these outlived it, and a database
# and a role are not scratch under tmp/, so they are yours to drop rather than
# mine.
#
# The nativeweb_mig* / nativeweb_race* / nativeweb_rec set is older residue, from
# the migration-runner work that produced docs/migration-lock-review.md: one
# throwaway database per lock and per race scenario, none reused, none referenced
# by any test or script. They are listed here rather than left because this is the
# first script since to touch the cluster, and 25 of them is now the bulk of what
# `\l` shows.
if ($DropDatabases) {
    foreach ($db in @('probe_dev')) {
        Act "drop database $db" { psql -U postgres -h 127.0.0.1 -c "DROP DATABASE IF EXISTS $db" }
    }

    # Computed rather than named: the list is long, mechanical, and would go stale
    # the moment one is dropped by hand. The LIKE patterns are narrow enough that
    # nothing in the keep-list above can match -- nativeweb_dev, nativeweb_sample_dev
    # and nativeweb_sample_test share the prefix but not any of the three suffixes.
    $scratch = psql -U postgres -h 127.0.0.1 -Atc @"
SELECT datname FROM pg_database
WHERE datname ~ '^nativeweb_(mig[0-9]+|race[0-9]*|rec)$'
ORDER BY 1
"@
    foreach ($db in $scratch) {
        if ($db) { Act "drop database $db" { psql -U postgres -h 127.0.0.1 -c "DROP DATABASE IF EXISTS $db" } }
    }

    Act 'drop role probe_app' { psql -U postgres -h 127.0.0.1 -c 'DROP ROLE IF EXISTS probe_app' }
}
else {
    Write-Host '  skip probe_dev / probe_app / nativeweb_mig* -- pass -DropDatabases as well'
    $preview = psql -U postgres -h 127.0.0.1 -Atc @"
SELECT count(*) FROM pg_database
WHERE datname ~ '^nativeweb_(mig[0-9]+|race[0-9]*|rec)$'
"@
    Write-Host "       ($preview scratch databases match, plus probe_dev)"
}

Write-Host ''
Write-Host '-- remaining state'
Write-Host "branch:      $(git -C $repo branch --show-current)"
Write-Host "worktrees:   $((git -C $repo worktree list) -join '; ')"
Write-Host "merged refs: $((git -C $repo branch --merged dev | Where-Object { $_ -notmatch '^\*|dev$|main$' }).Count) still present"
Write-Host "databases:   $(psql -U postgres -h 127.0.0.1 -Atc ""select string_agg(datname, ', ' order by datname) from pg_database where datname like 'nativeweb%' or datname like 'probe%'"")"

if ($Execute -and $failed) { exit 1 }
