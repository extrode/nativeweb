# public-release-scratch.ps1 -- written 2026-10-20 after the history rewrite and public release.
#
# Empties this checkout's tmp/ of the scratch the release left behind: probes, review diffs,
# rehearsal clones of the history rewrite, transfer bundles, logs. Every entry is checked with git
# at run time, and anything git tracks is refused.
#
# The pre-scrub backups (pre-scrub-backup-*.bundle, pre-scrub-dev-sha-*.txt, pre-scrub-check.git)
# hold the history as it was before the rewrite. They are the only local copy of it besides any
# archive clone, so they need their own flag and are kept otherwise.
#
# Usage, from the repository root:
#   pwsh -NoProfile scripts/cleanup/public-release-scratch.ps1                         # dry run
#   pwsh -NoProfile scripts/cleanup/public-release-scratch.ps1 -e -DeleteScratch       # scratch only
#   pwsh -NoProfile scripts/cleanup/public-release-scratch.ps1 -e -DeleteScratch -DeletePreScrubBackups
[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    [switch]$DeleteScratch,
    [switch]$DeletePreScrubBackups
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$repo = (git rev-parse --show-toplevel)
if ($LASTEXITCODE -ne 0) { Write-Host 'refuse: not inside a git repository.'; if ($Execute) { exit 1 }; exit 0 }
Set-Location $repo
$failed = $false
$mode = if ($Execute) { 'execute' } else { 'dry run' }
Write-Host "== public-release-scratch: $repo [$mode] =="

if ($Execute -and -not $DeleteScratch) {
    Write-Host 'refuse: -Execute deletes untracked files only with -DeleteScratch as well. Nothing done.'
    exit 1
}

$tmp = Join-Path $repo 'tmp'
if (-not (Test-Path $tmp)) {
    Write-Host 'skip:  tmp/ does not exist; nothing to do.'
    exit 0
}

git check-ignore -q -- tmp/
if ($LASTEXITCODE -ne 0) {
    Write-Host 'refuse: tmp/ is not gitignored here, so it is not scratch by rule. Nothing done.'
    if ($Execute) { exit 1 }
    exit 0
}

function Test-PreScrub([string]$name) {
    return ($name -like 'pre-scrub-backup-*.bundle') -or ($name -like 'pre-scrub-dev-sha-*.txt') -or ($name -eq 'pre-scrub-check.git')
}

Write-Host ''
Write-Host '-- 1. tmp/ scratch --'
$kept = @()
foreach ($item in Get-ChildItem -LiteralPath $tmp -Force) {
    $rel = "tmp/$($item.Name)"
    $tracked = git ls-files -- $rel
    if ($tracked) {
        Write-Host "refuse: $rel holds files tracked by git. A cleanup script does not delete committed"
        Write-Host '        content; propose it as a commit instead. Left alone.'
        $failed = $true
        continue
    }
    if ((Test-PreScrub $item.Name) -and -not $DeletePreScrubBackups) {
        Write-Host "keep:  $rel (pre-scrub backup; pass -DeletePreScrubBackups to remove)"
        $kept += $rel
        continue
    }
    if ($Execute) {
        try {
            Remove-Item -LiteralPath $item.FullName -Recurse -Force
            Write-Host "removed $rel"
        } catch {
            Write-Host "failed: $rel -- $($_.Exception.Message). Left alone."
            $failed = $true
        }
    } else {
        Write-Host "would: remove $rel"
    }
}

Write-Host ''
Write-Host '-- remaining --'
$left = @(Get-ChildItem -LiteralPath $tmp -Force)
Write-Host "tmp/ holds $($left.Count) entr$(if ($left.Count -eq 1) { 'y' } else { 'ies' })."
$left | ForEach-Object { Write-Host "  tmp/$($_.Name)" }

if ($Execute -and $failed) { exit 1 }
exit 0
