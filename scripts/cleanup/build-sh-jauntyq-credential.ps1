<#
.SYNOPSIS
Removes the scratch that fix/build-sh-jauntyq-credential produced.

.DESCRIPTION
Written 2026-09-05. Bare invocation is a dry run: it lists what would go and changes nothing.
Pass -Execute (-e) to act.

Named exactly, not globbed. The previous branch's script used bare `tmp/*.log` and `tmp/*.bak`
patterns and its dry run listed ~150 files from six other branches, including 3 GB of perf logs
nothing on that branch made. Everything below was created by THIS branch and by nothing else.

  tmp/coldpkgs-1/       An empty NuGet global-packages folder used to prove a Windows restore
                        reaches the feed rather than the machine cache. ~36 packages, restorable
                        in 11.6 s.
  tmp/cfgprobe/         An isolated repo + APPDATA pair used to establish which config file
  tmp/cfgprobe-appdata/ `dotnet nuget update source` writes to. The APPDATA half holds a
                        NuGet.Config with the literal password `secret-token-probe` -- a probe
                        value, never a real credential, but there is no reason to keep it.
  the six tmp/*.py edit scripts and the logs this branch wrote, each named below.

  the fix/build-sh-jauntyq-credential branch
                        Only once it is an ancestor of dev.

Not removed, and deliberately:

  /tmp/cfgprobe-wsl1    Inside WSL, not this repository -- the same probe run on the platform
                        build.sh targets. Left for WSL's own /tmp handling; this script does not
                        reach across into another filesystem to delete things.
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

$trees = @('tmp/coldpkgs-1', 'tmp/cfgprobe', 'tmp/cfgprobe-appdata')
foreach ($rel in $trees) {
    $full = Join-Path $repo $rel
    if (Test-Path -LiteralPath $full) {
        Step "remove tree $full" { Remove-Item -LiteralPath $full -Recurse -Force }
    }
    else {
        Write-Host "skip:  $rel is already gone"
    }
}

$files = @(
    'tmp/add-restore-preflight.py'
    'tmp/add-buildsh-tests.py'
    'tmp/fix-docs-preflight.py'
    'tmp/fix-visibility-preflight.py'
    'tmp/fix-review-preflight.py'
    'tmp/fix-tests-scoped.py'
    'tmp/preflight.diff'
    'tmp/repo-preflight.log'
    'tmp/repo-scoped.log'
    'tmp/repo-scoped2.log'
    'tmp/repo-scoped3.log'
    'tmp/repo-scoped4.log'
    'tmp/repo-final.log'
    'tmp/break-mode.log'
    'tmp/break-source.log'
)
foreach ($rel in $files) {
    $full = Join-Path $repo $rel
    if (Test-Path -LiteralPath $full) {
        $kb = [int]((Get-Item -LiteralPath $full).Length / 1KB)
        Step "remove $full ($kb KB)" { Remove-Item -LiteralPath $full -Force }
    }
    else {
        Write-Host "skip:  $rel is already gone"
    }
}

Write-Host ''

foreach ($b in @('fix/build-sh-jauntyq-credential')) {
    if (git -C $repo rev-parse --verify --quiet "refs/heads/$b") {
        # `git branch -d` accepts a branch merged to its own upstream, so a pushed branch whose
        # commits never reached dev deletes with a warning and exit 0. merge-base asks the
        # question this script means to ask.
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
