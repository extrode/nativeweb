<#
.SYNOPSIS
Removes what the spec 023 work (admin relations and field kinds) left behind.

.DESCRIPTION
Written 2026-09-03 at the end of feat/023-admin-relations-and-field-kinds. Bare invocation is a
dry run: it lists what would go and changes nothing. Pass -Execute (-e) to act.

Candidates, each checked at run time rather than asserted here:

  nativeweb_sample_023rehearsal
  nativeweb_sample_023rehearsal_test
                           two PostgreSQL databases created 2026-09-02 as copies of
                           nativeweb_sample_dev and nativeweb_sample_test, to rehearse
                           migration 0004 and to run the integration and e2e tiers against a
                           migrated schema before the real databases were migrated. Every SQL-
                           backed test run on the branch used the _test one, through the
                           ConnectionStrings__Sample environment variable. Nothing reads them
                           once 0004 is applied for real. Dropped through psql as the postgres
                           superuser, which is how db/sample/README.md creates the databases.

  ../jauntyq/.worktrees/nativeweb-build-2a005cb
                           a detached worktree of the JauntyQ repository at 2a005cb, created
                           2026-09-02 because the jauntyq checkout's current branch no longer
                           carries src/ and nativeweb's Directory.Build.props probe needs one
                           that does. It lives in the sibling repository, not this one, and
                           EVERY nativeweb build on this machine still resolves JauntyQ through
                           it: removing it breaks the build until JauntyQRoot points somewhere
                           else. Needs -RemoveJauntyPin as well as -Execute, and is refused
                           unless the environment already names another JauntyQRoot.

  feat/023-admin-relations-and-field-kinds
                           the branch, once merged into dev. `git branch -d` is the merged
                           check: git compares commits and refuses an unmerged branch.

nativeweb_sample_022probe is listed by scripts/cleanup/022-sample-public-site.ps1 and is not
repeated here. tmp/probe/023-* is the session's own scratch under the gitignored tmp/ and is not
listed.
#>
[CmdletBinding()]
param(
    [Alias('e')]
    [switch]$Execute,
    [switch]$RemoveJauntyPin
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$script:failed = $false

$branch = (git -C $repo rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'dev') {
    Write-Host "refuse: run this from dev, not from '$branch'. Nothing changed."
    exit 1
}

function Step([string]$label, [scriptblock]$act) {
    if ($Execute) {
        Write-Host "run:   $label"
        & $act
    }
    else {
        Write-Host "would: $label"
    }
}

# 1. The rehearsal databases
foreach ($db in @('nativeweb_sample_023rehearsal', 'nativeweb_sample_023rehearsal_test')) {
    $exists = (psql -h localhost -U postgres -d postgres -Atc "SELECT 1 FROM pg_database WHERE datname = '$db'").Trim()
    if ($exists -ne '1') {
        Write-Host "skip:  database $db no longer exists"
        continue
    }
    Step "DROP DATABASE $db" {
        psql -h localhost -U postgres -d postgres -q -c "DROP DATABASE $db"
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  refused: psql reported an error. Left alone.'
            $script:failed = $true
        }
    }
}

# 2. The JauntyQ pin worktree, in the sibling repository
$jauntyq = Join-Path (Split-Path -Parent $repo) 'jauntyq'
$pin = Join-Path $jauntyq '.worktrees/nativeweb-build-2a005cb'
$pinFull = [System.IO.Path]::GetFullPath($pin)
$currentRoot = if ($env:JauntyQRoot) { [System.IO.Path]::GetFullPath($env:JauntyQRoot) } else { '' }
if (-not (Test-Path -LiteralPath $pin)) {
    Write-Host 'skip:  ../jauntyq/.worktrees/nativeweb-build-2a005cb no longer exists'
}
elseif (-not $RemoveJauntyPin) {
    Write-Host 'hold:  ../jauntyq/.worktrees/nativeweb-build-2a005cb is what nativeweb builds against; pass -RemoveJauntyPin with -Execute to remove it'
}
elseif ($currentRoot -eq '' -or $currentRoot.TrimEnd('\', '/') -eq $pinFull.TrimEnd('\', '/')) {
    Write-Host 'refuse: JauntyQRoot is unset or still names the pin, so removing it breaks every nativeweb build. Left alone.'
    $script:failed = $true
}
else {
    Step 'git -C ../jauntyq worktree remove .worktrees/nativeweb-build-2a005cb' {
        git -C $jauntyq worktree remove $pin
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  refused: the worktree holds changes. Left alone.'
            $script:failed = $true
        }
        else {
            git -C $jauntyq worktree prune
        }
    }
}

# 3. The merged branch
$target = 'feat/023-admin-relations-and-field-kinds'
if (-not (git -C $repo rev-parse --verify --quiet "refs/heads/$target")) {
    Write-Host "skip:  branch $target no longer exists"
}
else {
    Step "git branch -d $target" {
        git -C $repo branch -d $target
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  refused: not merged. Left alone.'
            $script:failed = $true
        }
    }
}

Write-Host ''
Write-Host 'Remaining sample databases:'
psql -h localhost -U postgres -d postgres -Atc "SELECT datname FROM pg_database WHERE datname LIKE 'nativeweb_sample%' ORDER BY 1"
Write-Host 'Remaining feat/023 branches:'
git -C $repo branch --list 'feat/023-*'

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run: nothing was changed. Pass -Execute to act.'
}
if ($Execute -and $script:failed) { exit 1 }
