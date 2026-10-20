<#
.SYNOPSIS
    Removes what fix/scaffold-e2e-os-allocated-ports left behind. Dry run by default.

.DESCRIPTION
    Every path is named in full. No globs: a previous script in this directory used bare
    tmp/*.log patterns and its dry run listed roughly 150 files belonging to six other
    branches.

    What it would remove:

      tmp/scaffold-e2e.log     per-tier test output captured while proving the fix
      tmp/permissions-e2e.log
      tmp/forms-e2e.log
      tmp/signin-e2e.log
      tmp/sample-unit.log
      tmp/break1.log           the five deliberate breaks and their results
      tmp/break2.log
      tmp/break3.log
      tmp/break4.log
      tmp/break5.log
      tmp/scaffold-port-review.diff   the diff handed to the review worker
      tmp/sample-publish.lock  the cross-process publish lock, only when nothing holds it
      the branch fix/scaffold-e2e-os-allocated-ports, only once merged into dev

    It does NOT stop justsmtp.web.exe on port 5348. That process belongs to another
    repository, and after this branch nothing here binds that port anyway.

.PARAMETER Execute
    Actually remove. Without it, this only reports.
#>
[CmdletBinding()]
param(
    [Alias('e')]
    [switch]$Execute
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$files = @(
    'tmp/scaffold-e2e.log'
    'tmp/permissions-e2e.log'
    'tmp/forms-e2e.log'
    'tmp/signin-e2e.log'
    'tmp/sample-unit.log'
    'tmp/break1.log'
    'tmp/break2.log'
    'tmp/break3.log'
    'tmp/break4.log'
    'tmp/break5.log'
    'tmp/scaffold-port-review.diff'
)

foreach ($relative in $files) {
    $path = Join-Path $repo $relative
    if (-not (Test-Path $path)) {
        Write-Host "absent   $relative"
        continue
    }
    if ($Execute) {
        Remove-Item $path -Force
        Write-Host "removed  $relative"
    }
    else {
        Write-Host "would remove  $relative"
    }
}

# The publish lock is shared by the two e2e tiers that publish apps/sample. Removing one a
# running tier holds would let a second publish race it, and ILC fails several layers away
# from anything that mentions parallelism.
$lock = Join-Path $repo 'tmp/sample-publish.lock'
if (Test-Path $lock) {
    $held = $true
    try {
        $stream = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None')
        $stream.Close()
        $held = $false
    }
    catch {
        $held = $true
    }

    if ($held) {
        Write-Host 'skipped  tmp/sample-publish.lock (held by a running tier)'
    }
    elseif ($Execute) {
        Remove-Item $lock -Force
        Write-Host 'removed  tmp/sample-publish.lock'
    }
    else {
        Write-Host 'would remove  tmp/sample-publish.lock'
    }
}
else {
    Write-Host 'absent   tmp/sample-publish.lock'
}

$branch = 'fix/scaffold-e2e-os-allocated-ports'
$merged = & git -C $repo branch --merged dev --format='%(refname:short)'
if ($merged -contains $branch) {
    if ($Execute) {
        & git -C $repo branch -d $branch
    }
    else {
        Write-Host "would remove  branch $branch (merged into dev)"
    }
}
elseif ((& git -C $repo branch --format='%(refname:short)') -contains $branch) {
    Write-Host "skipped  branch $branch (not merged into dev)"
}
else {
    Write-Host "absent   branch $branch"
}
