<#
.SYNOPSIS
    Removes what the two ::1-gate branches left behind. Dry run by default.

.DESCRIPTION
    Covers fix/ipv6-loopback-trial-bind and fix/ipv6-gate-testable, which were one piece
    of work in two commits' worth of branches: the first replaced Socket.OSSupportsIPv6
    with a trial bind, the second made the IPv6-unavailable branch reachable from a test.

    Every path is named in full. No globs: a previous script in this directory used bare
    tmp/*.log patterns and its dry run listed roughly 150 files belonging to six other
    branches.

    What it would remove:

      tmp/g-scaffold.log       the first green run of the new PortAllocationTests
      tmp/g-signin.log
      tmp/b1.log               the two deliberate breaks and their results
      tmp/b2.log
      tmp/f-scaffold.log       the full-tier runs taken after the seam went in
      tmp/f-permissions.log
      tmp/f-forms.log
      tmp/f-signin.log
      tmp/f-sample.log
      tmp/ipv6-review.diff     the diff handed to the review worker
      tmp/ipv6-evidence.txt    the measured output handed to it alongside, since it has
                               no shell and could not produce those numbers itself
      the branches fix/ipv6-loopback-trial-bind, fix/ipv6-gate-testable and
      docs/ipv6-gate-record, each only once merged into dev

    It does NOT touch tmp/sample-publish.lock. Two sibling scripts already claim that
    file -- signin-e2e-excluded-port.ps1 removes it unconditionally, and
    scaffold-e2e-os-allocated-ports.ps1 removes it only when nothing holds it. A third
    claim would make it worse. That overlap is a real defect in the older pair and is
    noted in work/todo.md rather than fixed from here.

    It does NOT revert the correction made to
    ~/.claude/handoffs/justsmtp.com/2026-09-05-localhost-tls-and-the-dev-cert-trap.md or
    the entry added to ~/.claude/docs/lessons/for-ai.md. Those are records, not scratch.

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
    'tmp/g-scaffold.log'
    'tmp/g-signin.log'
    'tmp/b1.log'
    'tmp/b2.log'
    'tmp/f-scaffold.log'
    'tmp/f-permissions.log'
    'tmp/f-forms.log'
    'tmp/f-signin.log'
    'tmp/f-sample.log'
    'tmp/ipv6-review.diff'
    'tmp/ipv6-evidence.txt'
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

$branches = @(
    'fix/ipv6-loopback-trial-bind'
    'fix/ipv6-gate-testable'
    'docs/ipv6-gate-record'
)

$merged = & git -C $repo branch --merged dev --format='%(refname:short)'
$all = & git -C $repo branch --format='%(refname:short)'

foreach ($branch in $branches) {
    if ($merged -contains $branch) {
        if ($Execute) {
            & git -C $repo branch -d $branch
        }
        else {
            Write-Host "would remove  branch $branch (merged into dev)"
        }
    }
    elseif ($all -contains $branch) {
        Write-Host "skipped  branch $branch (not merged into dev)"
    }
    else {
        Write-Host "absent   branch $branch"
    }
}
