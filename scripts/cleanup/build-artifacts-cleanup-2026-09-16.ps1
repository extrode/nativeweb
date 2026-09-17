#!/usr/bin/env pwsh
# build-artifacts-cleanup-2026-09-16.ps1
#
# Written 2026-09-16 during a machine-wide disk-space audit that found this
# project carrying 2.87 GB of build/tool artifacts (bin, obj, node_modules,
# dist, and similar directories -- see the list below). All of these are
# produced by the project's own build/install step and are safe to delete;
# each one is still re-checked against git at run time before being touched,
# so nothing tracked is ever removed even if this list goes stale.
#
# If your build layout changes (new artifact dirs, moved output paths,
# renamed projects), UPDATE THE LIST BELOW rather than leaving this stale --
# that is the whole point of keeping this script instead of doing this by
# hand once.
#
# Dry run is the default. --execute/-e authorises deletion.

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$Execute = $false
foreach ($a in $args) {
  switch ($a) {
    '--execute' { $Execute = $true }
    '-e'        { $Execute = $true }
    default {
      [Console]::Error.WriteLine("unknown argument: $a")
      [Console]::Error.WriteLine('usage: build-artifacts-cleanup-2026-09-16.ps1 [--execute|-e]')
      exit 2
    }
  }
}

$RepoRoot = (git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
if (-not $RepoRoot) {
  [Console]::Error.WriteLine('REFUSED: could not resolve the repo root from this script''s location.')
  exit 1
}
$RepoRoot = $RepoRoot.Trim() -replace '/', '\'

$Failed = $false
$script:Listed  = 0
$script:Removed = 0

function Mb($bytes) { '{0:N0} MB' -f ($bytes / 1MB) }

function Size($path) {
  if (-not (Test-Path -LiteralPath $path)) { return 0 }
  $item = Get-Item -LiteralPath $path
  if ($item.PSIsContainer) {
    return (Get-ChildItem -LiteralPath $path -Recurse -File -Force -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum
  }
  return $item.Length
}

function Drop($rel, $why) {
  $path = Join-Path $RepoRoot $rel
  if (-not (Test-Path -LiteralPath $path)) {
    Write-Host "   - $rel"
    Write-Host '     already gone'
    return
  }

  $gitRel = $rel -replace '\\', '/'
  git -C $RepoRoot ls-files --error-unmatch -- $gitRel *> $null
  if ($LASTEXITCODE -eq 0) {
    Write-Host "   - $rel"
    Write-Host "     REFUSED: tracked by git. Propose it as a commit instead. Left alone."
    $script:Failed = $true
    return
  }

  $bytes = Size $path
  Write-Host "   - $rel  ($(Mb $bytes))"
  Write-Host "     $why"
  $script:Listed += $bytes
  if ($Execute) {
    try {
      Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
      Write-Host '     removed'
      $script:Removed += $bytes
    } catch {
      Write-Host "     FAILED: $($_.Exception.Message)"
      $script:Failed = $true
    }
  }
}

Write-Host ''
Write-Host "extrode.com/nativeweb build-artifacts cleanup 2026-09-16   mode: $(if ($Execute) { 'EXECUTE' } else { 'dry run' })"
Write-Host ''

Drop 'apps/sample/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'apps/sample/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.signin.tests.aot/probe-host/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.scaffold.tests.e2e/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.signin.tests.e2e/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.forms.tests.e2e/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.permissions.tests.e2e/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'skeleton/tests/__product__.tests.e2e/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.signin.tests.aot/probe-host/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.web.tests.unit/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.render.tests.unit/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.scaffold.tests.integration/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.sample.tests.integration/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.permissions.tests.integration/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/probe/023f/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/probe/023claims/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/stamp015c/tests/stampcheck.tests.integration/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/adopt-probe/probe-tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.signin.tests.integration/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop '.worktrees/fix-signin-conformance-refresh-collision/tests/nativeweb.signin.tests.integration/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.sample.tests.unit/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/probe/m/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/stamp015-old/tests/stampcheck.tests.unit/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/stamp015c/tests/stampcheck.tests.unit/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/sectest/apps/sectest/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/probe-sec2/apps/sectest/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.signin.tests.unit/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.security.tests.unit/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.scaffold.tests.unit/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop '.worktrees/fix-signin-conformance-refresh-collision/tests/nativeweb.signin.tests.unit/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.permissions.tests.unit/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.forms.tests.unit/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/stamp015-old/apps/stampcheck/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/stamp015c/apps/stampcheck/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/adopt-probe/your-product/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/nuget-pkgs/netstandard.library/2.0.3/build' "build output; regenerated by the project's build step."
Drop 'tmp/fmtlit/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.mail.tests.unit/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.mail/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.repo.tests.unit/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.forms.tests.integration/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.signin.tests.aot/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.web.tests.unit/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.scaffold.tests.integration/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.signin.tests.unit/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tmp/wsl-publish-probe/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/t14-publish/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/samplepub/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/publish-028/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/probe/021-publish/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/perfprobe/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/perf/web/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/bench/publish/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/aot-no-msvc/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/aotcheck/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/stamp015-old/apps/stampcheck/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/stamp015-old/apps/stampcheck/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tmp/stamp015c/apps/stampcheck/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/shallow-probe/skeleton/apps/__product__/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/shallow-probe/apps/web/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/sectest/apps/sectest/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/sectest/apps/sectest/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tmp/probe-sec2/apps/sectest/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/probe-sec2/apps/sectest/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.sample.tests.unit/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.render.tests.unit/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'skeleton/apps/__product__/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'apps/sample/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop '.worktrees/pin-3bc0496/skeleton/apps/__product__/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop '.worktrees/pin-3bc0496/apps/sample/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop '.worktrees/fix-signin-conformance-refresh-collision/skeleton/apps/__product__/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop '.worktrees/fix-signin-conformance-refresh-collision/apps/sample/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop '.worktrees/epass-pin/skeleton/apps/__product__/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop '.worktrees/epass-pin/apps/sample/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'tmp/stamp015c/apps/stampcheck/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.sample.tests.integration/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.permissions.tests.integration/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop '.worktrees/fix-signin-conformance-refresh-collision/tests/nativeweb.signin.tests.unit/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.signin/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.repo.tests.unit/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.permissions.tests.unit/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.scaffold.tests.unit/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.signin.tests.integration/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tmp/t7probe/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.signin.testing/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.web/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tmp/probe/023claims/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.scaffold.tests.e2e/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tmp/probe/023f/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.forms.tests.unit/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop '.worktrees/fix-signin-conformance-refresh-collision/tests/nativeweb.signin.tests.integration/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.signin/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/stamp015c/tests/stampcheck.tests.integration/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.web/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/probe/m/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.security.tests.unit/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tmp/t7probe/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/adopt-probe/probe-tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.scaffold.generator/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop '.worktrees/pin-3bc0496/libs/nativeweb.signin/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.forms.tests.integration/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'skeleton/apps/__product__/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop '.worktrees/fix-signin-conformance-refresh-collision/libs/nativeweb.signin/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.render.generator/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tmp/adopt-probe/your-product/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tmp/stamp015c/tests/stampcheck.tests.unit/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.permissions/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.forms.tests.e2e/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.signin.tests.e2e/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.permissions.tests.integration/public-read-host/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.mail.tests.unit/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.permissions.tests.e2e/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop '.worktrees/pin-3bc0496/libs/nativeweb.web/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tmp/stamp015-old/tests/stampcheck.tests.unit/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.signin.testing/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'skeleton/tests/__product__.tests.integration/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.permissions.tests.integration/public-read-host/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'skeleton/tests/__product__.tests.unit/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop '.worktrees/fix-signin-conformance-refresh-collision/libs/nativeweb.signin.testing/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/nativeweb.signin.tests.aot/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.forms.generator/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.render/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tmp/fmtlit/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.permissions.generator/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop '.worktrees/epass-pin/libs/nativeweb.render/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'skeleton/tests/__product__.tests.e2e/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.permissions/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop '.worktrees/pin-3bc0496/libs/nativeweb.signin/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop '.worktrees/fix-signin-conformance-refresh-collision/libs/nativeweb.signin/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.security/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.scaffold/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.scaffold.generator/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.forms/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop '.worktrees/pin-3bc0496/libs/nativeweb.web/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop '.worktrees/fix-signin-conformance-refresh-collision/libs/nativeweb.signin.testing/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop '.worktrees/epass-pin/libs/nativeweb.security/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop '.worktrees/pin-3bc0496/libs/nativeweb.render.generator/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.mail/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.render.generator/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'skeleton/libs/__product__.data/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop '.worktrees/pin-3bc0496/libs/nativeweb.render/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.security/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop '.worktrees/pin-3bc0496/libs/nativeweb.security/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop '.worktrees/fix-signin-conformance-refresh-collision/libs/nativeweb.security/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tmp/nuget-pkgs/microsoft.dotnet.ilcompiler/10.0.7/build' "build output; regenerated by the project's build step."
Drop 'libs/nativeweb.render/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop '.worktrees/epass-pin/libs/nativeweb.render/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/stamp015-old/libs/stampcheck.data/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tmp/stamp015c/libs/stampcheck.data/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tmp/sectest/libs/sectest.data/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tmp/probe-sec2/libs/sectest.data/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.forms.generator/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop '.worktrees/pin-3bc0496/libs/nativeweb.render.generator/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop '.worktrees/epass-pin/libs/nativeweb.security/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.scaffold/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.forms/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'libs/nativeweb.permissions.generator/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'skeleton/libs/__product__.data/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/watchprobe/app/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop '.worktrees/pin-3bc0496/libs/nativeweb.render/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop '.worktrees/pin-3bc0496/libs/nativeweb.security/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop '.worktrees/fix-signin-conformance-refresh-collision/libs/nativeweb.security/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/stamp015-old/libs/stampcheck.data/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/stamp015c/libs/stampcheck.data/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/sectest/libs/sectest.data/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/probe-sec2/libs/sectest.data/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/nuget-pkgs/microsoft.net.illink.tasks/10.0.7/tools/netframework/build' "build output; regenerated by the project's build step."
Drop 'tmp/nuget-pkgs/microsoft.net.illink.tasks/10.0.7/tools/net/build' "build output; regenerated by the project's build step."
Drop 'tmp/nuget-pkgs/microsoft.net.illink.tasks/10.0.7/build' "build output; regenerated by the project's build step."
Drop 'tmp/t7probe/pub/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'libs/nativeweb.web/build' "build output; regenerated by the project's build step."
Drop '.worktrees/pin-3bc0496/libs/nativeweb.web/build' "build output; regenerated by the project's build step."
Drop '.worktrees/epass-pin/libs/nativeweb.web/build' "build output; regenerated by the project's build step."
Drop 'tmp/nuget-pkgs/jauntyq.generator/0.4.0/build' "build output; regenerated by the project's build step."
Drop '.worktrees/fix-signin-conformance-refresh-collision/libs/nativeweb.web/build' "build output; regenerated by the project's build step."
Drop 'tmp/nuget-pkgs/fido2/4.0.1/build' "build output; regenerated by the project's build step."
Drop 'tmp/watchprobe/app/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tmp/t7probe/wwwroot/vendor' "vendored dependency directory -- checked for git-tracking at runtime."
Drop 'skeleton/tests/__product__.tests.unit/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'skeleton/tests/__product__.tests.integration/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'skeleton/apps/__product__/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'bin' "MSBuild output; 'dotnet build' regenerates it."

Write-Host '--------'
if ($Execute) {
  Write-Host "Reclaimed: $(Mb $script:Removed)"
} else {
  Write-Host "Would reclaim: $(Mb $script:Listed)"
  Write-Host 'Pass --execute (or -e) to actually delete.'
}
Write-Host ''

if ($Execute -and $Failed) {
  Write-Host 'Finished with errors: see the refused steps above.'
  exit 1
}
exit 0
