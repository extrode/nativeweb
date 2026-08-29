# csp-hash-scan-context.ps1 -- cleanup after the CSP inline-script hash scanning
# fix, written at close-out on 2026-08-16. Produced by the NativeWeb
# fix/csp-hash-scan-context branch (docs/plans/2026-08-16-003-csp-hash-scan-context.md).
#
# Dry run is the default and always exits 0; --execute / -e is the only way to
# act. The stamped throwaway products are untracked scratch directories rather
# than repo state, so deleting them needs --delete-stamped-products on top of
# --execute (two flags, because it is a recursive delete outside the repo).
#
# Kept deliberately (do NOT add here):
#   - tests/nativeweb.render.tests.unit/templates/Csp/{commented,rawtext,attribute}.html.
#     They started as throwaway probes and are now the regression fixtures for
#     the three over-authorization shapes; deleting them deletes the coverage.

param(
    [Alias('e')][switch]$Execute,
    [switch]$DeleteStampedProducts,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

# PowerShell binds -Execute / -e; accept the POSIX spellings too so the
# interface is identical in both shells. Without this, PowerShell strips one
# leading dash and reports "A parameter cannot be found that matches parameter
# name '-delete-stamped-products'" for the very spelling the header documents.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute'                 { $Execute = $true }
        '--delete-stamped-products' { $DeleteStampedProducts = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$repo = 'C:\home\code\extrode.com\nativeweb'
$tmp  = "$env:LOCALAPPDATA\Temp"

# Safety gate: the branch must already be merged, which means being on dev.
$branch = git -C $repo branch --show-current
if ($branch -ne 'dev') {
    if ($Execute) { Write-Error "$repo is on '$branch', expected 'dev'; refusing." }
    else { Write-Host "NOTE: $repo is on '$branch', expected 'dev' (execute would refuse)." }
}

function Act($desc, [scriptblock]$action) {
    if ($Execute) { Write-Host "DO   $desc"; & $action } else { Write-Host "would $desc" }
}

Write-Host '-- 1. worktrees'
# None were created for this branch; the prune catches a stale registration.
Act 'git worktree prune' { git -C $repo worktree prune }

Write-Host '-- 2. merged branches (git branch -d refuses if unmerged)'
Act 'delete branch fix/csp-hash-scan-context' { git -C $repo branch -d fix/csp-hash-scan-context }

Write-Host '-- 3. stamped throwaway products (needs --delete-stamped-products as well)'
# The skeleton compiles only once stamped, so verifying the app-level lockstep
# meant stamping two products: cspchk (abandoned when the skeleton's
# Directory.Packages.props still lacked AngleSharp) and cspchk2 (where the test
# was run and perturbed). Both are full checkouts with their own git repos.
foreach ($dir in @("$tmp\cspchk", "$tmp\cspchk2")) {
    if (Test-Path $dir) {
        if ($DeleteStampedProducts -and $Execute) {
            Write-Host "DO   delete $dir (recursive, includes its git repo)"
            Remove-Item -Recurse -Force $dir
        } else {
            Write-Host "would delete $dir (--execute --delete-stamped-products)"
        }
    }
}

Write-Host '-- remaining state'
git -C $repo branch --list | Write-Host
git -C $repo worktree list | Write-Host
if (-not $Execute) { exit 0 }
