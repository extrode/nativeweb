# 007-session-token-at-rest.ps1 -- cleanup after spec 007, written at close-out
# on 2026-08-16. Produced by the NativeWeb feat/007-session-token-at-rest branch.
#
# Dry run is the default and always exits 0; --execute / -e is the only way to
# act. Nothing here is irreversible, so there is no second flag.
#
# Kept deliberately (do NOT add here):
#   - db/migrations/0054_bff_store_session_id_hashed.sql. It is applied to
#     nativeweb_dev and tracked in schema_migrations, and the integration suite
#     reads the file at runtime to lift the SET id expression out of it.
#   - the 106 rewritten bff_store session rows. They are the migration's output,
#     not scratch: their cookies still authenticate, and the plaintext is gone
#     by design and not recoverable.
#   - /tmp/0054.bak and /tmp/se.bak, the perturbation backups. Both were copied
#     back during the run and the working tree matches HEAD; they are inside
#     the shell's own temp and not this script's business.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute
)

$ErrorActionPreference = 'Stop'

$repo = 'C:\home\code\extrode-legacy-org.com\NativeWeb'

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
# None were created for this spec; the prune is here so a stale registration
# from an earlier run does not survive unnoticed.
Act 'git worktree prune' { git -C $repo worktree prune }

Write-Host '-- 2. merged branches (git branch -d refuses if unmerged)'
Act 'delete branch feat/007-session-token-at-rest' { git -C $repo branch -d feat/007-session-token-at-rest }

Write-Host '-- remaining state'
git -C $repo branch --list | Write-Host
git -C $repo worktree list | Write-Host
if (-not $Execute) { exit 0 }
