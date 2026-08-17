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
#
# The third is this script's own branch. It can delete itself because the script
# is merged into dev before it is ever run, so the commit survives the branch.
#
# Dry run is the default and always exits 0; --execute / -e is the only way to
# act. Deleting the scratch feeds is irreversible beyond that, so it needs
# --delete-scratch on top of --execute, and dropping the repro databases needs
# --drop-repro-databases on top of --execute.
#
# Kept deliberately (do NOT add here):
#   - The ~16 older docs/* and chore/* branches in `git branch --list`. They
#     predate this work and belong to their own; deleting them is a separate
#     decision.
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
foreach ($b in @(
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
        'chore/ci-500-cleanup-entries')) {

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
    'nativeweb_cifinal'
)
foreach ($d in $reproDatabases) {
    if ($Execute -and $DropReproDatabases) {
        Write-Host "DO   drop database $d"
        psql -h localhost -U postgres -c "DROP DATABASE IF EXISTS $d"
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
