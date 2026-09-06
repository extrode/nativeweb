#requires -Version 7
<#
.SYNOPSIS
    Cleanup for the scaffold generator defect fixes.

.DESCRIPTION
    Produced 2026-09-06 by the work on `fix/scaffold-generator-defects` and
    `docs/age-key-not-exposed`. Removes the scratch scripts those branches
    produced under the project's own tmp/, and offers the two merged branches
    for deletion.

    Nothing here asserts what is scratch: every file target is asked of git at
    run time and refused if git tracks it, and every branch is handed to
    `git branch -d`, which compares commits rather than names.

    Bare invocation is the dry run. -Execute is the only way to act, and
    -DeleteScratch is required as well before any file is removed.

.EXAMPLE
    pwsh -NoProfile scripts/cleanup/scaffold-generator-defects.ps1

.EXAMPLE
    pwsh -NoProfile scripts/cleanup/scaffold-generator-defects.ps1 -e -DeleteScratch
#>
[CmdletBinding()]
param(
    [Alias('e')]
    [switch]$Execute,

    [switch]$DeleteScratch
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$script:failed = $false

# A gate about the current state, not about the script's own success: standing on
# the wrong branch is still wrong on the second run.
$branch = git -C $repo rev-parse --abbrev-ref HEAD
if ($branch -ne 'dev') {
    Write-Host "refuse: on branch '$branch'. Run this from dev, where the work is merged."
    exit 1
}

Write-Host ''
Write-Host '1. Scratch under tmp/'
Write-Host '---------------------'

$scratch = @(
    'tmp/fix-exposure-verdict.py'
    'tmp/fix-scaffold-generator.py'
    'tmp/add-scaffold-tests.py'
    'tmp/add-target-key-test.py'
    'tmp/add-review-tests.py'
    'tmp/fix-review-findings-scaffold.py'
    'tmp/falsify-scaffold-fixes.py'
    'tmp/falsify-review-fixes.py'
    'tmp/record-scaffold-work.py'
    'tmp/scaffold-review-prompt.md'
    'tmp/scaffold-review-diff.patch'
)

foreach ($rel in $scratch) {
    $path = Join-Path $repo $rel

    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host "  skip:  $rel is already gone"
        continue
    }

    git -C $repo ls-files --error-unmatch -- $rel > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  refuse: $rel is tracked by git. A cleanup script does not delete committed"
        Write-Host "          content; propose it as a commit instead. Left alone."
        $script:failed = $true
        continue
    }

    if ($Execute -and $DeleteScratch) {
        Remove-Item -LiteralPath $path -Force
        Write-Host "  removed $rel"
    }
    else {
        Write-Host "  would remove $rel"
    }
}

Write-Host ''
Write-Host '2. Merged branches'
Write-Host '------------------'

$branches = @(
    'docs/age-key-not-exposed'
    'fix/scaffold-generator-defects'
    'chore/scaffold-cleanup-script'
)

foreach ($target in $branches) {
    git -C $repo rev-parse --verify --quiet "refs/heads/$target" > $null 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  skip:  branch $target no longer exists"
        continue
    }

    if ($Execute) {
        git -C $repo branch -d $target
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  refused: not merged. Left alone.'
            $script:failed = $true
        }
    }
    else {
        Write-Host "  would run: git branch -d $target"
    }
}

Write-Host ''
Write-Host '3. Remaining state'
Write-Host '------------------'
Write-Host '  branches:'
git -C $repo branch --list 'docs/age-key-not-exposed' 'fix/scaffold-generator-defects' 'chore/scaffold-cleanup-script' |
    ForEach-Object { Write-Host "   $_" }
Write-Host '  tmp/ entries still present:'
foreach ($rel in $scratch) {
    if (Test-Path -LiteralPath (Join-Path $repo $rel)) {
        Write-Host "    $rel"
    }
}

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run. Nothing was changed.'
    Write-Host '  -e                 delete the merged branches'
    Write-Host '  -e -DeleteScratch  and remove the tmp/ files as well'
    exit 0
}

if ($script:failed) {
    exit 1
}
