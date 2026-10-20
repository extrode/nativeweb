<#
.SYNOPSIS
Removes the Roslyn floor probe and the branches that recorded its result.

.DESCRIPTION
Written 2026-09-05. Bare invocation is a dry run: it lists what would go and changes nothing.
Pass -Execute (-e) to act.

  tmp/roslyn-floor        A generator + consumer pair pinned to SDK 9.0.313 by its own
                          global.json, built to measure whether a 4.12.0 generator loads under
                          Roslyn 4.14.0. It does; a 5.3.0 one does not. Both rows are in
                          docs/decisions/2026-09-05-010-roslyn-pin-floor.md, so the directory
                          carries nothing that is not written down.

  the two branches        Only once each is an ancestor of dev.
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

$path = Join-Path $repo 'tmp/roslyn-floor'
if (Test-Path $path) {
    $mb = [int]((Get-ChildItem -Path $path -File -Recurse -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum / 1MB)
    Step "remove $path ($mb MB)" { Remove-Item -Path $path -Recurse -Force }
}
else {
    Write-Host 'skip:  tmp/roslyn-floor is already gone'
}

Write-Host ''

foreach ($b in @('docs/roslyn-pin-decision', 'chore/roslyn-floor-cleanup-script')) {
    if (git -C $repo rev-parse --verify --quiet "refs/heads/$b") {
        # `git branch -d` accepts a branch merged to its own upstream, so it is not the
        # merged-into-dev check it looks like. merge-base asks the question this means to ask.
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
