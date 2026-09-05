<#
.SYNOPSIS
Removes the branches the Roslyn 5.3.0 pin bump left behind.

.DESCRIPTION
Written 2026-09-05 at the end of chore/roslyn-csharp-5-3. Bare invocation is a dry run: it lists
what would go and changes nothing. Pass -Execute (-e) to act.

Candidates, each checked at run time rather than asserted here:

  chore/roslyn-csharp-5-3      The pin bump and decisions 010/011.
  chore/roslyn-pin-followups   This script and the work/todo.md note.

Both only once they are ancestors of dev.

No scratch to remove: the measurements this bump rests on were taken by tmp/roslyn-floor, which
scripts/cleanup/roslyn-floor-probe.ps1 already removed. Verification here reused the repository's
own tiers rather than a throwaway project, so nothing new was created under tmp/.
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

foreach ($b in @('chore/roslyn-csharp-5-3', 'chore/roslyn-pin-followups')) {
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
