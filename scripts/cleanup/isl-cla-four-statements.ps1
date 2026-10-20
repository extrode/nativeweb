# Cleanup candidates from the ISL-CLA four-statements amendment and the epass.io handoff.
#
# Bare invocation is the dry run. -Execute (-e) is the only way to act. Nothing here is
# irreversible: the worktree is a detached checkout of a commit already merged into dev,
# and `git worktree add` remakes it.
#
# NOT removed, and not by asking either: the docs/isl-cla-four-statements and
# docs/epass-io-adoption-handoff branches, both merged and kept.
[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$worktree = Join-Path $repo '.worktrees\isl-cla-verify'

if (-not (Test-Path $worktree)) {
    Write-Host 'Nothing to remove.'
    return
}

# A worktree was needed because a running nativeweb.web (Debug) held apps/web\bin, so the
# unit suite could not build in the main tree. Its own bin/obj are why it is worth removing.
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
