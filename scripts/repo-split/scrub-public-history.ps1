# Scrubs NativeWeb's git history before the repository goes public, matching Jaunty's and
# JauntyQ's own precedent of doing this before the first public push rather than after (see
# docs/decisions/2026-09-17-013-history-filtered-before-first-public-release.md).
#
# Three transforms, one git-filter-repo pass over the real repo:
#   - path removal:     scrub-paths-idp.txt + scrub-paths-devmarks.txt (--invert-paths)
#   - text replacement: scrub-text-rules.txt    (two real infrastructure IPs)
#   - message rewrite:  scrub-message-rules.txt (the pre-Extrode org identity, "extrode-legacy-org")
#
# Unlike JauntyQ's own scrub (pure text/message replacement, nothing removed), this one removes
# whole directories from history -- the identity provider that left NativeWeb on 2026-08-31 and
# DevMarks, a smaller product spun out to manzil.my. Removed history is not just discarded: this
# script builds a full-commit-history archive of exactly those paths, to a location OUTSIDE this
# repository, before the real repo is touched. The real rewrite refuses to run if that archive is
# missing or fails to verify.
#
# Usage:
#   .\scrub-public-history.ps1                          dry run + build/verify the archive
#                                                        (default, no changes to this repo)
#   .\scrub-public-history.ps1 -ArchiveDir <path>       write the archive bundles somewhere
#                                                        other than the default sibling dir
#   .\scrub-public-history.ps1 -Execute                 archive, then rewrite local history
#   .\scrub-public-history.ps1 -Execute -ForcePush      rewrite, then force-push dev

param(
    [switch]$Execute,
    [switch]$ForcePush,
    [string]$ArchiveDir
)

$Here = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $Here "..\..")).Path
$OriginUrl = "https://github.com/extrode/nativeweb.git"
if (-not $ArchiveDir) {
    $ArchiveDir = Join-Path (Resolve-Path (Join-Path $RepoRoot "..")).Path "nativeweb-history-archive"
}

function Ok      { param($m) Write-Output "ok:    $m" }
function SkipMsg { param($m) Write-Output "skip:  $m" }
function Doing   { param($m) Write-Output "do:    $m" }
function Dry     { param($m) Write-Output "dry:   $m" }
function Bad     { param($m) Write-Output "FAIL:  $m" }

Set-Location $RepoRoot

# The git-filter-repo.exe wrapper on PATH can produce no stdout/stderr in this shell and can
# exit nonzero on valid input (same caveat JauntyQ's own script notes); invoke the underlying
# Python module directly so its diagnostics are actually visible.
$FilterRepoPy = python3 -c "import git_filter_repo; print(git_filter_repo.__file__)"
function Invoke-GitFilterRepo {
    python3 $FilterRepoPy @args
}

$status = git status --porcelain
if ($status) {
    Bad "working tree is not clean; commit or stash before running this"
    exit 1
}

$IdpPaths = Join-Path $Here "scrub-paths-idp.txt"
$DevmarksPaths = Join-Path $Here "scrub-paths-devmarks.txt"
$TextRules = Join-Path $Here "scrub-text-rules.txt"
$MessageRules = Join-Path $Here "scrub-message-rules.txt"
$AlreadyRan = Join-Path $RepoRoot ".git\filter-repo\already_ran"

# --- Phase 1: build and verify the archive (always runs, read-only against this repo) --------

function Build-Archive {
    param([string]$Name, [string]$PathsFile)

    $bundle = Join-Path $ArchiveDir "nativeweb-$Name-history.bundle"
    $scratch = Join-Path $RepoRoot "tmp\repo-split-archive-$Name"

    if ((Test-Path $bundle) -and (git bundle verify $bundle *>$null; $LASTEXITCODE -eq 0)) {
        SkipMsg "$bundle already exists and verifies; not rebuilding"
        return
    }

    Doing "building $Name archive (full history, not a snapshot) at $bundle"
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $scratch
    New-Item -ItemType Directory -Force -Path $ArchiveDir | Out-Null
    New-Item -ItemType Directory -Force -Path (Split-Path $scratch) | Out-Null
    git clone --no-hardlinks --quiet $RepoRoot $scratch

    Push-Location $scratch
    try {
        Remove-Item -Force -ErrorAction SilentlyContinue ".git\filter-repo\already_ran"
        Invoke-GitFilterRepo --force --paths-from-file $PathsFile
    } finally {
        Pop-Location
    }

    git -C $scratch bundle create $bundle --all
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $scratch

    git bundle verify $bundle *>$null
    if ($LASTEXITCODE -ne 0) {
        Bad "archive bundle failed to verify: $bundle"
        exit 1
    }
    Ok "archive written and verified: $bundle"
}

Build-Archive -Name "idp" -PathsFile $IdpPaths
Build-Archive -Name "devmarks" -PathsFile $DevmarksPaths

if (-not $Execute) {
    Dry "previewing rewrite (git filter-repo --dry-run); no changes will be made to this repo"
    # --force is required even for --dry-run: filter-repo refuses to run at all unless the repo
    # is a fresh clone or --force is given.
    Remove-Item -Force -ErrorAction SilentlyContinue $AlreadyRan
    Invoke-GitFilterRepo --force --dry-run `
        --paths-from-file $IdpPaths `
        --paths-from-file $DevmarksPaths `
        --invert-paths `
        --replace-text $TextRules `
        --replace-message $MessageRules
    Dry "review .git/filter-repo/ for the analysis; re-run with -Execute to apply"
    exit 0
}

# --- Phase 2: the real rewrite (only past here with -Execute) --------------------------------

foreach ($name in @("idp", "devmarks")) {
    $bundle = Join-Path $ArchiveDir "nativeweb-$name-history.bundle"
    git bundle verify $bundle *>$null
    if (-not (Test-Path $bundle) -or $LASTEXITCODE -ne 0) {
        Bad "refusing to rewrite: $bundle is missing or does not verify"
        Bad "the archive must exist and verify before the real repo is touched"
        exit 1
    }
}
Ok "both archive bundles present and verified in $ArchiveDir"

$CurrentDevSha = (git rev-parse dev).Trim()
Ok "dev is at $CurrentDevSha before rewrite (used to pin the force-push lease below)"

$tmpDir = Join-Path $RepoRoot "tmp"
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$stamp = Get-Date -Format "yyyyMMddTHHmmss"
$backup = Join-Path $tmpDir "pre-scrub-backup-$stamp.bundle"
Doing "backing up full history to $backup before rewriting"
git bundle create $backup --all
Ok "backup written: $backup"

Doing "rewriting history (git filter-repo --force)"
Remove-Item -Force -ErrorAction SilentlyContinue $AlreadyRan
Invoke-GitFilterRepo --force `
    --paths-from-file $IdpPaths `
    --paths-from-file $DevmarksPaths `
    --invert-paths `
    --replace-text $TextRules `
    --replace-message $MessageRules
Ok "history rewritten"

Doing "re-adding origin remote (filter-repo removes it as a safety measure)"
git remote remove origin 2>$null
git remote add origin $OriginUrl
Ok "origin set to $OriginUrl"

if (-not $ForcePush) {
    SkipMsg "not pushing (pass -ForcePush to force-push dev over origin)"
    exit 0
}

Doing "force-pushing dev to origin (lease pinned to the pre-rewrite tip captured above)"
git push origin dev "--force-with-lease=dev:$CurrentDevSha"
Ok "pushed"
