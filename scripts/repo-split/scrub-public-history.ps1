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
# missing, stale, or fails to verify.
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

$ErrorActionPreference = "Stop"

function Ok      { param($m) Write-Output "ok:    $m" }
function SkipMsg { param($m) Write-Output "skip:  $m" }
function Doing   { param($m) Write-Output "do:    $m" }
function Dry     { param($m) Write-Output "dry:   $m" }
function Bad     { param($m) [Console]::Error.WriteLine("FAIL:  $m") }

# PowerShell's $ErrorActionPreference does not catch a native executable's nonzero exit code --
# only real PowerShell errors -- so every git/python call that must not silently continue on
# failure is checked against $LASTEXITCODE explicitly, right after it runs.
function Assert-LastExitCode {
    param([string]$What)
    if ($LASTEXITCODE -ne 0) {
        Bad "$What failed (exit $LASTEXITCODE)"
        exit 1
    }
}

function Test-BundleValid {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return $false }
    git bundle verify $Path *> $null
    return ($LASTEXITCODE -eq 0)
}

$Here = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $Here "..\..")).Path
$OriginUrl = "https://github.com/extrode/nativeweb.git"
if (-not $ArchiveDir) {
    $ArchiveDir = Join-Path (Resolve-Path (Join-Path $RepoRoot "..")).Path "nativeweb-history-archive"
}
# Resolved to absolute now: a relative -ArchiveDir would otherwise resolve against whatever
# directory a later Push-Location leaves current, then get deleted along with the scratch clone.
New-Item -ItemType Directory -Force -Path $ArchiveDir | Out-Null
$ArchiveDir = (Resolve-Path $ArchiveDir).Path

# A machine-wide reference-transaction hook (dotfiles/githooks) refuses any ref move that is not
# a fast-forward, as general protection against an accidental force-reset. Every ref move this
# script makes is exactly that, deliberately -- filter-repo rewrites every commit and moves every
# branch/tag to point at its rewritten counterpart. Set for this process only, and restored in the
# `finally` below -- a bare process-level $env: assignment would otherwise persist in the calling
# PowerShell session after this script exits, silently disabling the guard for every git command
# run afterward in that same window.
$OriginalGitAllowProtected = $env:GIT_ALLOW_PROTECTED
$env:GIT_ALLOW_PROTECTED = "1"

try {

Set-Location $RepoRoot

# The git-filter-repo.exe wrapper on PATH can produce no stdout/stderr in this shell and can
# exit nonzero on valid input (same caveat JauntyQ's own script notes); invoke the underlying
# Python module directly so its diagnostics are actually visible.
$FilterRepoPy = python3 -c "import git_filter_repo; print(git_filter_repo.__file__)"
Assert-LastExitCode "resolving git_filter_repo's module path"
function Invoke-GitFilterRepo {
    python3 $FilterRepoPy @args
    Assert-LastExitCode "git filter-repo"
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
    $currentDevSha = (git rev-parse dev).Trim()

    # `git bundle verify` proves the bundle is intact, not that it is current: a bundle built
    # before a later commit landed on dev would verify cleanly and be reused, silently missing
    # that commit's changes under the archived paths. Compare its recorded dev tip to the real one.
    if (Test-BundleValid $bundle) {
        $bundleDevSha = (git bundle list-heads $bundle refs/heads/dev 2>$null | ForEach-Object { ($_ -split "\s+")[0] } | Select-Object -First 1)
        if ($bundleDevSha -and $bundleDevSha -eq $currentDevSha) {
            SkipMsg "$bundle already exists, verifies, and matches dev's current tip; not rebuilding"
            return
        }
        Doing "existing $bundle is stale (built from a different dev tip); rebuilding"
    }

    Doing "building $Name archive (full history, not a snapshot) at $bundle"
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $scratch
    New-Item -ItemType Directory -Force -Path (Split-Path $scratch) | Out-Null
    git clone --no-hardlinks --quiet $RepoRoot $scratch
    Assert-LastExitCode "cloning to scratch"

    Push-Location $scratch
    try {
        Remove-Item -Force -ErrorAction SilentlyContinue ".git\filter-repo\already_ran"
        Invoke-GitFilterRepo --force --paths-from-file $PathsFile
    } finally {
        Pop-Location
    }

    git -C $scratch bundle create $bundle --all
    Assert-LastExitCode "writing $Name archive bundle"
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $scratch

    if (-not (Test-BundleValid $bundle)) {
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
    if (-not (Test-BundleValid $bundle)) {
        Bad "refusing to rewrite: $bundle is missing or does not verify"
        Bad "the archive must exist and verify before the real repo is touched"
        exit 1
    }
}
Ok "both archive bundles present and verified in $ArchiveDir"

# A force-push of dev alone leaves every other ref on origin untouched -- other branches,
# refs/pull/*, tags -- each keeping the old, unscrubbed graph fully reachable from GitHub even
# after this succeeds. This whole approach's premise ("one moment before anyone clones") only
# holds if dev is the only thing origin has. Refuse rather than silently leave that open.
Doing "checking origin has nothing but dev, and that it matches local before rewriting"
$remoteRefs = git ls-remote origin
Assert-LastExitCode "reading origin's refs"
$remoteDevLine = $remoteRefs | Where-Object { ($_ -split "\s+")[1] -eq "refs/heads/dev" }
if (-not $remoteDevLine) {
    Bad "origin has no refs/heads/dev; refusing rather than guess what to push over"
    exit 1
}
$remoteDevSha = ($remoteDevLine -split "\s+")[0]
$localDevSha = (git rev-parse dev).Trim()
# Local dev is *expected* to be ahead of origin/dev here -- the normal shape of this whole
# workflow is finish work on a branch, merge it into dev locally (AGENTS.md's own
# merged-unpushed convention), then run this before ever pushing that merge. Requiring exact
# equality would refuse the very case this script exists for. What must be refused is real
# divergence: origin/dev holding a commit this local dev does not have, which the archive
# bundles (built from local heads) would then silently miss.
git fetch --quiet origin refs/heads/dev 2>$null
git merge-base --is-ancestor $remoteDevSha $localDevSha 2>$null
if ($LASTEXITCODE -ne 0) {
    Bad "origin/dev ($remoteDevSha) is not an ancestor of local dev ($localDevSha)"
    Bad "origin has a commit this checkout does not; fetch and reconcile before rewriting --"
    Bad "the archive is built from local heads and would silently miss it"
    exit 1
}
$otherRemoteRefs = $remoteRefs | Where-Object {
    $ref = ($_ -split "\s+")[1]
    $ref -ne "HEAD" -and $ref -ne "refs/heads/dev"
}
if ($otherRemoteRefs) {
    Bad "origin has refs besides dev -- force-pushing dev alone will not remove these from GitHub,"
    Bad "so the history being scrubbed stays reachable through them:"
    $otherRemoteRefs | ForEach-Object { [Console]::Error.WriteLine($_) }
    Bad "delete or account for each one first"
    exit 1
}
Ok "origin has exactly dev, and it is an ancestor of local dev ($localDevSha)"

$localTags = git tag -l
if ($localTags) {
    Doing "local tags exist and are not on origin, so they are not a push-time leak, but"
    Doing "filter-repo will remap each one to wherever its commit lands in the rewritten history:"
    $localTags | ForEach-Object { [Console]::Error.WriteLine($_) }
}

$CurrentDevSha = $localDevSha
Ok "dev is at $CurrentDevSha before rewrite (used to pin the force-push lease below)"

$tmpDir = Join-Path $RepoRoot "tmp"
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$stamp = Get-Date -Format "yyyyMMddTHHmmss"
$backup = Join-Path $tmpDir "pre-scrub-backup-$stamp.bundle"
Doing "backing up full history to $backup before rewriting"
git bundle create $backup --all
Assert-LastExitCode "writing the full-history backup bundle"
Ok "backup written: $backup"

# Recorded even if this run does not also -ForcePush: without it, a later manual push has no
# record of which SHA the lease should be pinned to, and re-deriving it means re-reading history
# that this same command is about to rewrite out from under itself.
$shaRecord = Join-Path $tmpDir "pre-scrub-dev-sha-$stamp.txt"
Set-Content -Path $shaRecord -Value $CurrentDevSha -NoNewline
Ok "pre-rewrite dev SHA recorded at $shaRecord"

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
Assert-LastExitCode "re-adding origin"
Ok "origin set to $OriginUrl"

if (-not $ForcePush) {
    SkipMsg "not pushing (pass -ForcePush to force-push dev over origin)"
    exit 0
}

Doing "force-pushing dev to origin (lease pinned to the pre-rewrite tip captured above)"
git push origin dev "--force-with-lease=dev:$CurrentDevSha"
Assert-LastExitCode "force-pushing dev"
Ok "pushed"

} finally {
    $env:GIT_ALLOW_PROTECTED = $OriginalGitAllowProtected
}
