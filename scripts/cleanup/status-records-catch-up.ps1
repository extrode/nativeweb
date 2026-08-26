# status-records-catch-up.ps1 -- the branch left by the 2026-08-27 records catch-up.
#
# That work re-measured all eleven test tiers on dev at 1004220 (1,373 facts, 0 failed, 0 skipped,
# solution build 0 warnings), ran the twelfth project's publish gate by hand, and brought four
# stale records up to what dev actually contains:
#
#   - work/status/current.md carried 1,308 facts measured on feat/009-signin-consumer-seams on
#     2026-08-25. Replaced with the dev measurement and the per-tier account of the +65.
#   - CHANGELOG.md had no entry at all for feat/docs-site. Added one.
#   - work/status/previous.md and work/todo.md still named the pre-restructure flat doc paths.
#   - work/todo.md gained the finding that tests/nativeweb.signin.epass.tests.aot -- spec 008
#     T14's publish gate -- runs nowhere automatic.
#
# One branch, merged into dev at 356ecab. Nothing else to remove: the work touched four tracked
# markdown files and created no worktree, no scratch under tmp/, no build output and no stash.
#
# There is no second flag here because there is nothing irreversible to gate. `git branch -d` on a
# branch whose commits are in dev is recoverable from dev; that is the whole difference between it
# and the `-DropStash` in parked-docs-html.ps1, where the commit becomes unreachable.

param(
    [Alias('e')][switch]$Execute,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'
# Several checks below expect a non-zero exit rather than a throw; see
# ~/.claude/scripts/cleanup/README.md on why this is pinned instead of inherited.
$PSNativeCommandUseErrorActionPreference = $false

foreach ($a in $Rest) {
    switch ($a) {
        '--execute' { $Execute = $true }
        default     { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$repo = 'C:\home\code\extrode.com\nativeweb'
$failed = $false

# Gate on the current state, not on whether this script has already run. Being on dev is what
# says the branch below is merged; it does not become false by succeeding.
$branch = git -C $repo branch --show-current
if ($branch -ne 'dev') {
    if ($Execute) { Write-Error "$repo is on '$branch', expected 'dev'; refusing." }
    else { Write-Host "NOTE: $repo is on '$branch', expected 'dev' (execute would refuse)." }
}

function Act($desc, [scriptblock]$action) {
    if ($Execute) { Write-Host "DO   $desc"; & $action } else { Write-Host "would $desc" }
}

Write-Host '-- 1. merged branch (git branch -d refuses if unmerged)'
$target = 'docs/catch-up-records'
git -C $repo rev-parse --verify --quiet "refs/heads/$target" > $null
if ($LASTEXITCODE -ne 0) {
    Write-Host "  skip:  branch $target no longer exists"
} else {
    Act "git branch -d $target" {
        git -C $repo branch -d $target
        if ($LASTEXITCODE -ne 0) { Write-Host '  refused: not merged. Left alone.'; $script:failed = $true }
    }
}

Write-Host '-- remaining state'
git -C $repo branch --list 'docs/*' | Write-Host

if ($Execute -and $failed) { exit 1 }
exit 0
