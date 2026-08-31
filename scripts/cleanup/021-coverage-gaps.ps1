# Cleanup for the 021 coverage-gap branch (test/021-coverage-gaps), 2026-08-31.
#
# Two kinds of target, and they are not equivalent:
#   - scratch this branch made under tmp/, gitignored;
#   - six test directories left behind when the identity provider left the
#     repository on 2026-08-31. They hold nothing but bin/ and obj/: no .csproj,
#     nothing in NativeWeb.slnx, nothing in git. Removing them needs
#     -DropOrphanTestDirs as well as -Execute, because "there is no project file
#     here" is a claim, and this script checks it at run time instead of trusting
#     the sentence above.
#
# Bare invocation is the dry run. -Execute is the only way to act.
#
# Nothing here is run by an agent. The user runs it.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    [switch]$DropOrphanTestDirs
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$failed = $false

$scratch = @(
    'tmp/probe'   # a console project stood up to check whether GetFolderPath honours USERPROFILE
)

$orphans = @(
    'tests/nativeweb.signin.epass.tests.unit',
    'tests/nativeweb.signin.epass.tests.integration',
    'tests/nativeweb.signin.epass.tests.e2e',
    'tests/nativeweb.signin.epass.tests.aot',
    'tests/nativeweb.web.tests.integration',
    'tests/nativeweb.web.tests.e2e'
)

function Test-Removable($rel) {
    $full = Join-Path $repo $rel

    if (-not (Test-Path $full)) {
        Write-Host "  skip:  $rel is already gone"
        return $null
    }

    git -C $repo ls-files --error-unmatch -- $rel > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  refuse: $rel is tracked by git. A cleanup script does not delete committed"
        Write-Host "          content; propose it as a commit instead. Left alone."
        $script:failed = $true
        return $null
    }

    return $full
}

Write-Host "Scratch under tmp/ (gitignored):"
$scratchPaths = @()
foreach ($p in $scratch) {
    $hit = Test-Removable $p
    if ($hit) {
        Write-Host "  would remove: $p"
        $scratchPaths += $hit
    }
}

Write-Host ""
Write-Host "Orphaned test directories -- needs -DropOrphanTestDirs as well as -Execute:"
$orphanPaths = @()
foreach ($p in $orphans) {
    $hit = Test-Removable $p
    if (-not $hit) { continue }

    # The claim in the header, made as a command. A directory that has grown a
    # project file since is a project, whatever this script was written believing.
    $projects = @(Get-ChildItem -LiteralPath $hit -Filter *.csproj -Recurse -File -ErrorAction SilentlyContinue)
    if ($projects.Count -gt 0) {
        Write-Host "  refuse: $p contains $($projects.Count) .csproj file(s). Not residue. Left alone."
        $script:failed = $true
        continue
    }

    Write-Host "  would remove: $p  (bin/obj residue of the departed identity provider)"
    $orphanPaths += $hit
}

Write-Host ""
Write-Host "NOT removed by this script, and not candidates:"
Write-Host "  the test/021-coverage-gaps branch -- branches are never deleted here"
Write-Host "  tests/nativeweb.web.tests.unit -- in NativeWeb.slnx, 94 tests, alive"
Write-Host "  tmp/review-021 -- the 021 review's split diffs and worker logs, kept as the"
Write-Host "    record of the three Fable passes until the user says otherwise"

if (-not $Execute) {
    Write-Host ""
    Write-Host "Dry run. Nothing was changed. Re-run with -Execute to remove the scratch above."
    Write-Host "Add -DropOrphanTestDirs to remove the orphaned test directories as well."
    exit 0
}

foreach ($p in $scratchPaths) {
    Remove-Item -LiteralPath $p -Recurse -Force
    Write-Host "removed: $p"
}

if ($DropOrphanTestDirs) {
    foreach ($p in $orphanPaths) {
        Remove-Item -LiteralPath $p -Recurse -Force
        Write-Host "removed: $p"
    }
} elseif ($orphanPaths.Count -gt 0) {
    Write-Host ""
    Write-Host "Kept the orphaned test directories. Add -DropOrphanTestDirs to remove them too."
}

if ($failed) { exit 1 }
