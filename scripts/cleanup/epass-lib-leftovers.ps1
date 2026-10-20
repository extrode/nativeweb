# Removes the four empty, untracked `libs/epass.*` directories left behind by the N4
# neutral-naming rename. Git does not track directories, so the rename moved every file out
# and left the folders; nothing references them and nothing in `NativeWeb.slnx` names them.
#
# Bare invocation is the dry run. `-Execute` / `-e` is the only way to act.

[CmdletBinding()]
param(
    [Alias('e')]
    [switch]$Execute
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$candidates = @(
    @{ Path = 'libs/epass.auth';     Why = 'emptied by the N4 rename to libs/nativeweb.auth' }
    @{ Path = 'libs/epass.data';     Why = 'emptied by the N4 rename to libs/nativeweb.data' }
    @{ Path = 'libs/epass.mail';     Why = 'emptied by the N4 rename to libs/nativeweb.mail' }
    @{ Path = 'libs/epass.security'; Why = 'emptied by the N4 rename to libs/nativeweb.security' }
)

$present = 0
$notEmpty = 0

foreach ($candidate in $candidates) {
    $full = Join-Path $repoRoot $candidate.Path
    if (-not (Test-Path $full)) { continue }

    $present++
    $contents = @(Get-ChildItem -LiteralPath $full -Force -Recurse)
    if ($contents.Count -gt 0) {
        # Refuse rather than delete: an empty leftover is safe to remove, a directory that
        # has since acquired content is not the thing this script was written for.
        Write-Host ("{0}  -- SKIPPED, {1} item(s) inside; expected empty" -f $full, $contents.Count)
        $notEmpty++
        continue
    }

    Write-Host ("{0}  -- {1}" -f $full, $candidate.Why)

    if ($Execute) {
        Remove-Item -LiteralPath $full -Force
        Write-Host "Removed $full"
    }
}

if ($present -eq 0) {
    Write-Host 'Nothing to do: none of the four directories is present.'
    exit 0
}

$removable = $present - $notEmpty
if (-not $Execute) {
    Write-Host ("Dry run. {0} removable, {1} skipped. Re-run with -Execute to remove them." -f $removable, $notEmpty)
} else {
    Write-Host ("Removed {0} director(ies), {1} skipped." -f $removable, $notEmpty)
}
