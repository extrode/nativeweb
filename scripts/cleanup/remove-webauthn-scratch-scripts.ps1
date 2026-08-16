#!/usr/bin/env pwsh
# Removes throwaway repo-root scripts and log output created ad hoc while
# manually driving the WebAuthn e2e test outside the normal `dotnet test`
# invocation during Slice 4 debugging. Only ever touches these known
# repo-root filenames -- never anything under apps/, libs/, tests/, or db/.
#
# Dry run is the default and always exits 0.
#   --execute | -e   act rather than print
#   --delete-files   (with --execute) actually delete. Its own second flag
#                    because every action here is deleting an untracked file,
#                    which nothing recreates -- so --execute alone is a no-op
#                    on purpose.
#
# Retrofitted 2026-08-17: this predates the standard cleanup-script interface
# and used to delete on a bare invocation, with no dry run and no way to ask
# what it would do.

param(
    [Alias('e')][switch]$Execute,
    [switch]$DeleteFiles,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

# PowerShell binds -Execute / -e; accept the POSIX spellings too so the
# interface is identical in both shells.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute'      { $Execute = $true }
        '--delete-files' { $DeleteFiles = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$repoRoot = git rev-parse --show-toplevel
$scratchFiles = @(
    "run_all_e2e_tests.sh",
    "run_test.sh",
    "run_test_release.sh",
    "run_test_verbose.sh",
    "test_direct.sh",
    "test_output.txt"
)

# Safety gate: these names are repo-root scratch, so a tracked file by the same
# name means the assumption behind this script no longer holds.
$found = 0
foreach ($name in $scratchFiles) {
    $path = Join-Path $repoRoot $name
    if (-not (Test-Path $path)) {
        Write-Host "nothing to clean up: $name"
        continue
    }

    $found++
    git -C $repoRoot ls-files --error-unmatch $name > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "TRACKED, refusing: $name" -ForegroundColor Red
        if ($Execute) { exit 1 }
        continue
    }

    if ($Execute -and $DeleteFiles) {
        Write-Host "DO   delete $path"
        Remove-Item $path -Force
    } else {
        Write-Host "would delete $path (--execute --delete-files)"
    }
}

if ($found -eq 0) { Write-Host 'Nothing left: this script has already been run.' }
if ($Execute -and -not $DeleteFiles -and $found -gt 0) {
    Write-Host 'NOTE: --execute without --delete-files does nothing here, by design.'
}

if (-not $Execute) { exit 0 }
