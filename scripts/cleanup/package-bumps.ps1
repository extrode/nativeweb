<#
.SYNOPSIS
Removes what the NuGet pin bumps left behind.

.DESCRIPTION
Written 2026-09-05 at the end of chore/package-bumps. Bare invocation is a dry run: it lists what
would go and changes nothing. Pass -Execute (-e) to act.

Candidates, each checked at run time rather than asserted here:

  tmp/stamp-bumps          A stamped product from tools/stamp-check.ts, made to prove the skeleton
                           still builds and runs its tiers on the new pins (1 unit, 6 integration).
                           A full checkout with its own build output. tmp/stamp-check/stampcheck
                           was already occupied by an older run, which is why this one has its own
                           name; that older one belongs to whichever branch made it and is not
                           listed here.

  the chore/package-bumps branch itself
                           Only once it is an ancestor of dev.

Not listed, and deliberately:

  the NuGet global package cache
                           The superseded 1.17.0 / 1.6.0 / 1.49.0 / 10.0.7 assets sit in
                           ~/.nuget/packages, shared by every repository on this machine. Nothing
                           here owns them, and `dotnet nuget locals` is not a thing to run on
                           somebody else's behalf.

  the Playwright browser downloads
                           1.62.0 installs its chromium build beside the older ones under
                           %USERPROFILE%\AppData\Local\ms-playwright. Same reason: machine-wide,
                           and any repo still on 1.49 needs the old one.
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
# bare string throw rather than answer 1.
$scratch = @()
foreach ($name in @('tmp/stamp-bumps')) {
    $path = Join-Path $repo $name
    if (Test-Path $path) { $scratch += @(Get-Item -Path $path) }
}

if ($scratch.Count -eq 0) {
    Write-Host 'skip:  no package-bump scratch under tmp/'
}

foreach ($item in $scratch) {
    $mb = [int]((Get-ChildItem -Path $item.FullName -File -Recurse -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum / 1MB)

    Step "remove $($item.FullName) ($mb MB)" {
        Remove-Item -Path $item.FullName -Recurse -Force
    }
}

Write-Host ''

if (git -C $repo rev-parse --verify --quiet 'refs/heads/chore/package-bumps') {
    # `git branch -d` is NOT the merged check it looks like: git accepts a branch merged
    # to its own upstream, so a pushed branch whose commits never reached dev deletes
    # with a warning and exit 0. merge-base asks the question the script means to ask.
    git -C $repo merge-base --is-ancestor 'chore/package-bumps' dev
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'skip:  chore/package-bumps is not merged into dev. Left alone.'
        $script:failed = $true
    }
    else {
        Step 'git branch -d chore/package-bumps' {
            git -C $repo branch -d 'chore/package-bumps'
            if ($LASTEXITCODE -ne 0) {
                Write-Host '  refused: git would not delete it. Left alone.'
                $script:failed = $true
            }
        }
    }
}
else {
    Write-Host 'skip:  chore/package-bumps no longer exists'
}

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run: nothing was changed. Pass -Execute to act.'
}

exit ([int]$script:failed)
