# Cleanup for the DevMarks build-out (spec 001, slices 1-9), written 2026-07-30,
# extended 2026-08-03 with what the manzil.my extraction (spec 003 V10 R7, commit
# 0760c7c) left behind. Produced by the DevMarks implementation sessions:
# parallel worktrees under .worktrees/, their prompt files, the merged
# feat/devmarks-* branches, the stale epass.* bin/obj leftovers from the N4
# neutral-naming rename, and the source-less apps/devmarks + devmarks/epass test
# directories whose projects now live in manzil.my.
#
# Dry run by default: bare invocation prints what it would do and changes nothing.
#   --execute | -e        actually remove worktrees and delete merged branches
#   --delete-files        (with --execute) also delete untracked files/dirs:
#                         .worktrees prompt *.txt, the unregistered
#                         feat-devmarks-transfer-codecs dir, libs/tests epass.*
#                         bin/obj, and the seven post-extraction leftover dirs
#   --drop-database       (with --execute) also DROP DATABASE devmarks_dev, the
#                         pre-rename local database superseded by manzil_dev.
#                         Its own flag because it is a database, not a file, and
#                         nothing recreates it but a full migrate.sh run.
#
# NOT handled here: apps/manzil/wwwroot/vendor/webauthn in manzil.my -- tracked
# files, so it needs a chore branch with git rm. Noted in work/todo.md.

param(
    [Alias('e')][switch]$Execute,
    [switch]$DeleteFiles,
    [switch]$DropDatabase,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)
$ErrorActionPreference = 'Stop'

# PowerShell binds -Execute / -e; accept the POSIX spellings too so the
# interface is identical in both shells.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute'       { $Execute = $true }
        '--delete-files'  { $DeleteFiles = $true }
        '--drop-database' { $DropDatabase = $true }
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
# Seven directories the manzil.my extraction emptied of source: git tracks
# nothing under any of them, so what remains is bin/obj a build wrote after the
# rename. Listed by name rather than globbed -- a glob over apps/ and tests/
# would happily match a project someone adds later.
$extractionLeftovers = @(
    'apps/devmarks',
    'tests/nativeweb.devmarks.tests.unit',
    'tests/nativeweb.devmarks.tests.integration',
    'tests/nativeweb.devmarks.tests.e2e',
    'tests/epass.web.tests.unit',
    'tests/epass.web.tests.integration',
    'tests/epass.web.tests.e2e'
) | Where-Object { (Test-Path $_) -and -not (git ls-files $_) }

$untracked = @(
    '.worktrees/feat-devmarks-transfer-codecs'   # dir left after its worktree was unregistered
) + $extractionLeftovers + (Get-ChildItem '.worktrees' -Filter 'prompt-*.txt' -ErrorAction SilentlyContinue |
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

# --- 4. Pre-rename database (needs --execute --drop-database) ---------------
# devmarks_dev is what manzil_dev was called before the extraction. Nothing in
# either repo references it any more (only scripts/extract-manzil.ps1, and only
# as a record of the rename it performed), and manzil_dev carries the current
# schema with every migration recorded. Dropping it is irreversible and takes a
# superuser, hence its own flag on top of --execute.
Write-Host "`n== Pre-rename database (needs --drop-database) =="
if ($Execute -and $DropDatabase) {
    psql -h 127.0.0.1 -U postgres -v ON_ERROR_STOP=1 -c 'DROP DATABASE IF EXISTS devmarks_dev'
    Write-Host 'dropped devmarks_dev'
}
else {
    Write-Host 'would DROP DATABASE devmarks_dev (superseded by manzil_dev)'
}

# --- 5. Remaining state -----------------------------------------------------
Write-Host "`n== Remaining worktrees =="
git worktree list
Write-Host "`n== Remaining local branches =="
git branch
if (-not $Execute) { exit 0 }
