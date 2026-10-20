<#
.SYNOPSIS
Removes what fix/signin-e2e-excluded-port left behind.

.DESCRIPTION
Written 2026-09-05. Bare invocation is a dry run: it lists what would go and changes nothing.
Pass -Execute (-e) to act.

Every candidate is named in full rather than matched by a glob. A previous script in this
directory used bare tmp/*.log patterns and its dry run listed roughly 150 files from six other
branches, so a pattern here would be a bug, not a convenience.

  tmp/signin-e2e             the self-signed kestrel.pfx SignInProcessFixture mints for each
                             run. Regenerated on the next run of the tier; removed here only
                             because the run that made this one is over.

  tmp/sample-publish.lock    the cross-process publish lock the two e2e tiers hold. Zero bytes,
                             and recreated by the next run. Only removed when no test process
                             is holding it, which the script checks by opening it exclusively.

  fix/signin-e2e-excluded-port
                             the branch, once merged into dev. `git branch -d` is the merged
                             check: git compares commits and refuses an unmerged branch.

NOT listed, deliberately:

  justsmtp.web.exe (PID 36360 at the time of writing) holds TCP 5348 and makes the scaffold e2e
  tier fail 31/31. It belongs to another repository and is somebody's running dev server. This
  script does not kill processes.

  artifacts/ and the published apps/sample binary are build output the next publish rewrites.
#>
[CmdletBinding()]
param(
    [Alias('e')]
    [switch]$Execute
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

# 1. The per-run certificate directory
$certDir = Join-Path $repo 'tmp/signin-e2e'
if (-not (Test-Path $certDir)) {
    Write-Host 'skip:  tmp/signin-e2e does not exist'
}
else {
    Step "remove tree $certDir" {
        Remove-Item -LiteralPath $certDir -Recurse -Force
    }
}

# 2. The publish lock, only if nothing holds it
$lock = Join-Path $repo 'tmp/sample-publish.lock'
if (-not (Test-Path $lock)) {
    Write-Host 'skip:  tmp/sample-publish.lock does not exist'
}
else {
    $held = $false
    try {
        $stream = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None')
        $stream.Dispose()
    }
    catch [System.IO.IOException] {
        $held = $true
    }

    if ($held) {
        Write-Host 'hold:  tmp/sample-publish.lock is open; an e2e tier is running. Left alone.'
    }
    else {
        Step "remove $lock" { Remove-Item -LiteralPath $lock -Force }
    }
}

# 3. The merged branch
$target = 'fix/signin-e2e-excluded-port'
if (-not (git -C $repo rev-parse --verify --quiet "refs/heads/$target")) {
    Write-Host "skip:  branch $target no longer exists"
}
else {
    Step "git branch -d $target" {
        git -C $repo branch -d $target
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  refused: not merged. Left alone.'
            $script:failed = $true
        }
    }
}

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run: nothing was changed. Pass -Execute to act.'
}
if ($Execute -and $script:failed) { exit 1 }
