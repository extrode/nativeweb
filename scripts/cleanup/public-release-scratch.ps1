# public-release-scratch.ps1 -- written 2026-10-20 after the history rewrite and public release.
#
# Empties this checkout's tmp/ of the scratch the release left behind: probes, review diffs,
# rehearsal clones of the history rewrite, transfer bundles, logs. Every entry is checked with git
# at run time, and anything git tracks is refused.
#
# The pre-scrub backups (pre-scrub-backup-*.bundle, pre-scrub-dev-sha-*.txt, pre-scrub-check.git)
# hold the history as it was before the rewrite. They are the only local copy of it besides any
# archive clone, so they need their own flag and are kept otherwise. An entry that holds one at
# any depth is kept whole.
#
# An entry that holds a git repository at any depth (a rehearsal clone, a scratch checkout) may
# carry commits that exist nowhere else, so it is kept unless -DeleteNestedRepos is passed too.
#
# Usage, from the repository root:
#   pwsh -NoProfile scripts/cleanup/public-release-scratch.ps1                         # dry run
#   pwsh -NoProfile scripts/cleanup/public-release-scratch.ps1 -e -DeleteScratch       # plain scratch only
#   pwsh -NoProfile scripts/cleanup/public-release-scratch.ps1 -e -DeleteScratch -DeleteNestedRepos
#   pwsh -NoProfile scripts/cleanup/public-release-scratch.ps1 -e -DeleteScratch -DeleteNestedRepos -DeletePreScrubBackups
[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    [switch]$DeleteScratch,
    [switch]$DeleteNestedRepos,
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

# Walks an entry once without descending into any .git directory or bare *.git repository, and
# reports whether a pre-scrub backup or a git repository sits anywhere inside it.
function Get-Contents([System.IO.FileSystemInfo]$entry) {
    $found = @{ PreScrub = (Test-PreScrub $entry.Name); Repo = $false }
    if (-not $entry.PSIsContainer) { return $found }
    if ($entry.Name -like '*.git') { $found.Repo = $true; return $found }
    $stack = [System.Collections.Generic.Stack[string]]::new()
    $stack.Push($entry.FullName)
    while ($stack.Count -gt 0) {
        foreach ($child in Get-ChildItem -LiteralPath $stack.Pop() -Force) {
            if (Test-PreScrub $child.Name) { $found.PreScrub = $true }
            if ($child.Name -eq '.git' -or ($child.PSIsContainer -and $child.Name -like '*.git')) { $found.Repo = $true; continue }
            if ($child.PSIsContainer -and -not $child.LinkType) { $stack.Push($child.FullName) }
        }
    }
    return $found
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
    $contents = Get-Contents $item
    if ($contents.PreScrub -and -not $DeletePreScrubBackups) {
        Write-Host "keep:  $rel (holds a pre-scrub backup; pass -DeletePreScrubBackups to remove)"
        $kept += $rel
        continue
    }
    if ($contents.Repo -and -not $DeleteNestedRepos) {
        Write-Host "keep:  $rel (holds a git repository; pass -DeleteNestedRepos to remove)"
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
