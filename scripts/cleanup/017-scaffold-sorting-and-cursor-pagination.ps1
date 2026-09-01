<#
.SYNOPSIS
Removes what spec 017 (scaffold sorting and cursor pagination) left behind.

.DESCRIPTION
Written 2026-09-01 by the 017 build. Bare invocation is a dry run: it lists what would go and
changes nothing. -Execute is the only way to act. Two further flags gate the two irreversible
things, and neither is implied by -Execute.

Section 1, under -Execute -- scratch this work created under the repository's own tmp/:

  tmp/probe-b64      a throwaway console project compiled to establish which System.Buffers.Text
                     .Base64Url members net10.0 actually ships, rather than asserting it.
  tmp/jq.log         output of `jaunty schema pull`, redirected to a file because the CLI printed
                     usage instead of running when the output was left on the console.
  tmp/del.trx        three trx logs, written only because `dotnet test`'s console logger swallowed
  tmp/int.trx        the assertion messages for the failures being read.
  tmp/kq.trx
  tmp/e2e3.log       run logs from the Playwright tier, the full-solution run and the NativeAOT
  tmp/full.log       publish gate, redirected so a long run could be read after it finished
  tmp/aot.log        rather than scrolled through as it went.
  tmp/017-code.diff  the production-only slice of the branch diff, handed to the code review.

  Each is checked against git at run time. Anything tracked is refused, not removed: git tracking
  a path is the repository saying that path is not scratch.

Section 2, needs -DropTestRows as well -- rows in the test database:

  The keyset suites insert 25 rows per test and nothing truncates, by the same discipline the
  rest of the integration tier follows: every test scopes itself with a unique marker, so a
  shared TRUNCATE is what breaks the moment two runs overlap. They accumulate. This removes only
  the two id prefixes those suites mint -- 'kq' from tests/nativeweb.scaffold.tests.integration
  and 'e2esort' from tests/nativeweb.scaffold.tests.e2e -- and leaves every other row, including
  the 'jq-' rows the older JauntyStoreTests writes and the demo rows.

  It acts on ConnectionStrings__Sample if set, otherwise the local dev database. It will not run
  against anything else, and it names the database it is about to write to before doing it.

Section 3, needs -RemoveSupersededQuery as well -- one tracked file:

  db/sample/queries/Articles/List.sql  superseded by the eight ListBy{Key,Title,Author,Created}
                                       {Asc,Desc}.sql files. It was left in place during the build
                                       because deleting a tracked file is a commit somebody
                                       reviews, not cleanup. Nothing calls the method it
                                       generates; the script checks that before staging it.

  This one stages a `git rm` and does NOT commit. You review the staged change and commit it, or
  `git reset` it.

The branch feat/017-scaffold-sorting-and-cursor-pagination is NOT touched. Branches are never
deleted here, not even behind a flag.

.EXAMPLE
pwsh -NoProfile scripts/cleanup/017-scaffold-sorting-and-cursor-pagination.ps1

.EXAMPLE
pwsh -NoProfile scripts/cleanup/017-scaffold-sorting-and-cursor-pagination.ps1 -e

.EXAMPLE
pwsh -NoProfile scripts/cleanup/017-scaffold-sorting-and-cursor-pagination.ps1 -e -DropTestRows
#>
[CmdletBinding()]
param(
    [Alias('e')]
    [switch]$Execute,

    [switch]$DropTestRows,

    [switch]$RemoveSupersededQuery
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Pinned rather than inherited: its default has moved between PowerShell versions, and every
# $LASTEXITCODE check below expects a non-zero exit to return rather than throw.
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$failed = $false

function Test-Tracked([string]$relative) {
    git -C $repoRoot ls-files --error-unmatch -- $relative > $null 2>&1
    return $LASTEXITCODE -eq 0
}

Write-Host "repo: $repoRoot"
Write-Host ''
Write-Host '1. scratch under tmp/'

$scratch = @(
    'tmp/probe-b64',
    'tmp/jq.log',
    'tmp/del.trx',
    'tmp/int.trx',
    'tmp/kq.trx',
    'tmp/e2e3.log',
    'tmp/full.log',
    'tmp/aot.log',
    'tmp/017-code.diff'
)

foreach ($relative in $scratch) {
    $path = Join-Path $repoRoot $relative

    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host "   skip:  $relative is already gone"
        continue
    }

    if (Test-Tracked $relative) {
        Write-Host "   refuse: $relative is tracked by git. A cleanup script does not delete"
        Write-Host '           committed content; propose it as a commit instead. Left alone.'
        $failed = $true
        continue
    }

    $bytes = (Get-ChildItem -Recurse -File -LiteralPath $path | Measure-Object -Property Length -Sum).Sum
    $kb = [math]::Round(([double]$bytes) / 1KB, 1)

    if ($Execute) {
        Remove-Item -Recurse -Force -LiteralPath $path
        Write-Host "   removed  $relative  ($kb KB)"
    }
    else {
        Write-Host "   would remove  $relative  ($kb KB)"
    }
}

Write-Host ''
Write-Host '2. test rows in the sample database'

$connection = $env:ConnectionStrings__Sample
if (-not $connection) {
    $connection = 'Host=localhost;Database=nativeweb_sample_test;Username=nativeweb_app'
}

if (-not $DropTestRows) {
    Write-Host '   skip:  -DropTestRows not given. Dropping rows is not implied by -Execute.'
    Write-Host "          target would be: $connection"
}
elseif (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
    Write-Host '   refuse: psql is not on PATH, so the row counts cannot be read or the rows'
    Write-Host '           removed. Left alone.'
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
    $predicate = "id LIKE 'kq%' OR id LIKE 'e2esort%'"

    $count = (psql $psqlUri -tAc "SELECT COUNT(*) FROM articles WHERE $predicate").Trim()
    if ($LASTEXITCODE -ne 0) {
        Write-Host "   refuse: could not read $($parts['database']). Left alone."
        $failed = $true
    }
    elseif ($count -eq '0') {
        Write-Host "   skip:  no keyset-suite rows in $($parts['database'])"
    }
    elseif ($Execute) {
        Write-Host "   database: $($parts['database']) on $($parts['host'])"
        psql $psqlUri -c "DELETE FROM articles WHERE $predicate" | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host '   refuse: the delete failed. Left alone.'; $failed = $true }
        else { Write-Host "   removed  $count row(s) from articles" }
    }
    else {
        Write-Host "   would remove  $count row(s) from articles in $($parts['database'])"
    }
}

Write-Host ''
Write-Host '3. the superseded query file'

$superseded = 'db/sample/queries/Articles/List.sql'
$supersededPath = Join-Path $repoRoot $superseded

if (-not (Test-Path -LiteralPath $supersededPath)) {
    Write-Host "   skip:  $superseded is already gone"
}
elseif (-not $RemoveSupersededQuery) {
    Write-Host '   skip:  -RemoveSupersededQuery not given. Staging a tracked deletion is not'
    Write-Host '          implied by -Execute.'
}
else {
    # Asked at run time. The claim "nothing calls it" is the whole justification, and a
    # justification written into a comment is one nobody re-checks.
    $callers = git -C $repoRoot grep -l -e 'Articles.ListAsync' -- '*.cs' 2>$null
    if ($callers) {
        Write-Host '   refuse: something still calls the method this file generates:'
        $callers | ForEach-Object { Write-Host "           $_" }
        $failed = $true
    }
    elseif ($Execute) {
        git -C $repoRoot rm --quiet -- $superseded
        if ($LASTEXITCODE -ne 0) { Write-Host '   refuse: git rm failed. Left alone.'; $failed = $true }
        else { Write-Host "   staged deletion of  $superseded  -- review and commit it, or git reset" }
    }
    else {
        Write-Host "   would stage deletion of  $superseded"
    }
}

Write-Host ''

if (-not $Execute) {
    Write-Host 'Dry run: nothing was changed. Pass -e to act, and -DropTestRows /'
    Write-Host '-RemoveSupersededQuery for the two things -e alone does not do.'
    exit 0
}

if ($failed) {
    Write-Host 'One or more items were refused and left alone. See above.'
    exit 1
}

Write-Host 'Done.'
