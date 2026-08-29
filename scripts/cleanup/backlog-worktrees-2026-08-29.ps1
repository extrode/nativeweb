# Cleanup candidates from the 2026-08-29 unsupervised backlog run.
#
# Bare invocation is the dry run. -Execute (-e) is the only way to act. Nothing here is
# irreversible: each is a detached checkout of a commit already merged into dev, and
# `git worktree add` remakes it.
#
# NOT removed, and not by asking either: any of the branches merged in this run. They are
# kept.
#
# Why there are worktrees at all: a running Debug nativeweb.web (pid 7668) holds
# apps\web\bin, so `dotnet test` in the main tree dies MSB3027/MSB3021 on every run. A
# worktree has its own bin/obj and builds past it. Later tasks in the run reuse a single
# tree, `.worktrees\backlog`, re-pointed with `git checkout --detach` rather than one tree
# per branch.
[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$candidates = @(
    'antiforgery-agree'
    'backlog'
) | ForEach-Object { Join-Path $repo ".worktrees\$_" } | Where-Object { Test-Path $_ }

if ($candidates.Count -eq 0) {
    Write-Host 'Nothing to remove.'
    return
}

foreach ($worktree in $candidates) {
    $size = (Get-ChildItem -LiteralPath $worktree -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    Write-Host ("{0}  ({1:N0} bytes) -- detached worktree used to run tests past a file lock" -f $worktree, $size)
}

if (-not $Execute) {
    Write-Host ''
    Write-Host ("Dry run. {0} item(s) listed. Re-run with -Execute to remove them." -f $candidates.Count)
    return
}

foreach ($worktree in $candidates) {
    git -C $repo worktree remove --force $worktree
    Write-Host ("Removed {0}" -f $worktree)
}
