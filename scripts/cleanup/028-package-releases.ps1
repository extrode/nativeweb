<#
.SYNOPSIS
Removes what spec 028 (versioned package releases) left behind.

.DESCRIPTION
Written 2026-09-04 at the end of feat/028-package-releases. Bare invocation is a dry run: it lists
what would go and changes nothing. Pass -Execute (-e) to act.

Candidates, each checked at run time rather than asserted here:

  every merged branch matching *028*
                           `git branch -d` is the merged check: git compares commits and refuses an
                           unmerged branch, so an in-progress 028 branch is safe. Matched by glob
                           rather than listed by name because a hook blocks committing this file
                           straight to dev, so writing the script creates another branch for the
                           script to name.

  artifacts/packages/*.nupkg
                           Thirteen 0.1.0-dev packages, produced by `deno run -A tools/pack.ts`
                           while the pack path was being proved. Gitignored, rebuilt in 23 s by
                           running the command again, and worth removing rather than keeping:
                           pack.ts refuses to run when that directory holds a package the run will
                           not produce, which is the guard that stops `dotnet nuget push`'s glob
                           from shipping a stale file. A leftover release-versioned package there
                           is the exact thing that guard exists to catch, so clearing the directory
                           between a --release experiment and the next pack is the intended way to
                           answer it.

Not listed, and deliberately:

  the artifacts/ directory itself
                           .gitignore line 5 covers it and the next pack recreates it. Removing the
                           directory rather than its contents gains nothing.

  bin/ and obj/ anywhere
                           Ordinary build output. `dotnet clean` owns it, and a cleanup script that
                           removes it makes the next build slow for no reason.

  the packages on the feed
                           There are none. Nothing has been published: publish.yml runs on a v*
                           tag and no tag exists. A published version cannot be cleaned up anyway,
                           only deleted, which breaks every consumer that restored it.
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
$targets = @(git -C $repo branch --list '*028*' --format '%(refname:short)' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($targets.Count -eq 0) {
    Write-Host 'skip:  no 028 branches remain'
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

$packages = @(Get-ChildItem -Path (Join-Path $repo 'artifacts/packages') -File -Filter '*.nupkg' -ErrorAction SilentlyContinue)

if ($packages.Count -eq 0) {
    Write-Host 'skip:  no packages under artifacts/packages'
}

foreach ($package in $packages) {
    $kb = [int]($package.Length / 1KB)

    Step "remove $($package.FullName) ($kb KB)" {
        Remove-Item -Path $package.FullName -Force
    }
}

Write-Host ''
Write-Host 'Remaining 028 branches:'
git -C $repo branch --list '*028*' | ForEach-Object { Write-Host "  $($_.Trim())" }

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run: nothing was changed. Pass -Execute to act.'
}

exit ([int]$script:failed)
