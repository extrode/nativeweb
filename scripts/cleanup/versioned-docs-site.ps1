<#
.SYNOPSIS
Removes what the versioned-docs-site work left behind.

.DESCRIPTION
Bare invocation is a dry run: it lists what would go and changes nothing. Pass -Execute to act.

Two candidates, both gitignored generated output, neither referenced by anything after
scripts/build-docs.sh moved its output to the tracked docs-site/:

  dist/docs-site      404 KB  the old flat site, overwritten on every build until this change.
                              Nothing writes it now and nothing reads it. dist/ itself is left
                              alone -- other builds use it.
  tmp/docs-probe      519 KB  a scratch versioned build made to measure the site before adopting
                              it. Under the repo's own tmp/, which is gitignored by rule.

Neither is a release artifact. docs-site/ is tracked and is NOT touched by this script.
#>
[CmdletBinding()]
param(
    [Alias('e')]
    [switch]$Execute
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$targets = @(
    (Join-Path $repoRoot 'dist/docs-site'),
    (Join-Path $repoRoot 'tmp/docs-probe')
)

$present = $targets | Where-Object { Test-Path $_ }

if (-not $present) {
    Write-Host 'Nothing to remove.'
    exit 0
}

foreach ($path in $present) {
    $size = (Get-ChildItem -Recurse -File -LiteralPath $path | Measure-Object -Property Length -Sum).Sum
    $kb = [math]::Round($size / 1KB)
    if ($Execute) {
        Remove-Item -Recurse -Force -LiteralPath $path
        Write-Host "removed  $path  ($kb KB)"
    }
    else {
        Write-Host "would remove  $path  ($kb KB)"
    }
}

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run: nothing was changed. Pass -Execute to remove these.'
}
