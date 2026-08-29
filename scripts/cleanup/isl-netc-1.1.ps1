# Cleanup candidates from the ISL-NETC 1.0 -> 1.1 re-sync.
#
# Bare invocation is the dry run. -Execute (-e) is the only way to act. Nothing here is
# irreversible: the worktree is a detached checkout of a commit already merged into dev, and
# `git worktree add` remakes it; the logs are test output that re-running reproduces.
#
# NOT removed, and not by asking either: the docs/isl-netc-1.1 branch, merged and kept.
#
# Why a worktree again, on 2026-08-30: the same Debug nativeweb.web (pid 7668) still holds
# apps\web\bin, so `dotnet test` in the main tree dies MSB3027/MSB3021. The earlier worktrees
# that routed around it were removed minutes before this branch started, so this one was made
# fresh. Stopping that process is the standing fix and is the user's call, not this script's.
[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$candidates = @(
    @{ Path = Join-Path $repo '.worktrees\isl-netc-1.1'; What = 'detached worktree used to run the unit suite past the pid 7668 file lock' }
    @{ Path = Join-Path $repo 'tmp\lic.log'; What = 'LicenseFilesTests run, 6/6' }
    @{ Path = Join-Path $repo 'tmp\lic-mut.log'; What = 'mutation check: §1.2 reverted to the unfilled [LICENSOR], 1 failed' }
    @{ Path = Join-Path $repo 'tmp\lic-mut2.log'; What = 'mutation check: NOTICE.md URL unpinned, 1 failed' }
    @{ Path = Join-Path $repo 'tmp\unit-full.log'; What = 'full web unit suite, 652/652' }
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
