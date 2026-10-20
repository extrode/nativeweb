# Cleanup for the framework-direction body of work, 2026-08-17 to 2026-08-19: the
# Q0-Q7 direction brainstorm, the endpoint-ergonomics and BindAsync spikes, the
# justsmtp.com harvest findings, the two security audits and their fixes, and the
# scaffolding POC (libs/nativeweb.scaffold{,.generator} + apps/sample).
#
# Every branch below is merged into dev with --no-ff, so -d is sufficient and -D is
# never right here.
#
# Dry run by default: bare invocation prints what it would do and changes nothing.
# -e / --execute applies it. Removing the untracked build output of the retired
# projects additionally needs -DropStaleBuildOutput on top of --execute.

param(
    [Alias('e')][switch]$execute,
    [switch]$DropStaleBuildOutput,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

# PowerShell binds -Execute / -e; accept the POSIX spelling of the action flag too
# so the interface is identical in both shells. The second flag is PascalCase
# because PowerShell cannot bind a kebab-case parameter name.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute' { $execute = $true }
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
# In the order they were built. Note that docs/framework-direction-q0-q3 was cut
# from the spike branch, so merging it dragged the spike into dev ahead of its own
# merge -- that is why the antiforgery fix had to land before anything else.
$branches = @(
    'docs/framework-direction',
    'docs/framework-direction-q0-q3',
    'chore/endpoint-ergonomics-spike',
    'docs/spike-and-justsmtp-findings',
    'chore/bindasync-spike',
    'fix/antiforgery-status-code',
    'docs/audit-and-bindasync-findings',
    'fix/audit-clear-bugs',
    'feat/scaffolding-poc',
    # The branches this script itself was written on.
    'chore/framework-direction-cleanup-script',
    'chore/cleanup-script-self-reference',
    'docs/t10-relying-party-extraction',
    'chore/cleanup-script-doc-branch',
    'fix/scaffold-aot-request-delegates',
    'chore/cleanup-script-aot-branch',
    'fix/sample-launch-profile',
    'feat/scaffold-host-extensions',
    'chore/cleanup-script-host-extensions',
    'feat/scaffold-configurable-root',
    'chore/cleanup-script-root-branch'
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

# --- 2. Build output of projects no longer in the solution -----------------
# libs/epass.* was renamed to libs/nativeweb.* by the N4 neutral-naming work and
# libs/nativeweb.devmarks.data belonged to the retired devmarks app. Nothing
# references them; only their bin/ and obj/ trees remain, untracked and gitignored.
# Listed rather than deleted because build output is cheap to regenerate and a
# stale tree is occasionally the only surviving copy of a generated file someone
# wants to read.
$staleTrees = @(
    'libs/epass.auth/bin', 'libs/epass.auth/obj',
    'libs/epass.data/bin', 'libs/epass.data/obj',
    'libs/epass.mail/bin', 'libs/epass.mail/obj',
    'libs/epass.security/bin', 'libs/epass.security/obj',
    'libs/nativeweb.devmarks.data/bin', 'libs/nativeweb.devmarks.data/obj'
)

foreach ($t in $staleTrees) {
    if (-not (Test-Path $t)) { Write-Host "skip $t (gone)"; continue }

    if ($execute -and $DropStaleBuildOutput) {
        Remove-Item -Recurse -Force $t
        Write-Host "removed $t"
    } elseif ($execute) {
        Write-Host "would remove $t (needs -DropStaleBuildOutput)" -ForegroundColor Yellow
    } else {
        Write-Host "would remove $t (needs --execute -DropStaleBuildOutput)"
    }
}

# --- Remaining state -------------------------------------------------------
Write-Host ""
Write-Host "branches still present:"
git branch --list 'feat/*' 'fix/*' 'chore/*' 'docs/*' 'refactor/*'
Write-Host ""
Write-Host "worktrees still registered:"
git worktree list
