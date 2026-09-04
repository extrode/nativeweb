<#
.SYNOPSIS
Removes what the Fable review of spec 028 and the stamp check left behind.

.DESCRIPTION
Written 2026-09-05 at the end of fix/fable-review-findings. Bare invocation is a dry run: it
lists what would go and changes nothing. Pass -Execute (-e) to act.

Candidates, each checked at run time rather than asserted here:

  every merged branch matching *fable-review*
                           `git merge-base --is-ancestor <branch> dev` is the merged check, not
                           `git branch -d`: git accepts a branch merged to its own upstream, so a
                           pushed-but-unmerged branch deletes with a warning and exit 0. That was
                           one of the findings this branch closed, so this script is the first
                           written with the check the other two now also carry.

Not listed, and deliberately:

  the stamped product under tmp/stamp-check/reviewfix
                           scripts/cleanup/skeleton-stamp-check.ps1 already removes every directory
                           under tmp/stamp-check/. Two scripts claiming one directory is how a dry
                           run starts lying about what is left.

  the review scratch under tmp/
                           Diffs, prompts, worker output and the throwaway git repository under
                           tmp/f1c that settled the branch-deletion finding. Scratch in the
                           project's own tmp/ is removed by whoever made it, not by a script.
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
# bare string throw rather than answer 1, so one matching branch failed the whole script.
$targets = @(git -C $repo branch --list '*fable-review*' --format '%(refname:short)' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($targets.Count -eq 0) {
    Write-Host 'skip:  no fable-review branches remain'
}

foreach ($target in $targets) {
    if (-not (git -C $repo rev-parse --verify --quiet "refs/heads/$target")) {
        Write-Host "skip:  branch $target no longer exists"
        continue
    }

    git -C $repo merge-base --is-ancestor $target dev
    if ($LASTEXITCODE -ne 0) {
        Write-Host "skip:  $target is not merged into dev. Left alone."
        $script:failed = $true
        continue
    }

    Step "git branch -d $target" {
        git -C $repo branch -d $target
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  refused: git would not delete it. Left alone.'
            $script:failed = $true
        }
    }
}

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run: nothing was changed. Pass -Execute to act.'
}

exit ([int]$script:failed)
