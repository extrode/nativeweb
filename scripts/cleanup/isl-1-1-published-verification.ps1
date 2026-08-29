# Cleanup candidates from verifying the ISL 1.1 adoptions against the published texts.
#
# Bare invocation is the dry run. -Execute (-e) is the only way to act. Nothing here is
# irreversible: the worktree is a detached checkout of a commit already merged into dev, and
# `git worktree add` remakes it; the fetched licence texts re-download from islamiclicense.org.
#
# NOT removed, and not by asking either: the docs/isl-1-1-published-verification branch, merged
# and kept.
#
# Why a worktree again, on 2026-08-30: the same Debug nativeweb.web (pid 7668) still holds
# apps\web\bin, so `dotnet test` in the main tree dies MSB3027/MSB3021. Stopping that process is
# the standing fix and is the user's call, not this script's.
[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$candidates = @(
    @{ Path = Join-Path $repo '.worktrees\isl-verify'; What = 'detached worktree used to run the unit suite past the pid 7668 file lock' }
    @{ Path = Join-Path $repo 'tmp\lic-live'; What = 'the four fetched licence texts plus fetch.ts, the evidence for the byte-identical diffs' }
    @{ Path = Join-Path $repo 'tmp\CONTRIBUTING.bak'; What = 'backup taken for the mutation check on the forum assertion' }
)

$found = @($candidates | Where-Object { Test-Path $_.Path })

if ($found.Count -eq 0) {
    Write-Host 'Nothing to remove.'
    return
}

foreach ($item in $found) {
    $size = (Get-ChildItem -LiteralPath $item.Path -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    if ($null -eq $size) { $size = (Get-Item -LiteralPath $item.Path).Length }
    Write-Host ("{0}  ({1:N0} bytes) -- {2}" -f $item.Path, $size, $item.What)
}

if (-not $Execute) {
    Write-Host ''
    Write-Host ("Dry run. {0} item(s) listed. Re-run with -Execute to remove them." -f $found.Count)
    return
}

foreach ($item in $found) {
    if ($item.Path -like '*\.worktrees\*') {
        git -C $repo worktree remove --force $item.Path
    }
    else {
        Remove-Item -LiteralPath $item.Path -Recurse -Force
    }

    Write-Host ("Removed {0}" -f $item.Path)
}
