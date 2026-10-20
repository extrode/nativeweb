# 014-declarative-form-binding.ps1 -- cleanup after spec 014, written at
# close-out on 2026-08-30. Produced by the feat/014-declarative-form-binding
# branch.
#
# Dry run is the default and always exits 0; --execute / -e is the only way to
# act. Nothing here is irreversible, so there is no second flag: every path
# listed is a build output that the next build recreates.
#
# Kept deliberately (do NOT add here):
#   - the branch itself. Per repo policy a branch is never deleted, not even by
#     asking.
#   - libs/nativeweb.forms and libs/nativeweb.forms.generator. They are the
#     deliverable.
#   - apps/sample/ArticleFilter.cs and the /articles filter form. That route is
#     the R5 publish gate -- it exists so the AOT publish has a generated form
#     to compile, and removing it would leave the gate compiling nothing.
#   - tests/nativeweb.web.tests.integration/DeclarativeRegisterBindingTests.cs.
#     Six facts, and the only thing asserting the register routes' wire names.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

# PowerShell binds -Execute / -e; accept the POSIX spelling too so the
# interface is identical in both shells.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute' { $Execute = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$ErrorActionPreference = 'Stop'

$repo = 'C:\home\code\extrode.com\nativeweb'

# Safety gate: the work must be merged before its build outputs are thrown away.
$branch = git -C $repo branch --show-current
if ($branch -ne 'dev') {
    if ($Execute) { Write-Error "$repo is on '$branch', expected 'dev'; refusing." }
    else { Write-Host "NOTE: $repo is on '$branch', expected 'dev' (execute would refuse)." }
}

# The NativeAOT publish outputs from the R5 gate. Roughly 24 MB of native exe
# each plus their intermediates, and both are rebuilt by the next publish.
$targets = @(
    "$repo\apps\sample\bin\Release",
    "$repo\apps\sample\obj\Release",
    "$repo\apps\web\bin\Release",
    "$repo\apps\web\obj\Release",
    # Debug outputs of the two new projects and their test project.
    "$repo\libs\nativeweb.forms\bin",
    "$repo\libs\nativeweb.forms\obj",
    "$repo\libs\nativeweb.forms.generator\bin",
    "$repo\libs\nativeweb.forms.generator\obj",
    "$repo\tests\nativeweb.forms.tests.unit\bin",
    "$repo\tests\nativeweb.forms.tests.unit\obj"
)

foreach ($t in $targets) {
    if (-not (Test-Path $t)) {
        Write-Host "absent:  $t"
        continue
    }

    $size = (Get-ChildItem $t -Recurse -File -ErrorAction SilentlyContinue |
             Measure-Object -Property Length -Sum).Sum
    $mb = if ($size) { [math]::Round($size / 1MB, 1) } else { 0 }

    if ($Execute) {
        Remove-Item $t -Recurse -Force
        Write-Host "removed: $t ($mb MB)"
    }
    else {
        Write-Host "would remove: $t ($mb MB)"
    }
}

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run. Re-run with --execute to remove the paths above.'
}
