<#
.SYNOPSIS
Removes what the xunit.v3 upgrade left behind.

.DESCRIPTION
Written 2026-09-05 at the end of chore/xunit-v3. Bare invocation is a dry run: it lists what
would go and changes nothing. Pass -Execute (-e) to act.

Candidates, each checked at run time rather than asserted here:

  tmp/xunit3-probe, tmp/vstest-probe
                           The two throwaway projects that established what xunit.v3 4.0.0 does
                           on the .NET 10 SDK: that it is Microsoft.Testing.Platform, that VSTest
                           is not an available option, and what the new CLI accepts. Each holds a
                           restored package cache. Their findings are in
                           docs/plans/2026-09-05-010-xunit-v3.md, so the directories carry nothing
                           that is not written down.

  tmp/build-log.txt        A captured build, kept only long enough to count 938 xUnit1051 errors.

  tmp/stamp-xunit3*        Stamped products from tools/stamp-check.ts, one per run, each a full
                           checkout with its own build output. The first two are failed runs kept
                           for the same reason the third is: nothing here reads them again.

  .worktrees/xunit-baseline
                           A checkout of dev used to prove the four nativeweb.signin.tests.e2e
                           failures predate this branch, under the old xunit 2.9.3 / VSTest stack.
                           Removed with `git worktree remove`, not rm, so git's own bookkeeping
                           goes with it.

  the chore/xunit-v3 branch itself
                           Only once it is an ancestor of dev.

Not listed, and deliberately:

  tests/Directory.Build.props, skeleton/tests/Directory.Build.props
                           New files, and the xUnit1051 suppression lives in them. They are the
                           deliverable.
#>
[CmdletBinding()]
param(
    [Alias('e')]
    [switch]$Execute
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

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

# @() is required, not defensive habit: Set-StrictMode -Version Latest makes .Count on a
# bare string throw rather than answer 1.
$scratch = @()
foreach ($name in @('tmp/xunit3-probe', 'tmp/vstest-probe', 'tmp/build-log.txt')) {
    $path = Join-Path $repo $name
    if (Test-Path $path) { $scratch += @(Get-Item -Path $path) }
}

$stampRoot = Join-Path $repo 'tmp'
if (Test-Path $stampRoot) {
    $scratch += @(Get-ChildItem -Path $stampRoot -Directory -Filter 'stamp-xunit3*')
}

if ($scratch.Count -eq 0) {
    Write-Host 'skip:  no xunit.v3 scratch under tmp/'
}

foreach ($item in $scratch) {
    $mb = [int]((Get-ChildItem -Path $item.FullName -File -Recurse -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum / 1MB)

    Step "remove $($item.FullName) ($mb MB)" {
        Remove-Item -Path $item.FullName -Recurse -Force
    }
}

Write-Host ''

$worktree = Join-Path $repo '.worktrees/xunit-baseline'
if (Test-Path $worktree) {
    Step "git worktree remove $worktree" {
        git -C $repo worktree remove --force $worktree
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  refused: git would not remove it. Left alone.'
            $script:failed = $true
        }
    }
}
else {
    Write-Host 'skip:  .worktrees/xunit-baseline is gone'
}

Write-Host ''

if (git -C $repo rev-parse --verify --quiet 'refs/heads/chore/xunit-v3') {
    # `git branch -d` is NOT the merged check it looks like: git accepts a branch merged
    # to its own upstream, so a pushed branch whose commits never reached dev deletes
    # with a warning and exit 0. merge-base asks the question the script means to ask.
    git -C $repo merge-base --is-ancestor 'chore/xunit-v3' dev
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'skip:  chore/xunit-v3 is not merged into dev. Left alone.'
        $script:failed = $true
    }
    else {
        Step 'git branch -d chore/xunit-v3' {
            git -C $repo branch -d 'chore/xunit-v3'
            if ($LASTEXITCODE -ne 0) {
                Write-Host '  refused: git would not delete it. Left alone.'
                $script:failed = $true
            }
        }
    }
}
else {
    Write-Host 'skip:  chore/xunit-v3 no longer exists'
}

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run: nothing was changed. Pass -Execute to act.'
}

exit ([int]$script:failed)
