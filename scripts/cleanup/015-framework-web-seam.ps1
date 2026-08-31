# Cleanup for spec 015 (the framework web seam).
#
# Bare invocation is the dry run. -Execute is the only way to act. Removing the
# vendored asset copies needs -DropVendoredAssets as well: they are tracked
# files, not scratch, and their removal changes what the repository ships.
#
# Nothing here is run by an agent. The user runs it.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    [switch]$DropVendoredAssets
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$scratch = @(
    'tmp/stamp015',   # a product stamped by tools/new-product.ps1 to verify T4 built
    'tmp/stamp015b'   # the same, restamped after tmp/stamp015 could not be reused
)

# Framework assets used to be vendored by hand into each consumer's wwwroot and
# kept in step by AssetParityTests. T7 replaced that with build-time delivery:
# one copy at libs/nativeweb.web/wwwroot/_nativeweb/, contributed to every
# consumer by libs/nativeweb.web/build/NativeWeb.Web.targets and served under
# /_nativeweb/. Nothing references the copies below any more -- the sample's and
# the skeleton's markup was retargeted in the same commit -- but they are still
# served, and a file that is served and never linked is one somebody edits.
$vendored = @(
    'apps/sample/wwwroot/css/tokens.css',
    'apps/sample/wwwroot/css/base.css',
    'apps/sample/wwwroot/css/components.css',
    'apps/sample/wwwroot/vendor/nativeweb/boot.js',
    'skeleton/apps/__product__/wwwroot/css/tokens.css',
    'skeleton/apps/__product__/wwwroot/css/base.css',
    'skeleton/apps/__product__/wwwroot/css/components.css',
    'skeleton/apps/__product__/wwwroot/vendor/nativeweb/boot.js'
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

Write-Host "Scratch created by spec 015 (all of it under tmp/, gitignored):"
$scratchPaths = @()
foreach ($p in $scratch) {
    $hit = Show-Target $p 'scratch'
    if ($hit) { $scratchPaths += $hit }
}

Write-Host ""
Write-Host "Tracked -- needs -DropVendoredAssets as well as -Execute:"
$vendoredPaths = @()
foreach ($p in $vendored) {
    $hit = Show-Target $p 'superseded by build-time delivery (015 T7)'
    if ($hit) { $vendoredPaths += $hit }
}

Write-Host ""
Write-Host "NOT removed by this script, and not candidates:"
Write-Host "  the feat/015-framework-web-seam branch -- branches are never deleted here"
Write-Host "  libs/nativeweb.assets/rollback -- the retained htmx 2.0.10 build; spec 012's"
Write-Host "    script owns that decision, gated behind its own -DropRollback"
Write-Host "  each app's wwwroot/vendor/{htmx,alpinejs,fonts} -- third-party, still vendored"
Write-Host "    per app on purpose, and still checked by AssetParityTests"
Write-Host "  apps/sample/wwwroot/css/sample.css and the skeleton's __product__.css -- the"
Write-Host "    app's own sheet, not a framework copy"

if (-not $Execute) {
    Write-Host ""
    Write-Host "Dry run. Nothing was changed. Re-run with -Execute to remove the scratch above."
    Write-Host "Add -DropVendoredAssets to remove the superseded copies as well; the suite stays"
    Write-Host "green either way, because the parity check treats their absence as the end state."
    exit 0
}

foreach ($p in $scratchPaths) {
    Remove-Item -LiteralPath $p -Recurse -Force
    Write-Host "removed: $p"
}

if ($DropVendoredAssets) {
    foreach ($p in $vendoredPaths) {
        Remove-Item -LiteralPath $p -Force
        Write-Host "removed: $p"
    }

    Write-Host ""
    Write-Host "Removed with git still tracking them: commit the deletions, or 'git checkout --'"
    Write-Host "to put them back. Then run the suite -- AssetParityTests passes on absence, and"
    Write-Host "the transitional wording in its remarks can come out with the last copy."
} elseif ($vendoredPaths.Count -gt 0) {
    Write-Host ""
    Write-Host "Kept the vendored copies. Add -DropVendoredAssets to remove them too."
}
