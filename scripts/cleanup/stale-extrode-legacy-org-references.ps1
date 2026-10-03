# Cleanup candidates from the stale-extrode-legacy-org-references fix.
#
# Bare invocation is the dry run. -Execute (-e) is the only way to act. Nothing here is
# irreversible: the worktree is a detached checkout of a commit already merged into dev,
# and `git worktree add` remakes it.
#
# NOT removed, and not by asking either: the fix/stale-extrode-legacy-org-references branch, merged
# and kept.
[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$worktree = Join-Path $repo '.worktrees\stale-org'

if (-not (Test-Path $worktree)) {
    Write-Host 'Nothing to remove.'
    return
}

# Same lock as the two license branches: a Debug nativeweb.web (pid 7668) holds
# apps\web\bin, so the unit suite cannot build in the main tree. This worktree also holds
# tmp/pack/NativeWeb.Render.0.1.0.nupkg, the pack output used to verify that the produced
# nuspec carries <authors>Extrode LLC</authors>; it goes with the tree.
$size = (Get-ChildItem -LiteralPath $worktree -Recurse -File -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum).Sum
Write-Host ("{0}  ({1:N0} bytes) -- detached worktree: unit suite past a file lock, plus tmp/pack" -f $worktree, $size)

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run. 1 item(s) listed. Re-run with -Execute to remove it.'
    return
}

git -C $repo worktree remove --force $worktree
Write-Host ("Removed {0}" -f $worktree)
