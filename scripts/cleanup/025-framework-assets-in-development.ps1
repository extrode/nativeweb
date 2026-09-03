<#
.SYNOPSIS
Removes what fix/025 (framework browser assets under dotnet run) left behind.

.DESCRIPTION
Written 2026-09-03 at the end of fix/025-framework-assets-in-development. Bare invocation is a
dry run: it lists what would go and changes nothing. Pass -Execute (-e) to act.

Candidates, each checked at run time rather than asserted here:

  fix/025-framework-assets-in-development
                           the branch, once merged into dev. `git branch -d` is the merged
                           check: git compares commits and refuses an unmerged branch.

Not listed, and deliberately: nothing else. The fix touched two framework files and added one
test file, all of which are the deliverable. tmp/dev-probe*.log, tmp/dev-final.log,
tmp/prod-probe.log and tmp/aot-025.log are this session's own scratch under the gitignored tmp/,
which is mine to delete and not the operator's to review.
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

$target = 'fix/025-framework-assets-in-development'
if (-not (git -C $repo rev-parse --verify --quiet "refs/heads/$target")) {
    Write-Host "skip:  branch $target no longer exists"
}
else {
    Step "git branch -d $target" {
        git -C $repo branch -d $target
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  refused: git would not delete it, which means it is not merged. Left alone.'
            $script:failed = $true
        }
    }
}

Write-Host ''
Write-Host 'Remaining fix/025 branches:'
git -C $repo branch --list 'fix/025*' | ForEach-Object { Write-Host "  $($_.Trim())" }

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run: nothing was changed. Pass -Execute to act.'
}

exit ([int]$script:failed)
