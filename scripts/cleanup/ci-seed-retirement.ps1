# ci-seed-retirement.ps1 -- retire db/seed/ci, superseded 2026-08-17 by
# db/migrations/0055_first_party_application.sql.
#
# db/seed/ci existed for one reason: a migrations-only database had no first-party
# application row, so RealmResolver.ResolveFirstPartyAsync threw and every
# first-party realm resolution answered 500 (33 integration failures on CI).
# Migration 0055 now creates that row for EVERY deployment, which is where it
# always belonged -- production applies migrations and never applies a seed. CI
# runs migrations before the seed, so the seed's WHERE NOT EXISTS never fires:
# it is now a guarded no-op.
#
# Three things go together, and the order matters:
#   1. .github/workflows/ci.yml   the "Apply CI seed" step  <- a CODE EDIT, not this script
#   2. db/seed/ci/                the seed file and directory
#   3. tests/.../CiSeedTests.cs   asserts the workflow step exists
#
# THIS SCRIPT DOES 2 AND 3 ONLY. Step 1 is a workflow change that belongs in a
# branch with a green CI run behind it, not in a cleanup script -- and it must land
# FIRST. Deleting the seed while ci.yml still applies it breaks the build: the glob
# `db/seed/ci/*.sql` matches nothing, bash passes the literal pattern through, and
# psql fails on a file that does not exist. So section 1 below is a hard
# precondition that refuses to act until ci.yml no longer names the step.
#
# Verification behind the supersession, measured 2026-08-17 against a local
# database built exactly as ci.yml builds one:
#   every migration except 0055  ->  33 failed / 225 passed  (the exact CI set)
#   migrations only, with 0055   ->  258 / 258
#
# Dry run is the default and always exits 0. --execute / -e is the only way to act,
# and because this deletes tracked files it needs --delete-tracked on top.
#
# Kept deliberately (do NOT add here):
#   - db/seed/0001_dev_seed.sql. Still the developer seed, unaffected by any of this.
#   - db/seed/README.md. It documents the split AND records why ci/ went away; the
#     note is the point. Reword it in a branch if ci/ is gone, do not delete it.
#   - db/migrations/0055_first_party_application.sql. Obviously.

param(
    [Alias('e')][switch]$Execute,
    [switch]$DeleteTracked,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'

# Pins current behaviour rather than working around it ($false is already the
# default on pwsh 7.6.4): with the preference $true, the Stop above turns every
# non-zero git exit into a terminating error, and the grep-style probes below exit
# non-zero on their normal path.
$PSNativeCommandUseErrorActionPreference = $false

foreach ($a in $Rest) {
    switch ($a) {
        '--execute' { $Execute = $true }
        '--delete-tracked' { $DeleteTracked = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$repo = 'C:\home\code\extrode-legacy-org.com\NativeWeb'
$failed = $false

$branch = git -C $repo branch --show-current
if ($branch -ne 'dev') {
    if ($Execute) { Write-Host "ERROR: $repo is on '$branch', expected 'dev'; refusing." -ForegroundColor Red; exit 1 }
    else { Write-Host "NOTE: $repo is on '$branch', expected 'dev' (execute would refuse)." }
}

Write-Host '-- 1. precondition: ci.yml must no longer apply the seed'
# The whole reason this is a precondition and not a comment: getting the order
# wrong does not leave a tidy repo with one stale step, it leaves a red build.
$workflow = Join-Path $repo '.github\workflows\ci.yml'
$stillApplies = (Select-String -LiteralPath $workflow -SimpleMatch 'db/seed/ci/*.sql' -Quiet)
if ($stillApplies) {
    Write-Host 'BLOCKED ci.yml still applies db/seed/ci/*.sql.' -ForegroundColor Red
    Write-Host '        Remove the "Apply CI seed" step (and CiSeedTests with it) in a branch,'
    Write-Host '        let CI go green, merge, then run this.'
    if ($Execute) { exit 1 }
    Write-Host '        (dry run: continuing so the rest of the plan is visible)'
}
else {
    Write-Host 'ok    ci.yml no longer applies db/seed/ci'
}

Write-Host '-- 2. superseded files (needs --delete-tracked as well as --execute)'
# git rm rather than Remove-Item: these are tracked, and leaving the index
# disagreeing with the working tree is a worse state than either end of the change.
$tracked = @(
    'db/seed/ci/0001_first_party_application.sql',
    'tests/nativeweb.web.tests.unit/CiSeedTests.cs'
)
foreach ($f in $tracked) {
    git -C $repo ls-files --error-unmatch $f 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "skip  $f (not tracked; already gone)"
        continue
    }

    if ($Execute -and $DeleteTracked -and -not $stillApplies) {
        Write-Host "DO   git rm $f"
        git -C $repo rm --quiet $f
        if ($LASTEXITCODE -ne 0) {
            Write-Host "FAILED to remove $f" -ForegroundColor Red
            $failed = $true
        }
    }
    elseif ($Execute) {
        Write-Host "keep  $f (pass --delete-tracked to remove)"
    }
    else {
        Write-Host "would git rm $f (with --execute --delete-tracked)"
    }
}

Write-Host '-- 3. the now-empty directory'
# git rm leaves the directory behind when its last file goes. Only remove it if it
# really is empty -- a leftover file here is someone else's work, not this script's.
$seedDir = Join-Path $repo 'db\seed\ci'
if (-not (Test-Path $seedDir)) {
    Write-Host 'skip  db/seed/ci (already gone)'
}
elseif (@(Get-ChildItem -LiteralPath $seedDir -Force).Count -gt 0 -and -not ($Execute -and $DeleteTracked)) {
    Write-Host 'would remove db/seed/ci once empty (with --execute --delete-tracked)'
}
elseif ($Execute -and $DeleteTracked -and -not $stillApplies) {
    $left = @(Get-ChildItem -LiteralPath $seedDir -Force)
    if ($left.Count -gt 0) {
        Write-Host "keep  db/seed/ci ($($left.Count) unexpected file(s) left; not mine to delete)" -ForegroundColor Yellow
        $failed = $true
    }
    else {
        Write-Host 'DO   remove db/seed/ci'
        Remove-Item -LiteralPath $seedDir -Force
    }
}
else {
    Write-Host 'would remove db/seed/ci once empty (with --execute --delete-tracked)'
}

Write-Host '-- 4. what to do after this runs'
Write-Host '   - db/seed/README.md still describes ci/ in the present tense; reword it.'
Write-Host '   - work/todo.md carries the entry that scheduled this; tick it off.'
Write-Host '   - run the unit tier: CiSeedTests is gone, so 413 becomes 410.'

Write-Host '-- remaining state'
git -C $repo status --short | Write-Host

if (-not $Execute) { exit 0 }
if ($failed) { exit 1 }
