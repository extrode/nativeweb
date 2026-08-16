# Cleanup for the spec-002 IdP hardening run (H1-H7 + close-out), 2026-07-31.
# Produced by the close-out of docs/specs/002-idp-hardening. Dry run by default:
# bare invocation prints what it would do and changes nothing. -e / --execute
# applies it. Deleting the seeded screenshot user additionally needs
# --delete-seeded-user on top of --execute.

param(
    [Alias('e')][switch]$execute,
    [switch]$DeleteSeededUser,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

# PowerShell binds -Execute / -e; accept the POSIX spellings too so the
# interface is identical in both shells. Without this it strips one leading
# dash and rejects the very spelling this script's header documents.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute'                   { $execute = $true }
        '--delete-seeded-user'        { $DeleteSeededUser = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$ErrorActionPreference = 'Stop'

# --- Safety gate: only run from dev with the work merged -------------------
$branch = git branch --show-current
if ($branch -ne 'dev') {
    Write-Host "Refusing to run: current branch is '$branch', expected 'dev'." -ForegroundColor Red
    if ($execute) { exit 1 } else { exit 0 }
}

$branches = @(
    'fix/e2e-appprocess-fixture',
    'fix/idp-csp-htmx-indicator',
    'fix/idp-oauth-rfc',
    'feat/idp-admin-unflag',
    'feat/idp-reset-hardening',
    'feat/idp-breach-throttle',
    'chore/db-role-rename',
    'docs/spec-002-closeout'
)

# --- 1. Merged spec-002 branches -------------------------------------------
foreach ($b in $branches) {
    $exists = git branch --list $b
    if (-not $exists) {
        Write-Host "skip   $b (already gone)"
        continue
    }
    if ($execute) {
        # -d, never -D: refuses if somehow unmerged.
        git branch -d $b
    } else {
        Write-Host "would  git branch -d $b"
    }
}

# --- 2. Seeded screenshot operator (nativeweb_dev) -------------------------
# Created 2026-07-31 to screenshot /admin for the close-out check: username
# 'closeout-shot-op', first_name 'Closeout Shot', operator-granted. Nothing
# else references it.
$deleteSql = "DELETE FROM users WHERE username = 'closeout-shot-op' AND first_name = 'Closeout Shot';"
if ($execute -and $DeleteSeededUser) {
    psql -h 127.0.0.1 -U nativeweb_app -d nativeweb_dev -v ON_ERROR_STOP=1 -c $deleteSql
    if ($LASTEXITCODE -ne 0) { exit 1 }
} elseif ($execute) {
    Write-Host "skip   seeded user delete (add --delete-seeded-user to remove it)"
} else {
    Write-Host "would  (with --delete-seeded-user) $deleteSql"
}

# --- Remaining state -------------------------------------------------------
Write-Host ""
Write-Host "Remaining local branches:"
git branch --list
Write-Host ""
Write-Host "Seeded user rows still present:"
psql -h 127.0.0.1 -U nativeweb_app -d nativeweb_dev -Atc "SELECT id, username FROM users WHERE username = 'closeout-shot-op';"
