# Cleanup candidates from the 2026-08-30 audit sweep and its fixes.
#
# Bare invocation is the dry run. -Execute (-e) is the only way to act.
#
# Nothing here is irreversible. The worktree is a detached checkout of a commit already
# merged into dev and `git worktree add` remakes it; the tmp/ entries are scratch this
# work produced and nothing tracked references them.
#
# NOT removed, and not by asking either: the chore/backlog-audit-2026-08-30 branch, and
# the tmp/ scratch this work did not create. Three older leftovers -- tmp/shallow-probe,
# tmp/linkprobe and tmp/probe-app -- are listed on work/todo.md as covered by no cleanup
# script; tmp/probe-app IS this work's and is included below, the other two are not.
[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# The worktree exists because a running Debug nativeweb.web (pid 7668) holds apps\web\bin,
# so `dotnet test` in the main tree dies MSB3026 -> MSB3027/MSB3021. A detached worktree
# has its own bin/obj and builds past it. Killing that process is the user's call, and it
# is on work/status/next.md rather than here.
$candidates = @(
    @{ Path = (Join-Path $repo '.worktrees\audit-sweep'); Kind = 'worktree'
       Why  = 'detached worktree: the unit suites, run past the apps\web\bin file lock' }
    @{ Path = (Join-Path $repo 'tmp\audit'); Kind = 'dir'
       Why  = 'sweep.ts and the two lanes evidence tables; the durable record is work/todo.md' }
    @{ Path = (Join-Path $repo 'tmp\probe-app'); Kind = 'dir'
       Why  = 'first stamped product, built to compile the skeleton C# the solution does not' }
    @{ Path = (Join-Path $repo 'tmp\probe-app2'); Kind = 'dir'
       Why  = 're-stamp of the same, after the skeleton edits' }
    @{ Path = (Join-Path $repo 'tmp\lic-live'); Kind = 'dir'
       Why  = 'Deno fetch probe from the licence verification; curl on this box returns 000' }
)

$present = @($candidates | Where-Object { Test-Path -LiteralPath $_.Path })

if ($present.Count -eq 0) {
    Write-Host 'Nothing to remove.'
    return
}

$total = 0
foreach ($item in $present) {
    $size = (Get-ChildItem -LiteralPath $item.Path -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    if ($null -eq $size) { $size = 0 }
    $total += $size
    Write-Host ("{0}  ({1:N0} bytes) -- {2}" -f $item.Path, $size, $item.Why)
}

if (-not $Execute) {
    Write-Host ''
    Write-Host ("Dry run. {0} item(s), {1:N0} bytes. Re-run with -Execute to remove them." -f $present.Count, $total)
    return
}

foreach ($item in $present) {
    if ($item.Kind -eq 'worktree') {
        git -C $repo worktree remove --force $item.Path
    }
    else {
        Remove-Item -LiteralPath $item.Path -Recurse -Force
    }

    Write-Host ("Removed {0}" -f $item.Path)
}

Write-Host ("Removed {0} item(s), {1:N0} bytes." -f $present.Count, $total)
