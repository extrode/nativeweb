# phase1-visible-ui.ps1 -- cleanup after Phase 1 of the framework-readiness plan
# (docs/plans/2026-08-31-007-framework-readiness.md), written at close-out on
# 2026-08-31 on the feat/phase1-visible-ui branch.
#
# Two bodies of work left build output behind:
#   - the identity provider's extraction on 2026-08-31 (decision 007), which
#     removed seventeen projects from source control and left their bin/ and
#     obj/ trees on disk, ~1.6 GB of it;
#   - this branch's own Release publishes of apps/sample.
#
# Dry run is the default and always exits 0; --execute / -e is the only way to
# act. Nothing here is irreversible: every path is a build output that the next
# build recreates, or a directory whose source content git no longer has. There
# is therefore no second flag.
#
# Every target is checked against git at run time rather than asserted here.
# A path git tracks is the repository saying that path is not scratch, and this
# script refuses it -- see ~/.claude/scripts/cleanup/README.md for the run that
# taught that lesson.
#
# Kept deliberately (do NOT add here):
#   - the branch. Per repo policy a branch is never deleted, not even by asking.
#   - apps/sample/Render/ArticlesPage.cs. It is TRACKED, and it is dead: no
#     production path has called it since the endpoints moved to compiled
#     templates, and the four tests that covered it were retargeted onto
#     Templates.Articles.Results. Removing it is a commit someone reviews, not a
#     cleanup, so it is a decision for the user rather than a target here. Said
#     out loud because a dead file that nothing references is exactly the kind
#     that survives by being invisible.
#   - tmp/. Scratch this session created under the project's own tmp/ is removed
#     as it is made and is not the user's to run a script for.

[CmdletBinding()]
param(
    [Alias('e')][switch]$Execute,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)

# PowerShell binds -Execute / -e; accept the POSIX spelling too so the
# interface is identical in both shells.
foreach ($a in $Rest) {
    switch ($a) {
        '--execute' { $Execute = $true }
        default { Write-Host "unknown argument: $a" -ForegroundColor Red; exit 2 }
    }
}

$ErrorActionPreference = 'Stop'
# Pinned rather than inherited: the tracked-ness check below expects a non-zero
# exit from git and must not have it thrown as a terminating error. The default
# has moved between PowerShell versions.
$PSNativeCommandUseErrorActionPreference = $false

$repo = 'C:\home\code\extrode.com\nativeweb'
$failed = $false

# Gate about the current state, not about this script's own success: the work
# must be merged before its build outputs are thrown away.
$branch = git -C $repo branch --show-current
if ($branch -ne 'dev') {
    if ($Execute) { Write-Error "$repo is on '$branch', expected 'dev'; refusing." }
    else { Write-Host "NOTE: $repo is on '$branch', expected 'dev' (execute would refuse)." }
}

$targets = @(
    # Projects that left with the identity provider on 2026-08-31. Source is
    # gone from git; only bin/ and obj/ remain.
    'apps\web',
    'libs\epass.auth',
    'libs\epass.data',
    'libs\epass.mail',
    'libs\epass.security',
    'libs\nativeweb.auth',
    'libs\nativeweb.data',
    'libs\nativeweb.devmarks.data',
    'libs\nativeweb.signin.epass',
    'libs\nativeweb.signin.epass.testing',
    'tests\nativeweb.signin.epass.tests.aot',
    'tests\nativeweb.signin.epass.tests.e2e',
    'tests\nativeweb.signin.epass.tests.integration',
    'tests\nativeweb.signin.epass.tests.unit',
    'tests\nativeweb.web.tests.e2e',
    'tests\nativeweb.web.tests.integration',
    'tests\nativeweb.web.tests.unit',

    # This branch's Release publishes, recreated by the next
    # `dotnet publish apps/sample -c Release` or `deno run -A tools/bench/run.ts`.
    'apps\sample\bin\Release',
    'apps\sample\obj\Release'
)

$total = 0

foreach ($rel in $targets) {
    $path = Join-Path $repo $rel

    if (-not (Test-Path $path)) {
        Write-Host "skip:    $rel (already gone)"
        continue
    }

    # The question this script must never answer from a comment.
    git -C $repo ls-files --error-unmatch -- $rel > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "refuse:  $rel is tracked by git. A cleanup script does not delete"
        Write-Host "         committed content; propose it as a commit instead. Left alone."
        $failed = $true
        continue
    }

    $size = (Get-ChildItem $path -Recurse -File -ErrorAction SilentlyContinue |
             Measure-Object -Property Length -Sum).Sum
    $mb = if ($size) { [math]::Round($size / 1MB, 1) } else { 0 }
    $total += $mb

    if ($Execute) {
        Remove-Item $path -Recurse -Force
        Write-Host "removed: $rel ($mb MB)"
    }
    else {
        Write-Host "would remove: $rel ($mb MB)"
    }
}

Write-Host ''
Write-Host ("total: {0} MB across {1} paths" -f [math]::Round($total, 1), $targets.Count)

if (-not $Execute) {
    Write-Host 'Dry run. Re-run with --execute to remove the paths above.'
}
elseif ($failed) {
    exit 1
}
