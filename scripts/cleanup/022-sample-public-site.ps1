<#
.SYNOPSIS
Removes what the spec 022 work (the sample's public site) left behind.

.DESCRIPTION
Written 2026-09-03 at the end of feat/022-sample-public-site. Bare invocation is a dry run: it
lists what would go and changes nothing. Pass -Execute (-e) to act.

Candidates, each checked at run time rather than asserted here:

  nativeweb_sample_022probe
                           a PostgreSQL database created 2026-09-02 to rehearse migration 0003
                           on a copy of nativeweb_sample_dev before it was applied for real.
                           Nothing reads it. Dropped through psql as the postgres superuser,
                           which is how db/sample/README.md creates the databases.

  36 tags in nativeweb_sample_test
                           rows the JauntySlugAndTagTests theory created before it learned to
                           delete its own: an eight-hex-digit slug and a name beginning "jq-".
                           Nothing asserts on them; they push the seeded tags off the admin's
                           first page, which is why JauntyStoreTests searches instead of
                           reading it. Needs -DropTestTags as well as -Execute, since it is a
                           DELETE on a database other work shares.

  feat/022-sample-public-site
                           the branch, once merged into dev. `git branch -d` is the merged
                           check: git compares commits and refuses an unmerged branch.

.worktrees/pin-67f42b8 is listed by scripts/cleanup/021-response-formats-and-negotiation.ps1
and is not repeated here. tmp/probe/022-* is the session's own scratch under the gitignored
tmp/ and is not listed.
#>
[CmdletBinding()]
param(
    [Alias('e')]
    [switch]$Execute,
    [switch]$DropTestTags
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

# 1. The rehearsal database
$probeDb = 'nativeweb_sample_022probe'
$exists = (psql -h localhost -U postgres -d postgres -Atc "SELECT 1 FROM pg_database WHERE datname = '$probeDb'").Trim()
if ($exists -ne '1') {
    Write-Host "skip:  database $probeDb no longer exists"
}
else {
    Step "DROP DATABASE $probeDb" {
        psql -h localhost -U postgres -d postgres -q -c "DROP DATABASE $probeDb"
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  refused: psql reported an error. Left alone.'
            $script:failed = $true
        }
    }
}

# 2. The leaked test tags
$tagWhere = "slug ~ '^[0-9a-f]{8}$' AND name LIKE 'jq-%'"
$count = (psql -h localhost -U nativeweb_app -d nativeweb_sample_test -Atc "SELECT count(*) FROM tags WHERE $tagWhere").Trim()
if ($count -eq '0') {
    Write-Host 'skip:  no leaked test tags in nativeweb_sample_test'
}
elseif (-not $DropTestTags) {
    Write-Host "hold:  $count leaked test tags in nativeweb_sample_test; pass -DropTestTags with -Execute to delete them"
}
else {
    Step "DELETE $count rows FROM tags in nativeweb_sample_test" {
        psql -h localhost -U nativeweb_app -d nativeweb_sample_test -q -c "DELETE FROM tags WHERE $tagWhere"
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  refused: psql reported an error. Left alone.'
            $script:failed = $true
        }
    }
}

# 3. The merged branch
$target = 'feat/022-sample-public-site'
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
Write-Host 'Remaining feat/022 branches:'
git -C $repo branch --list 'feat/022-*'

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run: nothing was changed. Pass -Execute to act.'
}
if ($Execute -and $script:failed) { exit 1 }
