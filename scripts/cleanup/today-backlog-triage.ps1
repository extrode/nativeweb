# today-backlog-triage.ps1 -- cleanup after 2026-08-17's backlog triage.
#
# Replaces scripts/cleanup/deno-first-tooling.ps1, which covered only the first of
# the day's branches and, now that the branch is gone, would have printed git's raw
# "error: branch ... not found" on the way past it. That script is deleted in the
# same commit as this one, so nothing needs to remove it.
#
# Branches produced by the day's work, all merged into dev:
#   chore/deno-first-tooling            AGENTS.md + skeleton/AGENTS.md Deno rule,
#                                       DenoToolingPolicyTests, work/todo.md note on
#                                       the Node 20 action deprecation
#   chore/deno-cleanup-script           the superseded cleanup script
#   docs/config-reference               struck the stale configuration-reference item
#   chore/perf-item-misfiled            recorded the perf follow-up as manzil.my's
#   chore/today-cleanup-consolidation   this script
#
# Dry run is the default and always exits 0; --execute / -e is the only way to act.
#
# Kept deliberately (do NOT add here):
#   - The edit to ~/.claude/AGENTS.md. It lives outside this repo, is not this
#     script's to touch, and nothing here created a copy of it.
#   - The ~50 older merged branches in `git branch --list`. They predate today and
#     belong to their own work; deleting them is a separate decision.
#   - No probe or scratch directories were created by this work.

param(
    [Alias('e')][switch]$Execute,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

# $false is already the default (verified on pwsh 7.6.4, 2026-08-17), so this is not
# working around current behaviour — it pins it. When the preference is $true, which
# a caller's profile can set, the Stop above turns every non-zero git exit into a
# terminating error, and the show-ref probe below deliberately exits non-zero on the
# normal path. Pinning it keeps the explicit $LASTEXITCODE checks authoritative
# regardless of whose shell this runs in.
$PSNativeCommandUseErrorActionPreference = $false

# PowerShell binds -Execute / -e; accept the POSIX spelling too so the interface
# is identical in both shells.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute' { $Execute = $true }
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
# None were created; the prune catches a stale registration from an earlier run.
Act 'git worktree prune' { git -C $repo worktree prune }

Write-Host '-- 2. merged branches (git branch -d refuses if unmerged)'
# Some of these were deleted by hand during the session. A missing branch is the
# desired end state, not a failure: report and skip. Only a -d that refuses an
# unmerged branch is an error worth a non-zero exit.
foreach ($b in @(
        'chore/deno-first-tooling',
        'chore/deno-cleanup-script',
        'docs/config-reference',
        'chore/perf-item-misfiled',
        'chore/today-cleanup-consolidation')) {

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

Write-Host '-- remaining state'
git -C $repo branch --list 'chore/*' | Write-Host
git -C $repo worktree list | Write-Host

if (-not $Execute) { exit 0 }
if ($failed) { exit 1 }
