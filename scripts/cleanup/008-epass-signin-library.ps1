# 008-epass-signin-library.ps1 -- cleanup after spec 008, written at close-out on
# 2026-08-19. Produced by the NativeWeb feat/008-signin-library branch.
#
# Dry run is the default and always exits 0; --execute / -e is the only way to
# act. Dropping the scratch flow table is irreversible, so it needs a second
# flag, --drop-flow-table, on top of --execute.
#
# Kept deliberately (do NOT add here):
#   - libs/nativeweb.signin.epass.testing. It looks like test scaffolding and is
#     not: it is the conformance fixture a CONSUMER inherits to prove their own
#     store takes atomically, and it ships.
#   - tests/nativeweb.signin.epass.tests.integration/db/. The queries and the
#     schema snapshot are the reference consumer's own data layer, generated
#     from at build time. Nothing here is a scratch pull.
#   - the four spec-008 branches' commits. Merged with --no-ff, so the branch
#     names survive in dev's history whether or not the refs are deleted.
#   - tmp/ under the repo root. Scratch this work created itself; deleting it
#     needs no script and no permission.

param(
    [Alias('e')][switch]$Execute,
    [switch]$DropFlowTable,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

# PowerShell binds -Execute / -e; accept the POSIX spelling too so the interface
# is identical in both shells. Without this, PowerShell strips one leading dash
# and refuses '--execute', the spelling the header documents.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute' { $Execute = $true }
        '--drop-flow-table' { $DropFlowTable = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$repo = 'C:\home\code\extrode-legacy-org.com\NativeWeb'

# Safety gate: the branches must already be merged, which means being on dev.
$branch = git -C $repo branch --show-current
if ($branch -ne 'dev') {
    if ($Execute) { Write-Error "$repo is on '$branch', expected 'dev'; refusing." }
    else { Write-Host "NOTE: $repo is on '$branch', expected 'dev' (execute would refuse)." }
}

function Act($desc, [scriptblock]$action) {
    if ($Execute) { Write-Host "DO   $desc"; & $action } else { Write-Host "would $desc" }
}

Write-Host '-- 1. worktrees'
# None were created for this spec; the prune is here so a stale registration
# from an earlier run does not survive unnoticed.
Act 'git worktree prune' { git -C $repo worktree prune }

Write-Host '-- 2. merged branches (git branch -d refuses if unmerged)'
foreach ($b in @(
        'docs/008-signin-library-spec',
        'docs/008-plan-and-tasks',
        'refactor/008-ring-split',
        'feat/008-signin-library')) {
    Act "delete branch $b" { git -C $repo branch -d $b }
}

Write-Host '-- 3. scratch flow table in nativeweb_dev (needs --drop-flow-table as well)'
# epass_login_flows is created by the integration tier's TestDatabase, which
# applies its own idempotent DDL at startup -- so dropping it costs the next run
# nothing but the CREATE. It is NOT in db/migrations/ and never was: the library
# defines no schema, and the table belongs to the reference consumer.
#
# Irreversible, hence the second flag. The rows in it are expired login flows
# and PKCE verifiers, worth nothing to anyone.
$dropSql = 'DROP TABLE IF EXISTS epass_login_flows;'
if ($DropFlowTable) {
    Act "psql nativeweb_dev -c `"$dropSql`"" {
        psql -h localhost -U nativeweb_app -d nativeweb_dev -c $dropSql
    }
}
else {
    Write-Host "would (with --drop-flow-table) psql nativeweb_dev -c `"$dropSql`""
}

Write-Host '-- remaining state'
git -C $repo branch --list | Write-Host
git -C $repo worktree list | Write-Host
if (-not $Execute) { exit 0 }
