# Cleanup for the orphaned project directories under libs/ and apps/, 2026-08-31.
#
# Ten directories left behind when the identity provider left the repository on
# 2026-08-31 (docs/decisions/2026-08-31-007-idp-leaves-nativeweb.md), plus the
# devmarks data layer that went with the app to the manzil.my repo. They hold
# nothing but bin/ and obj/: no .csproj, no .cs outside those two, nothing in
# NativeWeb.slnx, nothing in git.
#
# apps/web alone is 542 MB, and libs/ reads as nineteen projects while holding
# ten. That is the cost of leaving them: a wrong answer to "what is in this
# repository", given to anyone who runs ls before they run git.
#
# The counterpart script for the six equivalent directories under tests/ is
# 021-coverage-gaps.ps1, which the user ran on 2026-08-31.
#
# Every claim above about the state of a directory is re-made here as a command:
# tracked-ness, absence, the absence of a project file, and the absence of source
# outside bin/ and obj/. A directory that has grown any of those since this was
# written is refused, not removed.
#
# Bare invocation is the dry run. -Execute is the only way to act, and the
# removals are irreversible for anything git does not have, so -DropOrphanDirs is
# required as well.
#
# Nothing here is run by an agent. The user runs it.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    [switch]$DropOrphanDirs
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$failed = $false

$orphans = @(
    'apps/web',
    'libs/nativeweb.auth',
    'libs/nativeweb.data',
    'libs/nativeweb.signin.epass',
    'libs/nativeweb.signin.epass.testing',
    'libs/nativeweb.devmarks.data',
    'libs/epass.auth',
    'libs/epass.data',
    'libs/epass.mail',
    'libs/epass.security'
)

function Get-DirectorySize($full) {
    $bytes = (Get-ChildItem -LiteralPath $full -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    if (-not $bytes) { return '0 B' }
    if ($bytes -ge 1GB) { return '{0:N1} GB' -f ($bytes / 1GB) }
    if ($bytes -ge 1MB) { return '{0:N1} MB' -f ($bytes / 1MB) }
    return '{0:N0} KB' -f ($bytes / 1KB)
}

Write-Host "Orphaned project directories -- needs -DropOrphanDirs as well as -Execute:"

$targets = @()
foreach ($rel in $orphans) {
    $full = Join-Path $repo $rel

    if (-not (Test-Path $full)) {
        Write-Host "  skip:  $rel is already gone"
        continue
    }

    git -C $repo ls-files --error-unmatch -- $rel > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  refuse: $rel is tracked by git. A cleanup script does not delete committed"
        Write-Host "          content; propose it as a commit instead. Left alone."
        $failed = $true
        continue
    }

    # "No project file here" is the whole reason this is residue rather than a
    # project, so it is asked rather than asserted.
    $projects = @(Get-ChildItem -LiteralPath $full -Filter *.csproj -Recurse -File -ErrorAction SilentlyContinue)
    if ($projects.Count -gt 0) {
        Write-Host "  refuse: $rel contains $($projects.Count) .csproj file(s). Not residue. Left alone."
        $failed = $true
        continue
    }

    # And so is "nothing but bin/ and obj/". A .cs file anywhere else is source
    # that git has never seen, which is the one thing here that cannot be got back.
    $source = @(Get-ChildItem -LiteralPath $full -Filter *.cs -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
    if ($source.Count -gt 0) {
        Write-Host "  refuse: $rel holds $($source.Count) .cs file(s) outside bin/ and obj/, untracked."
        Write-Host "          That is unrecoverable source, not build output. Left alone."
        $failed = $true
        continue
    }

    Write-Host "  would remove: $rel  ($(Get-DirectorySize $full), bin/obj residue)"
    $targets += $full
}

Write-Host ""
Write-Host "NOT removed by this script, and not candidates:"
Write-Host "  libs/nativeweb.mail -- tracked, has a .csproj, in NativeWeb.slnx"
Write-Host "  the eight tracked libs and apps/sample -- the framework and its reference app"
Write-Host "  any branch -- branches are never deleted here"
Write-Host "  tmp/ -- scratch, and the 021 script's business, not this one's"

if (-not $Execute) {
    Write-Host ""
    Write-Host "Dry run. Nothing was changed."
    Write-Host "Re-run with -Execute -DropOrphanDirs to remove the directories above."
    exit 0
}

if (-not $DropOrphanDirs) {
    Write-Host ""
    Write-Host "Kept everything. -Execute alone does nothing here: these removals are"
    Write-Host "irreversible for anything git does not have, so they need -DropOrphanDirs too."
    if ($failed) { exit 1 }
    exit 0
}

foreach ($full in $targets) {
    Remove-Item -LiteralPath $full -Recurse -Force
    Write-Host "removed: $full"
}

Write-Host ""
Write-Host "Remaining under libs/ and apps/:"
Get-ChildItem -LiteralPath (Join-Path $repo 'libs') -Directory | ForEach-Object { Write-Host "  libs/$($_.Name)" }
Get-ChildItem -LiteralPath (Join-Path $repo 'apps') -Directory | ForEach-Object { Write-Host "  apps/$($_.Name)" }

if ($failed) { exit 1 }
