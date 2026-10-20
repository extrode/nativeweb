<#
.SYNOPSIS
Removes what spec 027 (sample forms examples: contact, upload, two-step) left behind.

.DESCRIPTION
Written 2026-09-04 at the end of feat/027-sample-forms-examples. Bare invocation is a dry run: it
lists what would go and changes nothing. Pass -Execute (-e) to act.

Candidates, each checked at run time rather than asserted here:

  every merged branch matching *027*
                           `git branch -d` is the merged check: git compares commits and refuses an
                           unmerged branch, so an in-progress 027 branch is safe. Matched by glob
                           rather than listed by name because a hook blocks committing this file
                           straight to dev -- so writing the script creates another branch for the
                           script to name, and a hardcoded list can never catch up with itself.
                           fix/cleanup-script-scalar-count is named outright because it is the
                           branch that fixed this script, so it exists before the run and the
                           self-reference the glob avoids does not arise.

  upload directories the sample wrote
                           This spec gave apps/sample somewhere to put a file, and running it puts
                           files there. Two shapes, both under build output and neither tracked:
                           the `uploads` directory beside the published binary's data-protection
                           key ring, and `uploads-<guid>` directories under the sample integration
                           tier's bin, which SampleFormsFactory removes on dispose and leaves
                           behind when a run is killed. Pictures uploaded by a browser during the
                           e2e tier are in the first.

Not listed, and deliberately: bin/ and obj/ under the two new test projects. They are ordinary
build output for projects that are the deliverable, `dotnet clean` owns them, and a cleanup script
that removes build output makes the next build slow for no reason.
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
$targets = @(git -C $repo branch --list '*027*' 'fix/cleanup-script-scalar-count' --format '%(refname:short)' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($targets.Count -eq 0) {
    Write-Host 'skip:  no 027 branches remain'
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

$uploadRoots = @(@(
    (Join-Path $repo 'apps/sample/bin'),
    (Join-Path $repo 'tests/nativeweb.sample.tests.integration/bin')
) | Where-Object { Test-Path $_ })

$uploads = @()
foreach ($root in $uploadRoots) {
    $uploads += @(Get-ChildItem -Path $root -Directory -Recurse -Filter 'uploads*' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq 'uploads' -or $_.Name -like 'uploads-*' })
}

if ($uploads.Count -eq 0) {
    Write-Host 'skip:  no upload directories under build output'
}

foreach ($upload in $uploads) {
    $count = (Get-ChildItem -Path $upload.FullName -File -ErrorAction SilentlyContinue | Measure-Object).Count

    Step "remove $($upload.FullName) ($count file(s))" {
        Remove-Item -Path $upload.FullName -Recurse -Force
    }
}

Write-Host ''
Write-Host 'Remaining 027 branches:'
git -C $repo branch --list '*027*' | ForEach-Object { Write-Host "  $($_.Trim())" }

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run: nothing was changed. Pass -Execute to act.'
}

exit ([int]$script:failed)
