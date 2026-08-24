<#
.SYNOPSIS
    Removes what spec 009 (three consumer seams for the sign-in library) left behind.

.DESCRIPTION
    Written 2026-08-25, after feat/009-signin-consumer-seams merged into dev with --no-ff.
    Written, never run by an agent. Run it yourself when you want the space back.

    The branch and both worktrees are the deliberate end state of the work, not litter: the
    merge is in dev and unpushed, so nothing here is safe to remove until you have pushed.
    The safety gate below enforces exactly that.

.PARAMETER Execute
    Do the work. Without it this is a dry run and changes nothing.

.PARAMETER DeletePublishOutput
    Also delete the NativeAOT publish directory, which is untracked build output rather than
    anything git can restore. Requires -Execute.

.EXAMPLE
    pwsh -NoProfile scripts/cleanup/009-signin-consumer-seams.ps1
    pwsh -NoProfile scripts/cleanup/009-signin-consumer-seams.ps1 -e
    pwsh -NoProfile scripts/cleanup/009-signin-consumer-seams.ps1 -e -DeletePublishOutput
#>

[CmdletBinding()]
param(
    [Alias('e')]
    [switch]$Execute,

    [switch]$DeletePublishOutput
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$branch = 'feat/009-signin-consumer-seams'
$trees = @(
    (Join-Path $repo '.worktrees/feat-009-signin-consumer-seams'),
    (Join-Path $repo '.worktrees/dev')
)
$publish = Join-Path $repo 'tests/nativeweb.signin.epass.tests.aot/bin/Release/net10.0/win-x64'
$scratch = Join-Path $repo 'tmp/review-feat-009-signin-consumer-seams'

$mode = if ($Execute) { 'EXECUTE' } else { 'DRY RUN — nothing will change' }
Write-Host "009 cleanup — $mode"
Write-Host "repo: $repo"
Write-Host ''

# --- 0. Safety gate --------------------------------------------------------------------
#
# The merge lives only in this clone until it is pushed. Deleting the branch before then
# leaves the reflog as the sole record, which is not a record.

$failed = $false

$merged = (& git -C $repo branch --merged dev --format '%(refname:short)') -contains $branch
if (-not $merged) {
    Write-Host "BLOCKED: $branch is not merged into dev." -ForegroundColor Red
    $failed = $true
} else {
    Write-Host "ok: $branch is merged into dev."
}

$unpushed = & git -C $repo log --oneline 'origin/dev..dev' 2>$null
if ($unpushed) {
    Write-Host "BLOCKED: dev has $($unpushed.Count) unpushed commit(s). Push first, or these deletions are the only copy." -ForegroundColor Red
    $failed = $true
} else {
    Write-Host 'ok: dev is pushed.'
}

if ($failed) {
    Write-Host ''
    Write-Host 'Preconditions failed. Nothing was touched.'
    if ($Execute) { exit 1 }
    exit 0
}

Write-Host ''

# --- 1. Worktrees ----------------------------------------------------------------------
#
# Both are full checkouts of this repo. .worktrees/dev exists because the main checkout was
# on someone else's branch when 009 merged, so the merge was performed from a worktree.
#
# `git worktree remove` refuses a dirty tree, which is the behaviour we want. If it refuses,
# that tree has uncommitted work in it and this script leaves it alone.

foreach ($tree in $trees) {
    if (-not (Test-Path $tree)) {
        Write-Host "skip: $tree (already gone)"
        continue
    }

    Write-Host "worktree remove: $tree"
    if ($Execute) {
        & git -C $repo worktree remove $tree
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  refused — the tree is dirty. Left in place.' -ForegroundColor Yellow
        }
    }
}

Write-Host 'worktree prune'
if ($Execute) { & git -C $repo worktree prune }

Write-Host ''

# --- 2. Branch -------------------------------------------------------------------------
#
# -d, never -D: if git thinks this is unmerged, believe git rather than the gate above.

Write-Host "branch -d $branch"
if ($Execute) {
    & git -C $repo branch -d $branch
    if ($LASTEXITCODE -ne 0) {
        Write-Host '  refused. Left in place.' -ForegroundColor Yellow
    }
}

Write-Host ''

# --- 3. Scratch ------------------------------------------------------------------------
#
# The review report, JSON and rendered HTML, under the gitignored tmp/. Both are ephemeral
# by design; the findings they held are in docs/specs/009-signin-consumer-seams/009-spec.md
# under "What review found", which is the copy that is kept.
#
# The artifact hub that `tools/html/build.js` wrote into docs/ (docs/index.html and
# docs/assets/) is deliberately NOT touched here: it is untracked, it is shared with every
# other artifact this repo has rendered, and removing it is not 009's business.

foreach ($ext in @('json', 'html')) {
    $file = "$scratch.$ext"
    if (Test-Path $file) {
        Write-Host "remove: $file"
        if ($Execute) { Remove-Item -Force $file }
    } else {
        Write-Host "skip: $file (already gone)"
    }
}

Write-Host ''

# --- 4. Publish output (second flag) ---------------------------------------------------
#
# ~93 MB: a 16.8 MB native exe and a 76 MB pdb, from the -r win-x64 AOT gate. Rebuilt by
# `dotnet publish tests/nativeweb.signin.epass.tests.aot -r win-x64 -c Release` whenever
# the gate is re-run, so this is slow to restore rather than impossible.

if (Test-Path $publish) {
    if ($DeletePublishOutput -and $Execute) {
        Write-Host "remove: $publish"
        Remove-Item -Recurse -Force $publish
    } else {
        Write-Host "would remove (needs -Execute -DeletePublishOutput): $publish"
    }
} else {
    Write-Host "skip: $publish (already gone)"
}

# --- 5. What is left -------------------------------------------------------------------

Write-Host ''
Write-Host 'Remaining state:'
& git -C $repo worktree list
Write-Host ''
& git -C $repo branch --list 'feat/009*'
Write-Host ''
if (-not $Execute) {
    Write-Host 'Dry run. Re-run with -e to act.'
}
