<#
.SYNOPSIS
Removes what the local skeleton stamp check left behind.

.DESCRIPTION
Written 2026-09-05 at the end of fix/skeleton-stamp-authorization. Bare invocation is a dry run: it
lists what would go and changes nothing. Pass -Execute (-e) to act.

Candidates, each checked at run time rather than asserted here:

  every merged branch matching *skeleton-stamp*
                           `git branch -d` is the merged check: git compares commits and refuses an
                           unmerged branch, so an in-progress branch is safe.

  stamped products under tmp/
                           Every directory tmp/stamp-check/ holds, plus tmp/localprobe, which was
                           the hand-run probe that found the defect. Each is a full checkout of a
                           stamped product with its own git repository and its own build output --
                           tens of megabytes apiece, and `tools/stamp-check.ts` refuses to reuse a
                           destination that is not empty, so they accumulate one per run. tmp/ is
                           gitignored, so none of this is tracked.

Not listed, and deliberately:

  tmp/stamp-check/ itself
                           The next run recreates it. Removing the directory rather than its
                           contents gains nothing.

  anything under skeleton/
                           It is the deliverable, not scratch.
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
$targets = @(git -C $repo branch --list '*skeleton-stamp*' --format '%(refname:short)' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($targets.Count -eq 0) {
    Write-Host 'skip:  no skeleton-stamp branches remain'
}

foreach ($target in $targets) {
    if (-not (git -C $repo rev-parse --verify --quiet "refs/heads/$target")) {
        Write-Host "skip:  branch $target no longer exists"
        continue
    }

    Step "git branch -d $target" {
        git -C $repo branch -d $target
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  refused: git would not delete it, which means it is not merged. Left alone.'
            $script:failed = $true
        }
    }
}

Write-Host ''

$stamped = @()
$stampRoot = Join-Path $repo 'tmp/stamp-check'
if (Test-Path $stampRoot) {
    $stamped += @(Get-ChildItem -Path $stampRoot -Directory)
}

$probe = Join-Path $repo 'tmp/localprobe'
if (Test-Path $probe) {
    $stamped += @(Get-Item -Path $probe)
}

if ($stamped.Count -eq 0) {
    Write-Host 'skip:  no stamped products under tmp/'
}

foreach ($product in $stamped) {
    $mb = [int]((Get-ChildItem -Path $product.FullName -File -Recurse -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum / 1MB)

    Step "remove $($product.FullName) ($mb MB)" {
        Remove-Item -Path $product.FullName -Recurse -Force
    }
}

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run: nothing was changed. Pass -Execute to act.'
}

exit ([int]$script:failed)
