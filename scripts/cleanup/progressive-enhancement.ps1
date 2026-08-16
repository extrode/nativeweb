# Cleanup for spec 004 (progressive enhancement layer + formatted HTML output).
# Produced by the spec 004 close-out on 2026-08-02. Dry run by default; pass
# --execute / -e to act. Nothing here is irreversible beyond branch deletion of
# fully merged branches (git branch -d refuses unmerged work).

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
$manzil = 'C:\home\code\extrode-legacy-org.com\manzil.my'

# Safety gate: both repos must be on dev.
foreach ($repo in @($nativeWeb, $manzil)) {
    $branch = git -C $repo branch --show-current
    if ($branch -ne 'dev') {
        if ($Execute) { Write-Error "$repo is on '$branch', expected 'dev'. Aborting."; exit 1 }
        Write-Host "NOTE: $repo is on '$branch' (expected 'dev'); --execute would abort."
    }
}

function Invoke-Step([string]$Description, [scriptblock]$Action) {
    if ($Execute) {
        Write-Host "DO    $Description"
        & $Action
    } else {
        Write-Host "WOULD $Description"
    }
}

Write-Host '== 1. Merged spec-004 branches (NativeWeb) =='
foreach ($b in @('feat/render-formatting', 'feat/boot-js', 'feat/nw-retrofit-web', 'fix/pe-e2e-settle-race', 'docs/spec-004-close-out')) {
    if (git -C $nativeWeb branch --list $b) {
        Invoke-Step "delete NativeWeb branch $b (merged)" { git -C $nativeWeb branch -d $b }.GetNewClosure()
    }
}

Write-Host '== 2. Merged / empty spec-004 branches (manzil.my) =='
if (git -C $manzil branch --list 'feat/nw-retrofit') {
    Invoke-Step 'delete manzil branch feat/nw-retrofit (merged)' { git -C $manzil branch -d 'feat/nw-retrofit' }
}
# chore/formatting-fallout closed EMPTY (zero commits beyond dev): B1's formatted
# output broke no manzil assertion, so there was nothing to merge.
if (git -C $manzil branch --list 'chore/formatting-fallout') {
    Invoke-Step 'delete manzil branch chore/formatting-fallout (empty, never merged)' { git -C $manzil branch -d 'chore/formatting-fallout' }
}

Write-Host '== 3. Debug scratch from the B5 swap-race diagnosis =='
foreach ($f in @(
    "$env:LOCALAPPDATA\Temp\claude\pe-debug.py",
    "$env:LOCALAPPDATA\Temp\claude\pe-debug2.py",
    "$env:LOCALAPPDATA\Temp\claude\pe-shots.py",
    "$env:LOCALAPPDATA\Temp\claude\manzil-dbg.pfx",
    "$env:LOCALAPPDATA\Temp\claude\vs-idp-signin.png",
    "$env:LOCALAPPDATA\Temp\claude\vs-manzil-home.png"
)) {
    if (Test-Path $f) {
        Invoke-Step "delete $f" { Remove-Item $f }.GetNewClosure()
    }
}
# Git Bash's /tmp maps to the user temp dir. manzil-pw.txt holds the manzil_app DB
# password; the durable copy lives in dotnet user-secrets (ConnectionStrings:Manzil).
foreach ($f in @('manzil-pw.txt', 'review-b4.out', 'review-b5.out', 'publish-web.log', 'publish-manzil.log', 'publish-tail.txt')) {
    $p = Join-Path $env:LOCALAPPDATA "Temp\$f"
    if (Test-Path $p) {
        Invoke-Step "delete $p" { Remove-Item $p }.GetNewClosure()
    }
}

Write-Host '== Remaining state =='
Write-Host '-- NativeWeb branches:'
git -C $nativeWeb branch
Write-Host '-- manzil.my branches:'
git -C $manzil branch

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run. Re-run with --execute (or -e) to apply.'
}
exit 0
