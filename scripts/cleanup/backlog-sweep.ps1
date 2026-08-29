# backlog-sweep.ps1 -- cleanup after the 2026-08-29 Now-ish backlog sweep
# (chore/backlog-sweep: security.txt contact, SdkPinTests, launch profile,
# cleanup-script paths, skeleton README + Directory.Build.props).
#
# Almost nothing here is cleanup. The sweep's output is committed source and
# tests; what it left behind is one stamped throwaway product under the repo's
# own tmp/, made to prove the skeleton builds.
#
# Dry run is the default and always exits 0. --execute / -e is the only way to
# act. One thing past that is irreversible enough to need its own flag:
#   -RemoveStampedProbe   deletes tmp/sectest, the Sectest product stamped to
#                         verify skeleton/Directory.Build.props parses and the
#                         skeleton's SecurityTxtEndpoint compiles. It is a full
#                         checkout with its own git repository and its own
#                         bin/obj, so it is not small and it is not tracked.
#
# Kept deliberately (do NOT add here):
#   - the branch. AGENTS.md: never delete a branch, not even by asking.
#   - the two cleanup scripts this sweep did NOT repoint. move-to-extrode.ps1
#     and relocate-nativeweb-to-extrode-legacy-org.ps1 name extrode-legacy-org.com\NativeWeb on
#     purpose: they are records of completed relocations, and rewriting their
#     paths would make each contradict its own first paragraph.
#   - anything under scripts/cleanup/. Those are the artifacts of past work,
#     not residue of this one.

param(
    [Alias('e')][switch]$Execute,
    [switch]$RemoveStampedProbe,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

# Whether a non-zero native exit throws under 'Stop' has moved between
# PowerShell versions. Pin it, or the reporting below silently never runs.
$PSNativeCommandUseErrorActionPreference = $false

foreach ($a in $Rest) {
    switch ($a) {
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$repo = 'C:\home\code\extrode.com\nativeweb'

# Context, reported and not refused on: this script removes nothing tracked, so
# a dirty tree or a feature branch is information rather than a blocker.
$branch = git -C $repo branch --show-current
Write-Host "repo:   $repo"
Write-Host "branch: $branch"
if (git -C $repo status --porcelain) {
    Write-Host "note:   uncommitted changes present. Nothing below touches tracked files."
}
Write-Host ''

function Act([string]$what, [scriptblock]$do) {
    if ($Execute) {
        Write-Host "  do    $what"
        & $do
    }
    else {
        Write-Host "  would $what"
    }
}

Write-Host '-- 1. stamped verification product'

$probe = Join-Path $repo 'tmp\sectest'
if (Test-Path $probe) {
    $size = (Get-ChildItem -Path $probe -Recurse -File -Force -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    $mb = [math]::Round($size / 1MB, 1)
    Write-Host "  found  tmp\sectest ($mb MB, a full checkout with its own .git)"

    if ($RemoveStampedProbe) {
        Act "remove tmp\sectest" {
            # -Force for the read-only files git leaves under .git\objects.
            Remove-Item -Path $probe -Recurse -Force
        }
    }
    else {
        Write-Host '  skip   tmp\sectest -- pass -RemoveStampedProbe to remove it'
    }
}
else {
    Write-Host '  none   tmp\sectest is already gone'
}

Write-Host ''
Write-Host '-- 2. nothing else'
Write-Host '  The sweep produced committed source and tests. No build output, no'
Write-Host '  branches, no worktrees, no containers, no database rows.'

Write-Host ''
if (-not $Execute) {
    Write-Host 'Dry run: nothing was changed. Re-run with --execute to act.'
}
exit 0
