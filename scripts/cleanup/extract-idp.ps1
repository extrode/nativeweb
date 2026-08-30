# extract-idp.ps1
#
# Written 2026-08-31 by the work on refactor/extract-idp, which removed the identity provider
# from NativeWeb (docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md). Written, never run:
# the user runs it.
#
#   pwsh -NoProfile scripts/cleanup/extract-idp.ps1                       # dry run, changes nothing
#   pwsh -NoProfile scripts/cleanup/extract-idp.ps1 -e                    # act
#   pwsh -NoProfile scripts/cleanup/extract-idp.ps1 -e -DeleteBuildOutput # also delete the build output
#
# What it would remove is build output and rename leftovers, ~1.6 GB of it. Every target is
# checked against git at run time before anything happens to it: this script deletes nothing that
# git tracks, and it says so and fails rather than deciding for itself.

[CmdletBinding()]
param(
    [Alias('e')]
    [switch]$Execute,

    # The second flag, named for what it does. bin/ and obj/ under a project that no longer
    # exists is regenerable output, but it is still 1.6 GB of deletion and it is not what
    # -Execute alone should authorise.
    [switch]$DeleteBuildOutput
)

$ErrorActionPreference = 'Stop'
# Several checks below expect a non-zero exit from a native command (git ls-files on an untracked
# path). Whether that throws instead of setting $LASTEXITCODE has moved between PowerShell
# versions, so pin it rather than inherit it.
$PSNativeCommandUseErrorActionPreference = $false

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:failed = $false

function Step {
    param([string]$What, [scriptblock]$Do)
    if ($Execute) {
        Write-Host "run:   $What"
        & $Do
    } else {
        Write-Host "would: $What"
    }
}

# Refuses a path git tracks. A cleanup script does not delete committed content; that is a commit
# somebody reviews. Absence is a skip, not a refusal, so the script survives its own success.
function Remove-IfUntracked {
    param([string]$Rel, [string]$Why)

    $full = Join-Path $repo $Rel
    if (-not (Test-Path $full)) {
        Write-Host "skip:  $Rel is already gone"
        return
    }

    git -C $repo ls-files --error-unmatch -- $Rel > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "refuse: $Rel is tracked by git. A cleanup script does not delete committed"
        Write-Host "        content; propose it as a commit instead. Left alone."
        $script:failed = $true
        return
    }

    $size = '?'
    try {
        $bytes = (Get-ChildItem -LiteralPath $full -Recurse -File -Force -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum
        if ($null -ne $bytes) { $size = '{0:N0} MB' -f ($bytes / 1MB) }
    } catch { }

    Step "remove $Rel  ($size) -- $Why" {
        Remove-Item -LiteralPath $full -Recurse -Force
    }
}

Write-Host ""
Write-Host "repo: $repo"
if (-not $Execute) {
    Write-Host "DRY RUN. Nothing is changed. Pass -e to act."
}
Write-Host ""

# --------------------------------------------------------------------------------------------
# 1. Rename leftovers.
#
# The 2026-08-19 neutral-naming rename moved libs/epass.* to libs/nativeweb.*, and left the old
# directories behind holding nothing but obj/ output that still carries the old assembly name.
# They look like projects in a directory listing and are not.
# --------------------------------------------------------------------------------------------
Write-Host "-- 1. libs/epass.* rename leftovers (2026-08-19) --"
foreach ($d in 'libs/epass.auth', 'libs/epass.data', 'libs/epass.mail', 'libs/epass.security') {
    Remove-IfUntracked -Rel $d -Why 'obj-only leftover of the neutral-naming rename'
}
Write-Host ""

# --------------------------------------------------------------------------------------------
# 2. Build output under the removed projects.
#
# Every one of these directories was git rm'd on refactor/extract-idp; what is left on disk is
# bin/ and obj/, which git never tracked. Regenerable by definition, and gone for good in the
# sense that the projects that produced them are not coming back here.
#
# Behind its own flag: 1.6 GB is worth a deliberate second word.
# --------------------------------------------------------------------------------------------
Write-Host "-- 2. build output under the removed projects --"
if (-not $DeleteBuildOutput) {
    Write-Host "skip:  build output left alone. Pass -DeleteBuildOutput as well to remove it."
} else {
    $removed = @(
        'apps/web',
        'libs/nativeweb.auth',
        'libs/nativeweb.data',
        'libs/nativeweb.signin.epass',
        'libs/nativeweb.signin.epass.testing',
        'tests/nativeweb.web.tests.unit',
        'tests/nativeweb.web.tests.integration',
        'tests/nativeweb.web.tests.e2e',
        'tests/nativeweb.signin.epass.tests.unit',
        'tests/nativeweb.signin.epass.tests.integration',
        'tests/nativeweb.signin.epass.tests.aot',
        'tests/nativeweb.signin.epass.tests.e2e'
    )
    foreach ($d in $removed) {
        Remove-IfUntracked -Rel $d -Why 'bin/obj of a project removed on refactor/extract-idp'
    }
}
Write-Host ""

# --------------------------------------------------------------------------------------------
# 3. What is deliberately NOT here.
# --------------------------------------------------------------------------------------------
Write-Host "-- 3. not touched, on purpose --"
Write-Host "       refactor/extract-idp   the branch stays. Branches are never deleted here,"
Write-Host "                              and this one is the only place the removed code is"
Write-Host "                              reachable by name rather than by SHA."
Write-Host "       tmp/                   scratch belonging to the session that made it."
Write-Host "       db/sample/             the surviving schema. Not a leftover."
Write-Host ""

# --------------------------------------------------------------------------------------------
# Remaining state.
# --------------------------------------------------------------------------------------------
Write-Host "-- remaining --"
foreach ($d in 'apps', 'libs', 'tests') {
    $full = Join-Path $repo $d
    if (Test-Path $full) {
        $names = (Get-ChildItem -LiteralPath $full -Directory | Select-Object -ExpandProperty Name) -join ', '
        Write-Host ("  {0,-8} {1}" -f "$d/", $names)
    }
}
Write-Host ""

# Non-zero only when -Execute was passed AND something refused. A dry run always exits 0, and so
# does a run that found nothing left to do.
if ($Execute -and $script:failed) {
    Write-Host "One or more targets were refused. See the lines above."
    exit 1
}
