# Cleanup for the seven-item backlog slate worked through on 2026-08-17/18 while
# the user was away: the migrate.sh run lock, the ActionPinTests comment scan, the
# migration lock-duration review, the 303 redirect change, the 415 sweep over the
# six tolerant-ternary handlers, the Release compressed-asset check, and the
# .editorconfig naming rules.
#
# Dry run by default: bare invocation prints what it would do and changes
# nothing. -e / --execute applies it. Removing the untracked scratch under tmp/
# additionally needs --delete-scratch on top of --execute.

param(
    [Alias('e')][switch]$execute,
    [switch]$DeleteScratch,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

# PowerShell binds -Execute / -e; accept the POSIX spellings too so the
# interface is identical in both shells.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute'         { $execute = $true }
        '--delete-scratch'  { $DeleteScratch = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$ErrorActionPreference = 'Stop'

# --- Safety gate: only run from dev with the work merged -------------------
$branch = git branch --show-current
if ($branch -ne 'dev') {
    Write-Host "Refusing to run: current branch is '$branch', expected 'dev'." -ForegroundColor Red
    if ($execute) { exit 1 } else { exit 0 }
}

# --- 1. The merged branches ------------------------------------------------
# One per item, in the order they were built. Each is merged into dev with
# --no-ff, so -d is sufficient and -D is never right here.
$branches = @(
    'fix/action-pin-comment-scan',
    'fix/migrate-run-lock',
    'docs/migration-lock-review',
    'fix/redirect-see-other',
    'fix/415-tolerant-ternary-handlers',
    'chore/release-compressed-asset-check',
    'chore/editorconfig-naming-rules'
)

foreach ($b in $branches) {
    $exists = git branch --list $b
    if (-not $exists) { Write-Host "skip branch $b (gone)"; continue }

    $merged = git branch --merged dev --list $b
    if (-not $merged) {
        Write-Host "skip branch $b (not merged into dev)" -ForegroundColor Yellow
        continue
    }

    if ($execute) {
        git branch -d $b
    } else {
        Write-Host "would: git branch -d $b"
    }
}

# --- 2. Scratch under tmp/ (untracked, gitignored) -------------------------
# Nothing here is tracked and nothing is referenced by the code. The bulk of it
# is the run-lock investigation, which took several rounds to measure: psql's
# stdout is block-buffered through a pipe, and psql on Windows writes CRLF, so
# each probe below exists because the previous one hung or silently never
# matched.
$scratch = @(
    # The coproc probes, in the order they were written.
    'tmp/coproc-probe.sh',
    'tmp/probe2.sh',
    'tmp/probe3.sh',
    'tmp/probe4.sh',
    'tmp/probe5.sh',
    'tmp/probe6.sh',
    'tmp/p5.txt',
    'tmp/a.txt',
    'tmp/b.txt',
    'tmp/c.txt',
    'tmp/ps-out.txt',
    'tmp/ps-err.txt',
    'tmp/lock-test.log',
    # Backup taken before the run-lock edit.
    'tmp/migrate.sh.bak',
    # Logs from the abandoned xUnit harness for the run lock (the .NET pipe
    # deadlock), one per attempt.
    'tmp/migrate-run-lock-0063c53deed94716b17f9024c308b5ec.log',
    'tmp/migrate-run-lock-0825e5c208d14ca3a022c6caee2bd7b0.log',
    'tmp/migrate-run-lock-1a8998d5d49a45f18d9e5addcabc9142.log',
    'tmp/migrate-run-lock-e536bc9d928b4f44907679a6069b3c30.log',
    'tmp/migrate-run-lock-f412cae20298420db47ff8cdc3abd6a3.log',
    'tmp/migrate-run-lock-f9fe4bbc94e14296903d1583347cc8fe.log',
    'tmp/migrate-run-lock-fa6218b141f6459abde87a8ca9567e71.log',
    'tmp/migrate-run-lock-fda95b0b32874863bfbda3bb3cb551f0.log',
    # Test-run logs: the compressed-asset check green, then perturbed, then the
    # whole e2e tier.
    'tmp/compressed-e2e.log',
    'tmp/compressed-e2e-perturbed.log',
    'tmp/e2e-full.log',
    # The four solution builds behind the .editorconfig violation count.
    'tmp/naming-build.log',
    'tmp/naming-build2.log',
    'tmp/naming-build3.log',
    'tmp/naming-build4.log'
)

foreach ($s in $scratch) {
    if (-not (Test-Path $s)) { Write-Host "skip scratch $s (gone)"; continue }

    if ($execute -and $DeleteScratch) {
        Remove-Item -Recurse -Force $s
        Write-Host "removed $s"
    } elseif ($execute) {
        Write-Host "would remove $s (needs --delete-scratch)" -ForegroundColor Yellow
    } else {
        Write-Host "would remove $s (needs --execute --delete-scratch)"
    }
}

# --- Remaining state -------------------------------------------------------
Write-Host ""
Write-Host "branches still present:"
git branch --list 'feat/*' 'fix/*' 'chore/*' 'docs/*' 'refactor/*'
Write-Host ""
Write-Host "tmp/ contents:"
if (Test-Path 'tmp') { Get-ChildItem tmp | Select-Object -ExpandProperty Name } else { Write-Host "(none)" }
