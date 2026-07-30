# Cleanup for the DevMarks build-out (spec 001, slices 1-9), written 2026-07-30.
# Produced by the DevMarks implementation sessions: parallel worktrees under
# .worktrees/, their prompt files, the merged feat/devmarks-* branches, and the
# stale epass.* bin/obj leftovers from the N4 neutral-naming rename.
#
# Dry run by default: bare invocation prints what it would do and changes nothing.
#   --execute | -e        actually remove worktrees and delete merged branches
#   --delete-files        (with --execute) also delete untracked files/dirs:
#                         .worktrees prompt *.txt, the unregistered
#                         feat-devmarks-transfer-codecs dir, libs/tests epass.* bin/obj
#
# NOT handled here (tracked files; needs a chore branch, noted in work/todo.md):
#   apps/devmarks/wwwroot/vendor/webauthn  -- unused copy from the IdP vendor dir.

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

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $repo

# --- 0. Safety gate ---------------------------------------------------------
$branch = git branch --show-current
if ($Execute -and $branch -ne 'dev') {
    Write-Error "On '$branch', expected 'dev'. Refusing to execute."
    exit 1
}
if (-not $Execute) { Write-Host "DRY RUN -- nothing will change. Pass --execute to act.`n" }

$worktrees = @(
    'feat-devmarks-accounts', 'feat-devmarks-bookmarks', 'feat-devmarks-metadata',
    'feat-devmarks-search', 'feat-devmarks-tags', 'feat-devmarks-transfer'
)
$branches = @(
    'feat/devmarks-accounts', 'feat/devmarks-bookmarks', 'feat/devmarks-metadata',
    'feat/devmarks-search', 'feat/devmarks-tags', 'feat/devmarks-transfer',
    'feat/devmarks-transfer-codecs', 'feat/devmarks-hardening'
)
$untracked = @(
    '.worktrees/feat-devmarks-transfer-codecs'   # dir left after its worktree was unregistered
) + (Get-ChildItem '.worktrees' -Filter 'prompt-*.txt' -ErrorAction SilentlyContinue |
        ForEach-Object { ".worktrees/$($_.Name)" }) +
    (Get-ChildItem 'libs', 'tests' -Directory -Filter 'epass.*' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -Directory -Include 'bin', 'obj' } |
        ForEach-Object { Resolve-Path -Relative $_.FullName })

# --- 1. Worktrees -----------------------------------------------------------
Write-Host "== Worktrees (git worktree remove) =="
foreach ($w in $worktrees) {
    $path = ".worktrees/$w"
    if (-not (Test-Path $path)) { continue }
    if ($Execute) { git worktree remove $path; Write-Host "removed $path" }
    else { Write-Host "would remove $path" }
}

# --- 2. Merged branches (-d only: refuses anything unmerged) ----------------
Write-Host "`n== Branches (git branch -d) =="
foreach ($b in $branches) {
    if (-not (git branch --list $b)) { continue }
    if ($Execute) { git branch -d $b; Write-Host "deleted $b" }
    else { Write-Host "would delete $b" }
}

# --- 3. Untracked leftovers (only with --execute --delete-files) ------------
Write-Host "`n== Untracked files/dirs (needs --delete-files) =="
foreach ($f in $untracked) {
    if (-not (Test-Path $f)) { continue }
    if ($Execute -and $DeleteFiles) { Remove-Item $f -Recurse -Force; Write-Host "deleted $f" }
    else { Write-Host "would delete $f" }
}

# --- 4. Remaining state -----------------------------------------------------
Write-Host "`n== Remaining worktrees =="
git worktree list
Write-Host "`n== Remaining local branches =="
git branch
if (-not $Execute) { exit 0 }
