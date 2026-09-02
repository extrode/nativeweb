<#
.SYNOPSIS
Removes what the spec 021 work (response formats and content negotiation) left behind.

.DESCRIPTION
Written 2026-09-02 at the end of feat/021-response-formats-and-negotiation. Bare invocation is
a dry run: it lists what would go and changes nothing. Pass -Execute (-e) to act.

Candidates, each checked against git at run time rather than asserted here:

  .worktrees/pin-67f42b8   a detached worktree of dev at 67f42b8, created 2026-09-02 22:40 by
                           the review tooling while the branch was under review. Nothing in the
                           branch references it. Removed through `git worktree remove` so git's
                           own bookkeeping goes with it; refused if it holds changes.

  feat/021-response-formats-and-negotiation
                           the branch, once merged into dev. `git branch -d` is the merged check:
                           git compares commits and refuses an unmerged branch on its own.

tmp/probe/021-* is the session's own scratch under the repo's gitignored tmp/ and is not listed.
#>
[CmdletBinding()]
param(
    [Alias('e')]
    [switch]$Execute
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$script:failed = $false

$branch = (git -C $repo rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'dev') {
    Write-Host "refuse: run this from dev, not from '$branch'. Nothing changed."
    exit 1
}

function Step([string]$label, [scriptblock]$act) {
    if ($Execute) {
        Write-Host "run:   $label"
        & $act
    }
    else {
        Write-Host "would: $label"
    }
}

# 1. The pinned worktree
$worktree = Join-Path $repo '.worktrees/pin-67f42b8'
if (-not (Test-Path -LiteralPath $worktree)) {
    Write-Host 'skip:  .worktrees/pin-67f42b8 no longer exists'
}
else {
    Step 'git worktree remove .worktrees/pin-67f42b8' {
        git -C $repo worktree remove $worktree
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  refused: the worktree holds changes. Left alone.'
            $script:failed = $true
        }
    }
}

# 2. The merged branch
$target = 'feat/021-response-formats-and-negotiation'
if (-not (git -C $repo rev-parse --verify --quiet "refs/heads/$target")) {
    Write-Host "skip:  branch $target no longer exists"
}
else {
    Step "git branch -d $target" {
        git -C $repo branch -d $target
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  refused: not merged. Left alone.'
            $script:failed = $true
        }
    }
}

Write-Host ''
Write-Host 'Remaining worktrees:'
git -C $repo worktree list
Write-Host 'Remaining feat/021 branches:'
git -C $repo branch --list 'feat/021-*'

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run: nothing was changed. Pass -Execute to act.'
}
if ($Execute -and $script:failed) { exit 1 }
