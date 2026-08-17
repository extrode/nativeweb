# deno-first-tooling.ps1 -- cleanup after making Deno the sanctioned JS/TS runtime, 2026-08-17.
# Produced by one branch, merged into dev as f11e68c:
#   chore/deno-first-tooling   AGENTS.md + skeleton/AGENTS.md rule, DenoToolingPolicyTests,
#                              work/todo.md note on the Node 20 action deprecation
#
# Dry run is the default and always exits 0; --execute / -e is the only way to act.
#
# Kept deliberately (do NOT add here):
#   - The edit to ~/.claude/AGENTS.md. It lives outside this repo and is not this
#     script's to touch; nothing here created a copy of it.
#   - No probe or scratch directories were created by this work.

param(
    [Alias('e')][switch]$Execute,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

# PowerShell binds -Execute / -e; accept the POSIX spelling too so the interface
# is identical in both shells.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute' { $Execute = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$repo = 'C:\home\code\extrode-legacy-org.com\NativeWeb'

# Safety gate: the branch below must already be merged, which means being on dev.
$branch = git -C $repo branch --show-current
if ($branch -ne 'dev') {
    if ($Execute) { Write-Error "$repo is on '$branch', expected 'dev'; refusing." }
    else { Write-Host "NOTE: $repo is on '$branch', expected 'dev' (execute would refuse)." }
}

function Act($desc, [scriptblock]$action) {
    if ($Execute) { Write-Host "DO   $desc"; & $action } else { Write-Host "would $desc" }
}

Write-Host '-- 1. worktrees'
# None were created; the prune catches a stale registration from an earlier run.
Act 'git worktree prune' { git -C $repo worktree prune }

Write-Host '-- 2. merged branches (git branch -d refuses if unmerged)'
foreach ($b in @('chore/deno-first-tooling')) {
    Act "delete branch $b" { git -C $repo branch -d $b }
}

Write-Host '-- remaining state'
git -C $repo branch --list 'chore/*' | Write-Host
git -C $repo worktree list | Write-Host
if (-not $Execute) { exit 0 }
