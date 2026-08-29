# Cleanup candidates from the ISL-CLA 1.0 -> 1.1 adoption bump.
#
# Bare invocation is the dry run. -Execute (-e) is the only way to act. Nothing here is
# irreversible: the worktree is a detached checkout of a commit already merged into dev,
# and `git worktree add` remakes it.
#
# NOT removed, and not by asking either: the docs/isl-cla-1-1 branch, merged and kept.
[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$worktree = Join-Path $repo '.worktrees\isl-cla-1-1'

if (-not (Test-Path $worktree)) {
    Write-Host 'Nothing to remove.'
    return
}

# Third branch running into the same lock: a Debug nativeweb.web (pid 7668) holds
# apps\web\bin, so the unit suite cannot build in the main tree. The worktree has its own
# bin/obj, which is both why it worked and why it is worth removing.
$size = (Get-ChildItem -LiteralPath $worktree -Recurse -File -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum).Sum
Write-Host ("{0}  ({1:N0} bytes) -- detached worktree used to run the unit suite past a file lock" -f $worktree, $size)

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run. 1 item(s) listed. Re-run with -Execute to remove it.'
    return
}

git -C $repo worktree remove --force $worktree
Write-Host ("Removed {0}" -f $worktree)
