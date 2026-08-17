# jauntyq-0.2.0-pin.ps1 -- cleanup after the 2026-08-17 JauntyQ 0.2.0 pin bump.
#
# Covers two branches. The second, chore/jauntyq-version-skew-note, is the
# 2026-08-17 todo.md entry that diagnosed the skew; it merged without a cleanup
# script of its own, and this is the script for the work that closed it out.
#
#   chore/jauntyq-version-skew-note   recorded the JNT8004 skew in work/todo.md
#   chore/jauntyq-0.2.0-pin           bumped both Directory.Packages.props pins
#                                     to 0.2.0 and rewrote that todo entry
#   chore/jauntyq-pin-cleanup-script  this script
#   docs/local-packages-auth          nuget.config note on local feed auth
#   chore/cleanup-script-auth-branches  added the last two lines to this list
#   fix/ci-solution-restore-and-publish-path  the two CI fixes the first green
#                                     build exposed
#   chore/ci-fix-cleanup-entries      added those two to this list
#   fix/ci-encryption-key-env         the Slice 2 key env vars CI never set
#   chore/env-fix-cleanup-entry       added those two to this list
#   fix/ci-integration-500            the CI first-party application seed and the
#                                     user-secrets/env precedence fix behind it
#   chore/ci-500-cleanup-entries      added those two, and the repro databases
#   fix/demote-multi-source           the Fable review fix to the config-source demotion
#   fix/first-party-application-migration  moved the first-party row into migration 0055
#   chore/ci-green-status             recorded the first green CI run
#   chore/ci-migration-cleanup-entries this list update
#   chore/ci-scratch-entries          scratch files from the review round
#   chore/ci-seed-retirement-script   scripts/cleanup/ci-seed-retirement.ps1
#   chore/final-cleanup-entries       these last four lines
#   chore/retire-ci-seed              removed the ci.yml seed step, rewrote CiSeedTests
#   chore/skeleton-action-pin         SHA-pinned the skeleton's msvc-dev-cmd + ActionPinTests
#   fix/realm-resolver-consistency    503 instead of throw, plus the boot-time row check
#   chore/three-task-cleanup-entries  the previous list update
#   chore/remove-ci-seed-file         git rm of the seed, staged by ci-seed-retirement.ps1
#   chore/list-seed-removal-branch    the previous list update
#   chore/correct-spent-script-note   corrected ci-seed-retirement.ps1's last open item
#   chore/list-final-note-branches    this list update, and the last one
#
# The third is this script's own branch. It can delete itself because the script
# is merged into dev before it is ever run, so the commit survives the branch.
#
# Dry run is the default and always exits 0; --execute / -e is the only way to
# act. Deleting the scratch feeds is irreversible beyond that, so it needs
# --delete-scratch on top of --execute, and dropping the repro databases needs
# --drop-repro-databases on top of --execute. Deleting branches this work did not
# create needs --delete-all-merged, which is section 2b and is not this script's
# original job -- see the paragraph there.
#
# Kept deliberately (do NOT add here):
#   - The jauntyq repo. Nothing here touched it; it was only read.
#   - The jauntyq-github credential added to the per-user NuGet.Config. That is
#     durable configuration, not scratch: it is what makes a local package-mode
#     build possible at all, and nuget.config documents how it was set up.
#   - No worktrees were created by this work, so there is nothing to remove.
#     The prune below is only for a stale registration left by an earlier run.

param(
    [Alias('e')][switch]$Execute,
    [switch]$DeleteScratch,
    [switch]$DropReproDatabases,
    [switch]$DeleteAllMerged,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

# $false is already the default (verified on pwsh 7.6.4, 2026-08-17), so this
# pins current behaviour rather than working around it. When the preference is
# $true, which a caller's profile can set, the Stop above turns every non-zero
# git exit into a terminating error -- and the show-ref probe below exits
# non-zero on the normal path. Pinning it keeps the explicit $LASTEXITCODE
# checks authoritative regardless of whose shell this runs in.
$PSNativeCommandUseErrorActionPreference = $false

# PowerShell binds -Execute / -e and -DeleteScratch; accept the POSIX spellings
# too so the interface is identical in both shells.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute' { $Execute = $true }
        '--delete-scratch' { $DeleteScratch = $true }
        '--drop-repro-databases' { $DropReproDatabases = $true }
        '--delete-all-merged' { $DeleteAllMerged = $true }
        # --delete-scratched is a near-miss for --delete-scratch that has been typed
        # here for real. Exit 2 rather than accepting it: a cleanup script that
        # guesses at what you meant is the last place for a helpful correction.
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$repo = 'C:\home\code\extrode-legacy-org.com\NativeWeb'
$failed = $false

# Safety gate: both branches below must already be merged, which means being on dev.
$branch = git -C $repo branch --show-current
if ($branch -ne 'dev') {
    if ($Execute) { Write-Host "ERROR: $repo is on '$branch', expected 'dev'; refusing." -ForegroundColor Red; exit 1 }
    else { Write-Host "NOTE: $repo is on '$branch', expected 'dev' (execute would refuse)." }
}

function Act($desc, [scriptblock]$action) {
    if ($Execute) { Write-Host "DO   $desc"; & $action } else { Write-Host "would $desc" }
}

Write-Host '-- 1. worktrees'
Act 'git worktree prune' { git -C $repo worktree prune }

Write-Host '-- 2. merged branches (git branch -d refuses if unmerged)'
# A branch already deleted by hand is the desired end state, not a failure:
# report and skip. Only a -d that refuses an unmerged branch is an error.
$thisWorksBranches = @(
        'chore/jauntyq-version-skew-note',
        'chore/jauntyq-0.2.0-pin',
        'chore/jauntyq-pin-cleanup-script',
        'docs/local-packages-auth',
        'chore/cleanup-script-auth-branches',
        'fix/ci-solution-restore-and-publish-path',
        'chore/ci-fix-cleanup-entries',
        'fix/ci-encryption-key-env',
        'chore/env-fix-cleanup-entry',
        'fix/ci-integration-500',
        'chore/ci-500-cleanup-entries',
        'fix/demote-multi-source',
        'fix/first-party-application-migration',
        'chore/ci-green-status',
        'chore/ci-migration-cleanup-entries',
        'chore/ci-scratch-entries',
        'chore/ci-seed-retirement-script',
        'chore/final-cleanup-entries',
        'chore/retire-ci-seed',
        'chore/skeleton-action-pin',
        'fix/realm-resolver-consistency',
        'chore/three-task-cleanup-entries',
        'chore/remove-ci-seed-file',
        'chore/list-seed-removal-branch',
        'chore/correct-spent-script-note',
        'chore/list-final-note-branches')

foreach ($b in $thisWorksBranches) {

    git -C $repo show-ref --verify --quiet "refs/heads/$b"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "skip  branch $b (already gone)"
        continue
    }

    Act "delete branch $b" {
        git -C $repo branch -d $b
        if ($LASTEXITCODE -ne 0) {
            Write-Host "FAILED to delete $b (unmerged?)" -ForegroundColor Red
            $script:failed = $true
        }
    }
}

Write-Host '-- 2b. every other branch already merged into dev (needs --delete-all-merged)'
# Scope widened deliberately, 2026-08-17, on the user's instruction. Sections 1-4
# clean up after one body of work; this section does not, and that is the whole
# reason it needs its own flag and its own paragraph.
#
# The header used to claim it left "~16 older docs/* and chore/*" branches alone.
# Measured 2026-08-17: 98 local branches, ALL merged into dev and NONE unmerged.
# Section 2's named list covers 26, main is excluded, and 71 are left -- against a
# header that claimed ~16. The repo has never had a branch cleanup; sparing 16 was
# never a decision anyone made, it was an estimate nobody counted.
#
# What is actually lost here is names, not commits: every branch below is merged,
# so its history is reachable from dev and `git branch -d` is a pointer deletion.
# But a branch name is sometimes the only surviving statement of why a change was
# made, and unlike sections 3 and 4 this touches work that predates all of it and
# belongs to whoever wrote it. Hence: never in the dry run's action set, never
# under bare --execute, and the full list printed before anything happens so 72
# names can be read rather than counted.
#
# main and dev are excluded by name, and the current branch by HEAD -- git -d
# refuses to delete the branch you are standing on, but failing on it would set
# $failed and exit 1 over something that was never in scope.
$otherMerged = @(git -C $repo branch --merged dev --format='%(refname:short)') |
    Where-Object { $_ -and $_ -notin @('dev', 'main', $branch) -and $_ -notin $thisWorksBranches }

if ($otherMerged.Count -eq 0) {
    Write-Host 'skip  no other merged branches'
}
elseif (-not $DeleteAllMerged) {
    Write-Host "would delete $($otherMerged.Count) other merged branch(es) (with --execute --delete-all-merged):"
    foreach ($b in $otherMerged) { Write-Host "        $b" }
}
else {
    foreach ($b in $otherMerged) {
        Act "delete branch $b" {
            git -C $repo branch -d $b
            if ($LASTEXITCODE -ne 0) {
                Write-Host "FAILED to delete $b (unmerged?)" -ForegroundColor Red
                $script:failed = $true
            }
        }
    }
}

Write-Host '-- 3. scratch verification dirs (needs --delete-scratch as well as --execute)'
# Created while proving 0.2.0 clears JNT8004 without a CI round trip: the nupkgs
# downloaded from the v0.2.0 and v0.1.0 GitHub releases, the throwaway
# nuget.config pair that maps JauntyQ.* at them, and the two --packages dirs the
# probe restores into. None is referenced by the repo; all are reproducible from
# `gh release download`. Nothing here is inside the repo, so none of it can be
# accidentally committed -- which is why deleting it is optional rather than
# required.
#
# The JauntyQ license permits self-hosted-SaaS use of generated output only, and
# forbids redistributing JauntyQ source or binaries. These directories hold
# JauntyQ binaries, so they must not be copied anywhere shared; deleting them is
# the safe direction.
$scratch = @(
    'C:\tmp\jq020feed',
    'C:\tmp\jq010feed',
    'C:\tmp\jq020probe',
    'C:\tmp\jq020pkgs',
    'C:\tmp\jq010pkgs',
    'C:\tmp\feedauth',
    'C:\tmp\pubprobe',
    'C:\tmp\ci.yml.bak',
    'C:\tmp\ci2.bak',
    # From diagnosing the 33 integration failures on run 32014071743.
    'C:\tmp\ci3.bak',
    'C:\tmp\ci3.log',
    'C:\tmp\scg.bak',
    'C:\tmp\itest.log',
    'C:\tmp\mig.err',
    'C:\tmp\nativeweb_dev.cols',
    'C:\tmp\nativeweb_cirepro.cols',
    # From the Fable/GLM review round and the 0055 migration work.
    'C:\tmp\ci500.diff',
    'C:\tmp\scg2.bak',
    'C:\tmp\m55.bak',
    'C:\tmp\dry2.log',
    'C:\tmp\migonly.err',
    'C:\tmp\no55.err',
    'C:\tmp\cl_before.md',
    'C:\tmp\csr.log',
    'C:\tmp\noseed.err',
    'C:\tmp\scg3.bak',
    'C:\tmp\nw.bak',
    'C:\tmp\sk.bak',
    'C:\tmp\ciy.bak',
    (Join-Path $env:LOCALAPPDATA 'Temp\sbomprobe1')
)
foreach ($d in $scratch) {
    if (-not (Test-Path $d)) { Write-Host "skip  $d (already gone)"; continue }
    if ($Execute -and $DeleteScratch) {
        Write-Host "DO   remove $d"
        Remove-Item -LiteralPath $d -Recurse -Force
    }
    elseif ($Execute) {
        Write-Host "keep  $d (pass --delete-scratch to remove)"
    }
    else {
        Write-Host "would remove $d (with --execute --delete-scratch)"
    }
}

Write-Host '-- 4. repro databases (needs --drop-repro-databases as well as --execute)'
# Local Postgres databases built to reproduce CI's schema exactly: migrations
# only (nativeweb_cirepro, nativeweb_cirepro2), migrations + the local dev seed
# (nativeweb_cirepro3, the run that proved the dev seed breaks six
# SessionAuthTests), and migrations + db/seed/ci (nativeweb_cirepro4,
# nativeweb_cifinal, the 258-green runs). All are throwaway; each is rebuilt in
# under a minute by creating the database and replaying db/migrations then
# db/seed/ci.
#
# Its own flag because dropping a database is irreversible and unrecoverable in
# a way deleting a directory of downloaded nupkgs is not. nativeweb_dev is NOT
# in this list and must never be: it is the local development database.
$reproDatabases = @(
    'nativeweb_cirepro',
    'nativeweb_cirepro2',
    'nativeweb_cirepro3',
    'nativeweb_cirepro4',
    'nativeweb_cifinal',
    'nativeweb_noseed',
    # From proving migration 0055 closes the production gap: migrations-only, and the
    # same schema with 0055 withheld (the run that reproduced CI's 33 failures exactly).
    'nativeweb_migonly',
    'nativeweb_no55'
)
foreach ($d in $reproDatabases) {
    if ($Execute -and $DropReproDatabases) {
        Write-Host "DO   drop database $d"
        # -w: never prompt for a password. Without it, a machine whose local auth is not
        # trust/peer for `postgres` stalls the script on an invisible prompt part-way
        # through an irreversible section, with no record of which databases already went.
        # Failing loudly and setting $failed is the only acceptable behaviour here.
        psql -w -h localhost -U postgres -c "DROP DATABASE IF EXISTS $d"
        if ($LASTEXITCODE -ne 0) {
            Write-Host "FAILED to drop $d" -ForegroundColor Red
            $failed = $true
        }
    }
    elseif ($Execute) {
        Write-Host "keep  database $d (pass --drop-repro-databases to drop)"
    }
    else {
        Write-Host "would drop database $d (with --execute --drop-repro-databases)"
    }
}

Write-Host '-- remaining state'
git -C $repo branch --list 'chore/jauntyq*' | Write-Host
git -C $repo worktree list | Write-Host

if (-not $Execute) { exit 0 }
if ($failed) { exit 1 }
