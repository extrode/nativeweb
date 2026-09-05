<#
.SYNOPSIS
Removes what the JauntyQ package-mode default left behind.

.DESCRIPTION
Written 2026-09-05 at the end of chore/jauntyq-package-default. Bare invocation is a dry run: it
lists what would go and changes nothing. Pass -Execute (-e) to act.

Candidates, each checked at run time rather than asserted here:

  tmp/*.py                 Six one-shot Python edit scripts, plus the logs beside them
  tmp/*.log                (repo-tier.log, sample.log, break1..break45.log, docs-check.log,
                           green.log). The break logs are the falsification record for
                           JauntyQResolutionModeTests; what they proved is written into
                           docs/decisions/2026-09-05-012-jauntyq-package-mode-default.md, so the
                           files carry nothing that is not recorded.

  tmp/stamp-*/             Stamped products written by tools/stamp-check.ts while verifying this
                           branch (tmp/stamp-jauntyq, tmp/stamp-review2). The script that made
                           them removes nothing itself.

  the chore/jauntyq-package-default branch itself
                           Only once it is an ancestor of dev.

Not listed, and deliberately:

  tmp/trx/                 Seven .trx reports that predate this branch. They belong to whichever
                           run made them; nothing here owns them.

  the pinned JauntyQ.* 0.4.0 packages in ~/.nuget/packages
                           The default build path now depends on them, and the jauntyq-github feed
                           is uncredentialed on this machine. Removing them would leave the
                           repository unable to build at all until the feed is set up.
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
foreach ($pattern in @('tmp/fix-*.py', 'tmp/add-*.py', 'tmp/*.log', 'tmp/*.bak')) {
    $found = @(Get-ChildItem -Path (Join-Path $repo $pattern) -File -ErrorAction SilentlyContinue)
    $scratch += $found
}

$stamps = @(Get-ChildItem -Path (Join-Path $repo 'tmp/stamp-*') -Directory -ErrorAction SilentlyContinue)
foreach ($dir in $stamps) {
    Step "remove tree $($dir.FullName)" {
        Remove-Item -Path $dir.FullName -Recurse -Force
    }
}

if ($scratch.Count -eq 0) {
    Write-Host 'skip:  no jauntyq-package-default scratch under tmp/'
}

foreach ($item in $scratch) {
    $kb = [int]($item.Length / 1KB)
    Step "remove $($item.FullName) ($kb KB)" {
        Remove-Item -Path $item.FullName -Force
    }
}

Write-Host ''

foreach ($b in @('chore/jauntyq-package-default')) {
    if (git -C $repo rev-parse --verify --quiet "refs/heads/$b") {
        # `git branch -d` is NOT the merged check it looks like: git accepts a branch merged
        # to its own upstream, so a pushed branch whose commits never reached dev deletes
        # with a warning and exit 0. merge-base asks the question the script means to ask.
        git -C $repo merge-base --is-ancestor $b dev
        if ($LASTEXITCODE -ne 0) {
            Write-Host "skip:  $b is not merged into dev. Left alone."
            $script:failed = $true
        }
        else {
            Step "git branch -d $b" {
                git -C $repo branch -d $b
                if ($LASTEXITCODE -ne 0) {
                    Write-Host '  refused: git would not delete it. Left alone.'
                    $script:failed = $true
                }
            }
        }
    }
    else {
        Write-Host "skip:  $b no longer exists"
    }
}

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run: nothing was changed. Pass -Execute to act.'
}

exit ([int]$script:failed)
