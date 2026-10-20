# Cleanup for spec 012 (htmx 2.0.10 -> 4.0.0 upgrade).
#
# Bare invocation is the dry run. -Execute is the only way to act. Removing the
# retained rollback build needs -DropRollback as well, because that one is not
# recoverable from the working tree once gone.
#
# Nothing here is run by an agent. The user runs it.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    [switch]$DropRollback
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$scratch = @(
    'tmp/htmx4',                 # the downloaded 4.0.0 source read during the upgrade
    'tmp/012-review.diff',       # the code-only diff handed to the review workers
    'tmp/Layout.cs.bak'          # restore point used while mutation-testing Layout.cs
)

$gated = @(
    'libs/nativeweb.assets/rollback'   # the retained htmx 2.0.10 build (spec 012 R11)
)

function Show-Target($rel, $why) {
    $full = Join-Path $repo $rel
    if (Test-Path $full) {
        Write-Host "  would remove: $rel  ($why)"
        return $full
    }
    Write-Host "  already gone: $rel"
    return $null
}

Write-Host "Scratch created by spec 012 (safe to remove; all of it is under tmp/ and gitignored):"
$scratchPaths = @()
foreach ($p in $scratch) {
    $hit = Show-Target $p 'scratch'
    if ($hit) { $scratchPaths += $hit }
}

Write-Host ""
Write-Host "Gated -- needs -DropRollback as well as -Execute:"
$gatedPaths = @()
foreach ($p in $gated) {
    $hit = Show-Target $p 'retained htmx 2.0.10, the one-file revert path'
    if ($hit) { $gatedPaths += $hit }
}

Write-Host ""
Write-Host "NOT removed by this script, and not candidates:"
Write-Host "  the feat/012-htmx-4-upgrade branch -- branches are never deleted here"
Write-Host "  apps/web/bin, apps/web/obj -- build output, rebuilt on demand, not this work's leavings"

if (-not $Execute) {
    Write-Host ""
    Write-Host "Dry run. Nothing was changed. Re-run with -Execute to remove the scratch above."
    Write-Host "Keep the rollback build until htmx 4.0 has run in production long enough that"
    Write-Host "reverting is no longer a plausible thing to want."
    exit 0
}

foreach ($p in $scratchPaths) {
    Remove-Item -LiteralPath $p -Recurse -Force
    Write-Host "removed: $p"
}

if ($DropRollback) {
    foreach ($p in $gatedPaths) {
        Remove-Item -LiteralPath $p -Recurse -Force
        Write-Host "removed: $p"
    }
    Write-Host ""
    Write-Host "The rollback build is gone. Reverting to htmx 2.0.10 now means fetching it again,"
    Write-Host "and spec 012 R11 no longer holds. Update libs/nativeweb.assets/README.md, which"
    Write-Host "documents the rollback/ carve-out."
} elseif ($gatedPaths.Count -gt 0) {
    Write-Host ""
    Write-Host "Kept the rollback build. Add -DropRollback to remove it too."
}
