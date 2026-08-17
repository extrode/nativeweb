# bounded-backlog-run.ps1 -- cleanup after the 2026-08-17 bounded backlog run.
#
# Written 2026-08-17. The user scoped four items before stepping away and each got
# its own branch off dev; all four are merged with --no-ff and unpushed.
#
#   chore/status-roll-ci-closeout   status window roll + the JauntyQ generator flake
#                                   writeup
#   fix/migrate-record-in-transaction
#                                   db/migrate.sh applies and records in one
#                                   transaction, asks the server whether the wrap
#                                   survived, fails loudly on a concurrent apply
#   chore/skeleton-local-mode-ci    the skeleton's CI rebuilt in checkout mode with
#                                   the framework as a SIBLING of the product
#   fix/415-before-antiforgery      the content-type guard moved ahead of antiforgery
#                                   at 17 handlers, plus both test tiers
#
# Dry run is the default and always exits 0. --execute / -e is the only way to act.
# The scratch deletions are irreversible and untracked, so they need the SECOND flag
# --delete-scratch as well; --execute alone touches branches only.
#
# Kept deliberately (do NOT add here):
#   - Every other branch in `git branch --list`. They predate this run and belong to
#     their own work; scripts/cleanup/merged-branch-backlog.ps1 is the recurring
#     drain for those.
#   - C:\tmp\migx, C:\tmp\race*, C:\tmp\ord*, C:\tmp\chain* and the other scratch
#     under C:\tmp. This run did not create them and cannot say what does.
#   - Nothing inside the repository. The run created no untracked file that survived
#     it; `git status` was clean at the merge.
#   - No worktrees were created, so section 1 is a bare prune for a stale
#     registration from some earlier run.

param(
    [Alias('e')][switch]$Execute,
    [switch]$DeleteScratch,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

# $false is already the default (pwsh 7.6.4); pinning it keeps the explicit
# $LASTEXITCODE checks authoritative even when a caller's profile sets it $true,
# which would otherwise turn the show-ref probe's normal non-zero exit into a throw.
$PSNativeCommandUseErrorActionPreference = $false

# PowerShell binds -Execute / -e and -DeleteScratch; accept the POSIX spellings too
# so the interface reads identically in both shells.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute' { $Execute = $true }
        '--delete-scratch' { $DeleteScratch = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$repo = 'C:\home\code\extrode-legacy-org.com\NativeWeb'
$failed = $false

# Safety gate: every branch below must already be merged, which means being on dev.
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
foreach ($b in @(
        'chore/status-roll-ci-closeout',
        'fix/migrate-record-in-transaction',
        'chore/skeleton-local-mode-ci',
        'fix/415-before-antiforgery')) {

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

Write-Host '-- 3. scratch outside the repository (needs --delete-scratch as well)'
# C:\tmp\ciws is the CI-layout reproduction: a product stamped from the skeleton
# plus full NativeWeb and jauntyq clones laid out as siblings, which is how the
# NU1008 nested-CPM failure and the sibling fix were measured. ~260 MB. The clones
# are ordinary checkouts of repositories that still exist; nothing here is a source
# of truth. C:\tmp\skelci was the first stamp attempt and is already gone.
#
# The diff and backup files are byte-for-byte reproducible from git: skel-diff*.txt
# from the skeleton branch's diff, P.bak / 415-diff.txt from
# fix/415-before-antiforgery. DiagTests.cs.bak is the throwaway probe that found the
# vacuous CRLF pass; its finding lives in the test, not the file.
$scratch = @(
    'C:\tmp\ciws',
    'C:\tmp\DiagTests.cs.bak',
    'C:\tmp\skel-diff.txt',
    'C:\tmp\skel-diff2.txt',
    "$env:TEMP\P.bak",
    "$env:TEMP\415-diff.txt"
)

foreach ($p in $scratch) {
    if (-not (Test-Path -LiteralPath $p)) {
        Write-Host "skip  $p (already gone)"
        continue
    }
    if (-not $DeleteScratch) {
        Write-Host "would delete $p  -- pass --delete-scratch to actually remove it"
        continue
    }
    Act "delete $p" { Remove-Item -LiteralPath $p -Recurse -Force }
}

Write-Host '-- remaining state'
git -C $repo branch --list | Write-Host
git -C $repo worktree list | Write-Host
Get-ChildItem -LiteralPath 'C:\tmp' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name | Write-Host

if (-not $Execute) { exit 0 }
if ($failed) { exit 1 }
