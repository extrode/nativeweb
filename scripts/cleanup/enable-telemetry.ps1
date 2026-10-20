# Cleanup for the "enable telemetry" run (docs/plans/2026-08-17-004-enable-telemetry.md),
# 2026-08-18. Dry run by default: bare invocation prints what it would do and
# changes nothing. -e / --execute applies it. Removing the untracked scratch
# under tmp/ additionally needs --delete-scratch on top of --execute.

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

# --- 1. The merged feature branch ------------------------------------------
$branches = @('feat/enable-telemetry')

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
# Publish output and the probe/diff files the two review rounds produced. All
# under the project's own tmp/, none of it tracked.
$scratch = @(
    # Three NativeAOT publishes, one per verification round.
    'tmp/publish-web',
    'tmp/publish-web2',
    'tmp/publish-web3',
    # Throwaway probes: what a span and a metric point actually carry, measured
    # rather than inferred from the semantic conventions.
    'tmp/NpgsqlProbe.cs',
    'tmp/TelemetryProbe.cs.throwaway',
    # Backups taken before each perturbation, and the perturbation scripts.
    'tmp/Npg.bak',
    'tmp/Program.cs.bak',
    'tmp/Program.cs.bak2',
    'tmp/QueryRedactingProcessor.cs.bak',
    'tmp/TelemetrySwitch.cs.bak',
    'tmp/TelemetryTests.cs.bak',
    'tmp/perturb.py',
    'tmp/perturb2.py',
    # Diffs and prompts handed to the review worker, which could not take them
    # on argv: an inlined diff exceeded the argument-length limit.
    'tmp/review-diff.txt',
    'tmp/review-diff2.txt',
    'tmp/review2-prompt.txt'
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
