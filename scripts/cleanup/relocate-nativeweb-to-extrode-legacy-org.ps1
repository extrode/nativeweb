# relocate-nativeweb-to-extrode-legacy-org.ps1
#
# Produced by the 2026-07-30 relocation of this repo from
#   C:\home\code\extrode.com\NativeWeb  ->  C:\home\code\extrode-legacy-org.com\NativeWeb
# The relocation itself is already done: ~/.claude session, memory, lessons,
# .claude.json project entry and the Obsidian vault all point at the new path.
# This script removes only the leftovers that relocation produced, plus one
# pre-existing stray directory it uncovered.
#
# Dry run is the default: a bare invocation prints and changes nothing.
# Pass --execute (or -e) to act. --remove-stray-dry-run-dir is a second,
# separate opt-in for the one item that is not ours.

[CmdletBinding()]
param(
  [Alias('e')][switch]$Execute,
  [switch]$RemoveStrayDryRunDir,
  [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

# PowerShell binds -Execute / -e; accept the POSIX spellings too so the
# interface is identical in both shells.
foreach ($a in $Rest) {
  switch ($a) {
    '--execute'                  { $Execute = $true }
    '--remove-stray-dry-run-dir' { $RemoveStrayDryRunDir = $true }
    default { Write-Error "unknown argument: $a"; exit 2 }
  }
}

# ---- safety gate -----------------------------------------------------------
$repo = 'C:\home\code\extrode-legacy-org.com\NativeWeb'
if (-not (Test-Path (Join-Path $repo 'AGENTS.md'))) {
  Write-Error "expected repo not found at $repo; refusing to run"
  if ($Execute) { exit 1 }
}

$claude = Join-Path $env:USERPROFILE '.claude'
$vault  = Join-Path $env:USERPROFILE 'dotfiles\claude-vault'
$mode   = if ($Execute) { 'EXECUTE' } else { 'DRY RUN' }
Write-Host "[$mode] relocate-nativeweb-to-extrode-legacy-org" -ForegroundColor Cyan

function Remove-Candidate([string]$path, [string]$why) {
  if (-not (Test-Path $path)) { Write-Host "  skip (absent) $path"; return }
  Write-Host "  remove $path  # $why"
  if ($Execute) { Remove-Item -LiteralPath $path -Recurse -Force }
}

# ---- 1. pre-edit backups taken during the relocation -----------------------
Write-Host '1. relocation backups'
Remove-Candidate (Join-Path $claude 'lessons\INDEX.md.premove')    'backup of lessons/INDEX.md before the path rewrite'
Remove-Candidate (Join-Path $claude 'lessons\queue.jsonl.premove') 'backup of lessons/queue.jsonl before the path rewrite'
Remove-Candidate (Join-Path $claude 'backups\.claude.json.premove-nativeweb') 'backup of .claude.json before the project-entry merge'

# ---- 2. stale vault project-map entry --------------------------------------
# `memory-sync.js scan` adds the new slug but does not retire the old one, so
# project-map.json still maps local--C--home-code-extrode-com-NativeWeb to a
# directory that no longer exists. Editing JSON, not deleting a file.
Write-Host '2. stale vault project-map entry'
$map = Join-Path $vault 'project-map.json'
$stale = 'local--C--home-code-extrode-com-NativeWeb'
if (Test-Path $map) {
  $json = Get-Content -Raw -LiteralPath $map | ConvertFrom-Json
  if ($json.projects.PSObject.Properties.Name -contains $stale) {
    Write-Host "  drop  $stale  # dir C:\home\code\extrode.com\NativeWeb no longer exists"
    if ($Execute) {
      $json.projects.PSObject.Properties.Remove($stale)
      $json | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $map -Encoding utf8
      Write-Host '  edited project-map.json — commit it in the vault repo'
    }
  } else {
    Write-Host "  skip (absent) $stale"
  }
} else {
  Write-Host "  skip (absent) $map"
}

# ---- 3. stray ~/.claude/--dry-run directory (NOT from this relocation) -----
# An earlier run parsed `--dry-run` as a path and created a directory of that
# name holding a partial copy of lessons/ and scripts/. Pre-existing, found
# while auditing old-path references. Second flag required.
Write-Host '3. stray --dry-run directory (needs -RemoveStrayDryRunDir)'
$stray = Join-Path $claude '--dry-run'
if (Test-Path $stray) {
  if ($RemoveStrayDryRunDir) {
    Remove-Candidate $stray 'flag parsed as a path by an earlier tool run'
  } else {
    Write-Host "  would remove $stray  # pass -RemoveStrayDryRunDir as well"
  }
} else {
  Write-Host "  skip (absent) $stray"
}

# ---- deliberately NOT touched ----------------------------------------------
Write-Host 'left alone (historical records, still referencing the old path):'
Write-Host '  ~/.claude/file-history/, backups/, history.jsonl, jobs/60fa6b24/'
Write-Host '  ~/.claude/lessons/queue.jsonl.bak'
Write-Host '  vault audit/reports/, audit/snapshots/, .sync-backup/'
Write-Host '  old-path cwd values recorded inside session transcripts'

Write-Host "[$mode] done" -ForegroundColor Cyan
if (-not $Execute) { Write-Host 'nothing was changed; re-run with --execute to act' }
exit 0
