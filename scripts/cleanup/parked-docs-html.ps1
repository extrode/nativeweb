# parked-docs-html.ps1 -- drop the docs HTML stashed for the extrode.com move, 2026-08-27.
#
# stash@{0} "generated docs HTML, parked for the extrode.com move 2026-08-26" holds 19 files:
# 12 rendered pages, docs/index.html, and the assets they reference (artifact.css, artifact.js,
# LICENSE-fonts.txt and three plex woff2). All of it is build output, and three findings say it
# is safe to drop rather than land:
#
#   1. Every one of the 12 pages has a committed markdown source. Checked file by file --
#      configuration.md, template-diagnostics.md and the rest now live under docs/02-reference/
#      and the other numbered sections after the docs restructure; admin-cms-handoff.md is in
#      docs/handovers/. Nothing in the stash is a source.
#   2. The tool that wrote it, tools/html/build.js, no longer exists in this repo. Its artifact
#      hub is named in docs/specs/010-documentation-and-tutorials/010-spec.md:45 and in
#      scripts/cleanup/009-signin-consumer-seams.ps1:130, which left it alone as not 009's
#      business. This script is where that deferred decision lands.
#   3. The HTML is stale regardless. It was rendered from the flat docs/ tree that existed
#      before the restructure into 00-quick-start/, 01-architecture/, 02-reference/, so its
#      filenames and internal links describe a tree this repo no longer has.
#
# The docs site is now built by scripts/build-docs.sh into dist/docs-site, which .gitignore
# already covers at the `dist/` line. No new ignore rule was added: nothing in the repo writes
# HTML into docs/ any more, and `git ls-files docs | grep .html` is empty, so a rule would guard
# against a tool that has been deleted.
#
# Dropping a stash is irreversible in practice -- the commit is unreachable and gc takes it --
# so it sits behind its own second flag on top of --execute.

param(
    [Alias('e')][switch]$Execute,
    [switch]$DropStash,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'
# Several checks below expect a non-zero exit rather than a throw; see
# ~/.claude/scripts/cleanup/README.md on why this is pinned instead of inherited.
$PSNativeCommandUseErrorActionPreference = $false

foreach ($a in $Rest) {
    switch ($a) {
        '--execute'    { $Execute = $true }
        '--drop-stash' { $DropStash = $true }
        default        { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
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

Write-Host '-- 1. the parked stash'
# Found by its message, never by index. stash@{0} is positional: one `git stash` from any other
# piece of work pushes this entry to stash@{1} and the index would then name someone else's work.
$marker = 'generated docs HTML, parked for the extrode.com move'
$entry  = git -C $repo stash list | Select-String -SimpleMatch $marker | Select-Object -First 1

if (-not $entry) {
    Write-Host "  skip:  no stash matching '$marker'"
} else {
    $ref = ($entry.Line -split ':', 2)[0]
    Write-Host "  found $ref"
    if (-not $DropStash) {
        Write-Host '  would drop it (needs --drop-stash / -DropStash as well)'
        git -C $repo stash show --name-only $ref | ForEach-Object { Write-Host "    $_" }
    } else {
        Act "git stash drop $ref" {
            git -C $repo stash drop $ref
            if ($LASTEXITCODE -ne 0) { Write-Host '  refused. Left alone.'; $script:failed = $true }
        }
    }
}

Write-Host '-- 2. merged branch (git branch -d refuses if unmerged)'
$target = 'chore/parked-docs-html'
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
git -C $repo stash list | Write-Host
git -C $repo branch --list 'chore/*' | Write-Host

if ($Execute -and $failed) { exit 1 }
exit 0
