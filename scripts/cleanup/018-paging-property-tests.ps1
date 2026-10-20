<#
.SYNOPSIS
Removes the future-dated feed rows the 018 mutation triage left in the test database.

.DESCRIPTION
Written 2026-09-01 by the 018 build. Bare invocation is a dry run: it lists what would go and
changes nothing. -Execute is the only way to act, and dropping rows needs -DropTestRows as well,
which -Execute does not imply.

What this is about, because the residue is not obvious and it is not inert:

  tools/mutation-triage.ts runs Stryker over tests/nativeweb.scaffold.tests.integration. Stryker
  kills a test host it judges hung -- 39 of them in one measured run -- and a killed process
  never reaches the `finally` that removes its fixture rows. The feed suites date their rows
  five years into the future on purpose, because GET /articles shows only the newest twenty, so
  what is left behind sits at the top of the public feed for every later run.

  Measured 2026-09-01: a triage run ending 15:57 left five future-dated published rows in
  nativeweb_sample_test, and an hour later
  NativeWeb.Scaffold.Tests.E2e.PublicFeedTests.Feed_UnderNativeAot_OrdersNewestFirst failed on
  them -- it asserted an absolute DOM slot, and a leaked row had landed between its two rows.
  That assertion was the defect and is fixed on this branch; these rows are the residue.

  Since spec 019 (2026-09-02) the feed tests in both tiers sweep this same residue themselves
  before inserting -- the same predicate, plus an age gate of thirty minutes so a concurrent
  host's live rows are left alone. Running either feed suite clears it; this script is the
  fallback for looking without running one, or for a row younger than the gate.

  The predicate is narrow on purpose: only ids the two feed suites mint ('feed-' from the
  integration tier, 'e2e-' from the e2e tier) AND created_at in the future. A demo row, a 'jq-'
  row, a 'kq'/'e2esort' keyset row (those are the 017 script's business) and anything dated in
  the present are all left alone.

  It acts on ConnectionStrings__Sample if set, otherwise the local dev database, and it names the
  database before writing to it.

The branch test/018-paging-property-tests is NOT touched. Branches are never deleted here, not
even behind a flag.

.EXAMPLE
pwsh -NoProfile scripts/cleanup/018-paging-property-tests.ps1

.EXAMPLE
pwsh -NoProfile scripts/cleanup/018-paging-property-tests.ps1 -e -DropTestRows
#>
[CmdletBinding()]
param(
    [Alias('e')]
    [switch]$Execute,

    [switch]$DropTestRows
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Pinned rather than inherited: its default has moved between PowerShell versions, and the
# $LASTEXITCODE checks below expect a non-zero exit to return rather than throw.
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$failed = $false

Write-Host "repo: $repoRoot"
Write-Host ''
Write-Host '1. future-dated feed rows in the sample database'

$connection = $env:ConnectionStrings__Sample
if (-not $connection) {
    $connection = 'Host=localhost;Database=nativeweb_sample_test;Username=nativeweb_app'
}

$predicate = "created_at > now() AND (id LIKE 'feed-%' OR id LIKE 'e2e-%')"

if (-not $DropTestRows) {
    Write-Host '   skip:  -DropTestRows not given. Dropping rows is not implied by -Execute.'
    Write-Host "          target would be: $connection"
    Write-Host "          predicate would be: $predicate"
}
elseif (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
    Write-Host '   refuse: psql is not on PATH, so the rows cannot be read or removed. Left alone.'
    $failed = $true
}
else {
    # Parsed from the same connection string the suites use, so the script cannot act on a
    # database the tests never wrote to.
    $parts = @{}
    foreach ($pair in $connection.Split(';')) {
        if ($pair -match '^([^=]+)=(.*)$') { $parts[$matches[1].Trim().ToLowerInvariant()] = $matches[2].Trim() }
    }

    $psqlUri = "postgresql://$($parts['username'])@$($parts['host'])/$($parts['database'])"

    $rows = psql $psqlUri -tAc "SELECT id || '  ' || created_at FROM articles WHERE $predicate ORDER BY created_at"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "   refuse: could not read $($parts['database']). Left alone."
        $failed = $true
    }
    else {
        $listed = @($rows | Where-Object { $_ -and $_.Trim() })

        if ($listed.Count -eq 0) {
            Write-Host "   skip:  no future-dated feed rows in $($parts['database'])"
        }
        else {
            # Listed one per line rather than counted. A count says "five rows"; the ids say
            # which suite leaked and when, which is what tells you whether it happened again.
            $listed | ForEach-Object { Write-Host "          $_" }

            if ($Execute) {
                Write-Host "   database: $($parts['database']) on $($parts['host'])"
                psql $psqlUri -c "DELETE FROM articles WHERE $predicate" | Out-Null
                if ($LASTEXITCODE -ne 0) { Write-Host '   refuse: the delete failed. Left alone.'; $failed = $true }
                else { Write-Host "   removed  $($listed.Count) row(s) from articles" }
            }
            else {
                Write-Host "   would remove  $($listed.Count) row(s) from articles in $($parts['database'])"
            }
        }
    }
}

Write-Host ''

if (-not $Execute) {
    Write-Host 'Dry run: nothing was changed. Pass -e -DropTestRows to act.'
    exit 0
}

if ($failed) {
    Write-Host 'One or more items were refused and left alone. See above.'
    exit 1
}

Write-Host 'Done.'
