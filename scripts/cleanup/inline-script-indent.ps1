# inline-script-indent.ps1 -- cleanup after the 2026-08-02 inline-script indentation fix
# (layout <script> bodies authored at formatted depth in manzil + skeleton).
# Dry run is the default and always exits 0; -Execute / -e is the only way to act.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

# PowerShell binds -Execute / -e; accept the POSIX spellings too so the
# interface is identical in both shells. Without this it strips one leading
# dash and rejects the very spelling this script's header documents.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute'                   { $Execute = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$ErrorActionPreference = 'Stop'

$nativeWeb = 'C:\home\code\extrode-legacy-org.com\NativeWeb'
$manzil    = 'C:\home\code\extrode-legacy-org.com\manzil.my'
$tmp       = "$env:LOCALAPPDATA\Temp\tmp"

# Safety gate: both repos must be on dev with the work merged.
foreach ($repo in @($nativeWeb, $manzil)) {
    $branch = git -C $repo branch --show-current
    if ($branch -ne 'dev') {
        if ($Execute) { Write-Error "$repo is on '$branch', expected 'dev'; refusing." }
        else { Write-Host "NOTE: $repo is on '$branch', expected 'dev' (execute would refuse)." }
    }
}

function Act($desc, [scriptblock]$action) {
    if ($Execute) { Write-Host "DO   $desc"; & $action } else { Write-Host "would $desc" }
}

Write-Host '-- 1. merged branches (git branch -d refuses if unmerged)'
Act "delete NativeWeb branch fix/skeleton-inline-script-indent" { git -C $nativeWeb branch -d fix/skeleton-inline-script-indent }
Act "delete manzil branch fix/inline-script-indent"             { git -C $manzil branch -d fix/inline-script-indent }

Write-Host '-- 2. review handoff diffs (untracked)'
foreach ($diff in @("$nativeWeb\inline-script.diff", "$manzil\inline-script.diff")) {
    if (Test-Path $diff) { Act "delete $diff" { Remove-Item $diff } }
}

Write-Host '-- 3. temp artifacts (emitted-generator dirs, stamped probe)'
foreach ($item in @("$tmp\manzil-gen-scriptfix", "$tmp\skelprobe2", "$tmp\skelprobe2-gen")) {
    if (Test-Path $item) { Act "delete $item" { Remove-Item -Recurse -Force $item } }
}

Write-Host '-- remaining state'
git -C $nativeWeb branch --list | Write-Host
git -C $manzil branch --list | Write-Host
if (-not $Execute) { exit 0 }
