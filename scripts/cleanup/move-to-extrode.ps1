# move-to-extrode.ps1 -- prerequisites for moving this repo from
# C:\home\code\extrode-legacy-org.com\NativeWeb to C:\home\code\extrode.com\NativeWeb,
# written 2026-08-26. The runbook is docs/moving-a-project.md in
# C:\home\code\extrode.com\docs; this script only clears what blocks the move.
#
# Dry run is the default and always exits 0; --execute / -e is the only way to
# act. Deleting the worktree's untracked generated HTML needs
# -DiscardGeneratedHtml on top of --execute (two flags for the irreversible
# step). That deletion is what lets `git worktree remove` succeed WITHOUT
# --force: remove refuses a tree holding untracked files, and forcing it would
# delete the same files while also silencing the refusal for anything else.
#
# Verified 2026-08-26 before this script was written:
#   feat/009-signin-consumer-seams is 0 ahead of dev, 43 behind, and its tip
#   equals `git merge-base` with dev -- fully contained. 0 dirty tracked files,
#   0 stashes in the repo. The 7 untracked files are tools/html/build.js output
#   and regenerate from the .md sources.
#
# Kept deliberately (do NOT add here):
#   - the branch feat/009-signin-consumer-seams. Removing a worktree is not
#     deleting its branch, and the branch is the record of the work.
#   - the 19 files staged in the main tree. Those are stashed for the move and
#     restored after it; they are not this script's business.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    [switch]$DiscardGeneratedHtml,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

# PowerShell binds -Execute / -e; accept the POSIX spellings too so the
# interface is identical in both shells.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute'                 { $Execute = $true }
        '--discard-generated-html'  { $DiscardGeneratedHtml = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$ErrorActionPreference = 'Stop'
# Several checks below EXPECT a non-zero exit (rev-parse --verify on an absent
# ref). Whether that throws instead depends on this, whose default has moved
# between PowerShell versions -- pin it rather than inherit it.
$PSNativeCommandUseErrorActionPreference = $false

# Derived, never hardcoded. This repo is about to change its absolute path, and
# a cleanup script that names the old one stops working the moment it is needed
# (pitfall 10 in the runbook -- 2 files in jaunty did exactly this).
$repo     = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$branch   = 'feat/009-signin-consumer-seams'
$treePath = Join-Path $repo '.worktrees/feat-009-signin-consumer-seams'

$script:failed = $false

function Step {
    param([string]$What, [scriptblock]$Do)
    if ($Execute) { Write-Host "run:   $What"; & $Do }
    else          { Write-Host "would: $What" }
}

Write-Host "repo:  $repo"
if (-not $Execute) { Write-Host "DRY RUN -- nothing will change. Pass -e to act." -ForegroundColor Yellow }
Write-Host ""

# -- gate ------------------------------------------------------------------
# About the CURRENT state, so it stays true after a successful run and is right
# to refuse on. The per-item guards below are the ones that must tolerate
# absence.
$current = git -C $repo branch --show-current
if ($current -ne 'dev') {
    Write-Host "refused: repo is on '$current', expected dev." -ForegroundColor Red
    exit 2
}

# -- 1. the worktree's untracked generated output --------------------------
Write-Host "1. untracked generated output in the worktree"
if (-not (Test-Path $treePath)) {
    Write-Host "   skip:  $treePath no longer exists"
} else {
    $untracked = @(git -C $treePath status --porcelain --untracked-files=all |
                   Where-Object { $_ -match '^\?\?' } |
                   ForEach-Object { $_.Substring(3).Trim() })
    if ($untracked.Count -eq 0) {
        Write-Host "   skip:  nothing untracked"
    } elseif (-not $DiscardGeneratedHtml) {
        Write-Host "   $($untracked.Count) untracked file(s); needs -DiscardGeneratedHtml as well:" -ForegroundColor Yellow
        $untracked | ForEach-Object { Write-Host "     $_" }
        Write-Host '   Without it, git worktree remove below will refuse. That refusal is correct.' -ForegroundColor Yellow
    } else {
        foreach ($u in $untracked) { Write-Host "     $u" }
        Step "git -C $treePath clean -fd" {
            git -C $treePath clean -fd | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Host '   failed: clean refused.' -ForegroundColor Red; $script:failed = $true }
        }
    }
}

# -- 2. the worktree ---------------------------------------------------------
Write-Host ""
Write-Host "2. the linked worktree"
if (-not (Test-Path $treePath)) {
    Write-Host "   skip:  already removed"
} else {
    # No --force. remove refuses a dirty or untracked-holding tree, and that
    # refusal is the behaviour we want: it means section 1 did not finish.
    Step "git -C $repo worktree remove $treePath" {
        git -C $repo worktree remove $treePath
        if ($LASTEXITCODE -ne 0) {
            Write-Host '   refused: tree not clean. Left alone.' -ForegroundColor Red
            $script:failed = $true
        }
    }
}

Write-Host ""
Write-Host "3. prune stale registrations"
# Always safe and always worth running: a registration can outlive its
# directory, and `git worktree list` keeps reporting one that is gone.
Step "git -C $repo worktree prune" { git -C $repo worktree prune | Out-Null }

# -- remaining state ---------------------------------------------------------
Write-Host ""
Write-Host "-- remaining state --"
git -C $repo worktree list
$stillThere = git -C $repo rev-parse --verify --quiet "refs/heads/$branch"
# ${branch}, not $branch -- a colon straight after a variable name is drive
# syntax to the parser and it refuses the file outright.
if ($stillThere) { Write-Host "branch ${branch}: still present, as intended" }
else             { Write-Host "branch ${branch}: GONE -- this script never deletes it" -ForegroundColor Red }

Write-Host ""
if ($Execute -and $script:failed) {
    Write-Host "one or more steps failed; the move is still blocked." -ForegroundColor Red
    exit 1
}
if ($Execute) { Write-Host "done. Re-run the preflight before moving." -ForegroundColor Green }
exit 0
