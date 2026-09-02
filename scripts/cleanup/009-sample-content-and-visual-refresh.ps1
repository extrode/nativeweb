<#
.SYNOPSIS
Removes what plan 009 (sample content and the public-site visual refresh) left behind.

.DESCRIPTION
Written 2026-09-03 at the end of feat/024-sample-content-and-visual-refresh. Bare invocation is a
dry run: it lists what would go and changes nothing. Pass -Execute (-e) to act.

Candidates, each checked at run time rather than asserted here:

  articles left in nativeweb_sample_test
                           50,641 rows on 2026-09-03, every one dated 2026 and left behind by
                           keyset paging runs and by three Stryker targets re-running the suite
                           once per mutant. The 017 cleanup removed 100,980 of these in
                           September and the table filled again, so this is a recurring residue
                           rather than one incident. Nothing reads them and no suite asserts a
                           count over this table, but they are why it cannot: an exact-set
                           assertion on the database path is impossible while they are there.

                           Needs -DropTestRows as well as -Execute, because a DELETE against a
                           table another agent may be paging through is not something to do by
                           default. The predicate spares the eight demo rows a1..a8 that
                           migration 0005 seeds, and spares anything created in the last thirty
                           minutes, so a suite running right now keeps its fixtures -- the same
                           age gate spec 019's feed sweep uses, and for the same reason.

  feat/024-sample-content-and-visual-refresh
                           the branch, once merged into dev. `git branch -d` is the merged
                           check: git compares commits and refuses an unmerged branch.

Not listed, and deliberately: the two local databases keep migration 0005's demo rows. They are
the content the sample is meant to serve, not residue. tmp/probe/009-*.py and tmp/009-e2e*.log
are this session's own scratch under the gitignored tmp/, which is mine to delete and not the
operator's to review.
#>
[CmdletBinding()]
param(
    [Alias('e')]
    [switch]$Execute,
    [switch]$DropTestRows
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

# 1. The leftover test rows
$predicate = @"
id NOT IN ('a1','a2','a3','a4','a5','a6','a7','a8')
AND created_at < now() - interval '30 minutes'
"@

$leftover = (psql -h localhost -U nativeweb_app -d nativeweb_sample_test -Atc "SELECT count(*) FROM articles WHERE $predicate").Trim()
if ($LASTEXITCODE -ne 0) {
    Write-Host '  refused: could not count rows in nativeweb_sample_test. Left alone.'
    $script:failed = $true
}
elseif ($leftover -eq '0') {
    Write-Host 'skip:  nativeweb_sample_test has no leftover article rows'
}
elseif (-not $DropTestRows) {
    Write-Host "hold:  nativeweb_sample_test has $leftover leftover article rows; pass -DropTestRows with -Execute to delete them"
}
else {
    Step "DELETE $leftover rows FROM articles IN nativeweb_sample_test" {
        psql -h localhost -U nativeweb_app -d nativeweb_sample_test -q -c "DELETE FROM articles WHERE $predicate"
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  refused: psql reported an error. Left alone.'
            $script:failed = $true
        }
    }
}

# 2. The merged branch
$target = 'feat/024-sample-content-and-visual-refresh'
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
Write-Host 'Rows now in nativeweb_sample_test.articles:'
psql -h localhost -U nativeweb_app -d nativeweb_sample_test -Atc "SELECT count(*) FROM articles"
Write-Host 'Remaining feat/024 branches:'
git -C $repo branch --list 'feat/024-*'

if (-not $Execute) {
    Write-Host ''
    Write-Host 'Dry run: nothing was changed. Pass -Execute to act.'
}
if ($Execute -and $script:failed) { exit 1 }
