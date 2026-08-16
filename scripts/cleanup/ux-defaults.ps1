# Cleanup for spec 005 (built-in UX defaults), generated 2026-08-02.
# Produced by the 005 close-out: removes the merged work branches in both repos,
# the docs-review worktree, and the scratch product stamped to verify the
# skeleton flash slot. Dry run is the default and changes nothing; -Execute / -e
# is the only way to act. Deleting the untracked scratch directory is
# irreversible, so it needs -DeleteScratch on top of -Execute.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    [switch]$DeleteScratch,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

# PowerShell binds -Execute / -e; accept the POSIX spellings too so the
# interface is identical in both shells. Without this it strips one leading
# dash and rejects the very spelling this script's header documents.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute'                   { $Execute = $true }
        '--delete-scratch'            { $DeleteScratch = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$ErrorActionPreference = 'Stop'

$nativeWeb = 'C:\home\code\extrode-legacy-org.com\NativeWeb'
$manzil = 'C:\home\code\extrode-legacy-org.com\manzil.my'
$scratch = @(
    'C:\Users\user\AppData\Local\Temp\tmp\flashcheck-15825',
    'C:\Users\user\AppData\Local\Temp\tmp\skel-fixes.py'
)

function Invoke-Step([string]$Description, [scriptblock]$Action) {
    if ($Execute) {
        Write-Host "DO   $Description"
        & $Action
    } else {
        Write-Host "would $Description"
    }
}

# -- 0. Safety gate: both repos must be on dev with the work merged. -----------
foreach ($repo in @($nativeWeb, $manzil)) {
    $branch = git -C $repo branch --show-current
    if ($branch -ne 'dev') {
        if ($Execute) { Write-Error "$repo is on '$branch', expected 'dev'." }
        Write-Host "NOTE $repo is on '$branch', expected 'dev' (gate would fail under -Execute)."
    }
}

# -- 1. NativeWeb: remove the docs-review worktree, then prune. ----------------
# git worktree remove refuses a dirty tree; if it refuses, leave it and say so.
$wt = Join-Path $nativeWeb '.worktrees\docs-pe-doc-fold'
if (Test-Path $wt) {
    Invoke-Step "git -C $nativeWeb worktree remove .worktrees/docs-pe-doc-fold" {
        git -C $nativeWeb worktree remove '.worktrees/docs-pe-doc-fold'
    }
    Invoke-Step "git -C $nativeWeb worktree prune" {
        git -C $nativeWeb worktree prune
    }
} else {
    Write-Host "skip worktree $wt (not present)"
}

# -- 2. NativeWeb: delete merged spec 005 branches (-d, never -D). -------------
foreach ($branch in @('feat/boot-ux', 'feat/idp-ux-defaults', 'feat/skeleton-flash',
        'docs/pe-doc-fold', 'chore/spec005-b2-closeout', 'chore/spec005-closeout')) {
    if (git -C $nativeWeb branch --list $branch) {
        Invoke-Step "git -C $nativeWeb branch -d $branch" {
            git -C $nativeWeb branch -d $branch
        }
    } else {
        Write-Host "skip NativeWeb branch $branch (not present)"
    }
}

# -- 3. manzil.my: delete merged spec 005 branches. ----------------------------
foreach ($branch in @('feat/ux-defaults', 'chore/spec005-changelog')) {
    if (git -C $manzil branch --list $branch) {
        Invoke-Step "git -C $manzil branch -d $branch" {
            git -C $manzil branch -d $branch
        }
    } else {
        Write-Host "skip manzil branch $branch (not present)"
    }
}

# -- 4. Scratch from the skeleton verification (untracked; second flag). -------
foreach ($path in $scratch) {
    if (Test-Path $path) {
        if ($Execute -and -not $DeleteScratch) {
            Write-Host "SKIP $path (pass -DeleteScratch on top of -Execute to remove)"
        } elseif ($Execute) {
            Write-Host "DO   remove $path"
            Remove-Item -Recurse -Force $path
        } else {
            Write-Host "would remove $path (requires -Execute -DeleteScratch)"
        }
    } else {
        Write-Host "skip scratch $path (not present)"
    }
}

# -- 5. Remaining state. -------------------------------------------------------
Write-Host "`n== NativeWeb branches =="
git -C $nativeWeb branch
Write-Host "`n== NativeWeb worktrees =="
git -C $nativeWeb worktree list
Write-Host "`n== manzil branches =="
git -C $manzil branch

if (-not $Execute) { exit 0 }
