# Cleanup for the 016 OAuth relying-party branch (feat/016-oauth-relying-party), 2026-09-01.
#
# Two kinds of target, and they are not equivalent:
#   - scratch this branch made under tmp/, gitignored;
#   - ten empty per-project tmp/ directories, which are residue of one failed
#     command. `dotnet publish -p:BaseIntermediateOutputPath=tmp/aot-gate/obj/`
#     was tried to isolate the AOT gate's publish; a RELATIVE
#     BaseIntermediateOutputPath applies per project, so MSBuild created a tmp/
#     under every referenced project before failing with CS0579 duplicate
#     attributes. The obj trees were removed the same minute; the empty shells
#     were not. Removing them needs -DropEmptyProjectTmp as well as -Execute,
#     because "empty" is a claim, and this script checks it at run time instead
#     of trusting the sentence above.
#
# Bare invocation is the dry run. -Execute is the only way to act.
#
# Nothing here is run by an agent. The user runs it.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    [switch]$DropEmptyProjectTmp
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$failed = $false

$scratch = @(
    'tmp/trx',                    # test result files parsed for failure detail
    'tmp/signin-e2e',             # the self-signed certificate the e2e fixture mints per run
    'tmp/sample-publish.lock',    # the cross-process publish lock the two e2e tiers share
    'tmp/016-review-prod.diff',   # the split branch diff the two review lenses read
    'tmp/016-review-tests.diff',
    'tmp/recovered-signin',       # the library as it stood at f743436^, before the generic half was lifted
    'tmp/epass-rp-contract.log',  # worker output: what the sibling repo's relying party expected
    'tmp/nw016-session-model.log'
)

# One-off python and helper files written to drive multi-line edits, because bash
# heredocs mangle backslashes in Windows paths and had already corrupted a csproj.
$scriptScratch = @(
    'tmp/probe.py', 'tmp/probe.txt', 'tmp/probe-patch.py', 'tmp/mutate.py', 'tmp/mutate2.py',
    'tmp/skeleton-wire.py', 'tmp/skeleton-program.py', 'tmp/sample-backchannel.py',
    'tmp/filelock.py', 'tmp/visibility.py', 'tmp/docs-readme.py', 'tmp/todo-edit.py',
    'tmp/status-edit.py', 'tmp/t18-seams.py', 'tmp/trx.py', 'tmp/trx2.py',
    'tmp/signout-body.html'
)

$emptyProjectTmp = @(
    'libs/nativeweb.forms/tmp',
    'libs/nativeweb.forms.generator/tmp',
    'libs/nativeweb.render/tmp',
    'libs/nativeweb.render.generator/tmp',
    'libs/nativeweb.scaffold/tmp',
    'libs/nativeweb.scaffold.generator/tmp',
    'libs/nativeweb.security/tmp',
    'libs/nativeweb.signin/tmp',
    'libs/nativeweb.web/tmp',
    'apps/sample/tmp'
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
foreach ($p in ($scratch + $scriptScratch)) {
    $hit = Test-Removable $p
    if ($hit) {
        Write-Host "  would remove: $p"
        $scratchPaths += $hit
    }
}

Write-Host ""
Write-Host "Empty per-project tmp/ directories -- needs -DropEmptyProjectTmp as well as -Execute:"
$emptyPaths = @()
foreach ($p in $emptyProjectTmp) {
    $hit = Test-Removable $p
    if (-not $hit) { continue }

    # The claim in the header, made as a command. A directory someone has since
    # put a file in is that person's directory, whatever this script believed.
    $contents = @(Get-ChildItem -LiteralPath $hit -Recurse -Force -ErrorAction SilentlyContinue)
    if ($contents.Count -gt 0) {
        Write-Host "  refuse: $p holds $($contents.Count) item(s). Not empty. Left alone."
        $script:failed = $true
        continue
    }

    Write-Host "  would remove: $p  (empty, residue of the BaseIntermediateOutputPath attempt)"
    $emptyPaths += $hit
}

Write-Host ""
Write-Host "NOT removed by this script, and not candidates:"
Write-Host "  the feat/016-oauth-relying-party branch -- branches are never deleted here"
Write-Host "  apps/sample/bin/Release -- the published binary the two e2e tiers launch; rebuilding"
Write-Host "    it costs a 15-minute NativeAOT publish"
Write-Host "  tests/nativeweb.signin.tests.aot/probe-host -- committed source, not scratch: it is"
Write-Host "    outside NativeWeb.slnx on purpose, and the AOT gate publishes it on every run"

if (-not $Execute) {
    Write-Host ""
    Write-Host "Dry run. Nothing was changed. Re-run with -Execute to remove the scratch above."
    Write-Host "Add -DropEmptyProjectTmp to remove the empty per-project tmp/ directories as well."
    exit 0
}

foreach ($p in $scratchPaths) {
    Remove-Item -LiteralPath $p -Recurse -Force
    Write-Host "removed: $p"
}

if ($DropEmptyProjectTmp) {
    foreach ($p in $emptyPaths) {
        Remove-Item -LiteralPath $p -Recurse -Force
        Write-Host "removed: $p"
    }
} elseif ($emptyPaths.Count -gt 0) {
    Write-Host ""
    Write-Host "Kept the empty per-project tmp/ directories. Add -DropEmptyProjectTmp to remove them too."
}

if ($failed) { exit 1 }
