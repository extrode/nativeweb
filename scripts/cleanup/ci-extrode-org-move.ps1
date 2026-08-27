# ci-extrode-org-move.ps1 -- what the 2026-08-27 CI repair left behind.
#
# Two failures, two branches, both merged into dev:
#
#   fix/ci-stamp-git-identity (8ae2238) -- skeleton-stamp died at its first real
#     step because a GitHub runner has no git identity and new-product.ps1 ends
#     by committing the stamped product.
#   fix/ci-extrode-package-feed (3233f15) -- test and aot-publish died with
#     NU1301 403 because JauntyQ.* is still owned by the extrode-legacy-org org while this
#     repo moved to extrode.
#
# Also removes the jauntyq worktree created to verify the 0.3.0 -> 0.4.0 pin
# bump. It lives in the JAUNTYQ repo, not this one: the first attempt put it
# under nativeweb/.worktrees and every pack failed NU1008, because
# Directory.Packages.props discovery walked up out of the jauntyq tree and found
# nativeweb's copy -- the trap ci.yml's checkout comment describes, with the
# roles reversed. It is detached at v0.4.0 and holds Release build output.
#
# NOT LISTED, and deliberately: everything under this repo's own tmp/. That
# directory holds scratch from many sessions, not just this one -- 743 MB of it
# is tmp/nuget-pkgs, the isolated NUGET_PACKAGES used for the package-mode
# verification, and tmp/local-feed holds the 0.4.0 nupkgs that were packed into
# it. Both are mine and safe to delete; the rest of tmp/ is not mine to judge,
# so this script touches none of it and leaves the whole directory to you.
#
# Removing a worktree is gated separately: `git worktree remove` refuses a dirty
# tree, which is the behaviour you want, but it also discards an untracked file
# nobody has looked at.

param(
    [Alias('e')][switch]$Execute,
    [switch]$DropWorktree,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'
# Several checks below expect a non-zero exit rather than a throw; see
# ~/.claude/scripts/cleanup/README.md on why this is pinned instead of inherited.
$PSNativeCommandUseErrorActionPreference = $false

foreach ($a in $Rest) {
    switch ($a) {
        '--execute'        { $Execute = $true }
        '--drop-worktree'  { $DropWorktree = $true }
        default            { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$repo    = 'C:\home\code\extrode.com\nativeweb'
$jauntyq = 'C:\home\code\extrode.com\jauntyq'
$tree    = Join-Path $jauntyq '.worktrees\jauntyq-v040'
$failed  = $false

# Gate on the current state, not on whether this script has already run. Being on
# dev is what says the branches below are merged; it does not become false by
# succeeding.
$branch = git -C $repo branch --show-current
if ($branch -ne 'dev') {
    if ($Execute) { Write-Error "$repo is on '$branch', expected 'dev'; refusing." }
    else { Write-Host "NOTE: $repo is on '$branch', expected 'dev' (execute would refuse)." }
}

function Act($desc, [scriptblock]$action) {
    if ($Execute) { Write-Host "DO   $desc"; & $action } else { Write-Host "would $desc" }
}

Write-Host '-- 1. the jauntyq verification worktree'
if (-not (Test-Path -LiteralPath $tree)) {
    Write-Host "  skip:  $tree no longer exists"
} elseif (-not $DropWorktree) {
    Write-Host "  found $tree"
    Write-Host '  would remove it (needs --drop-worktree / -DropWorktree as well)'
} else {
    # Never rm -rf a worktree: that leaves the registration behind and
    # `git worktree list` keeps reporting a directory that is gone.
    Act "git worktree remove $tree" {
        git -C $jauntyq worktree remove $tree
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  refused (dirty tree?). Left alone.'
            $script:failed = $true
        } else {
            git -C $jauntyq worktree prune
        }
    }
}

Write-Host '-- 2. merged branches (git branch -d refuses if unmerged)'
foreach ($target in @('fix/ci-stamp-git-identity', 'fix/ci-extrode-package-feed')) {
    git -C $repo rev-parse --verify --quiet "refs/heads/$target" > $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  skip:  branch $target no longer exists"
        continue
    }

    Act "git branch -d $target" {
        git -C $repo branch -d $target
        if ($LASTEXITCODE -ne 0) { Write-Host '  refused: not merged. Left alone.'; $script:failed = $true }
    }
}

Write-Host '-- remaining state'
git -C $jauntyq worktree list | Write-Host
git -C $repo branch --list 'fix/*' | Write-Host

if ($Execute -and $failed) { exit 1 }
exit 0
