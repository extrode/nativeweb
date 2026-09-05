<#
.SYNOPSIS
    Removes what fix/ci-credential-outside-workspace left behind. Dry run by default.

.DESCRIPTION
    The branch moved the GitHub Packages credential out of the checked-out nuget.config in
    all four workflow jobs that authenticate the feed. Every path is named in full; no globs.

    What it would remove:

      tmp/nugetprobe/          the scratch checkout used to establish, before any workflow was
                               touched, that `dotnet nuget update source --configfile` throws on
                               a file that does not declare the source while `add source` writes
                               cleanly outside the tree
      the branch fix/ci-credential-outside-workspace, only once merged into dev

    It does NOT touch ~/.nuget or any NuGet config on this machine: nothing here wrote one.
    Nothing on a runner needs cleaning either -- the whole point of the change is that the
    credential now lands on ephemeral runner state instead of in the workspace.

.PARAMETER Execute
    Actually remove. Without it, this only reports.
#>
[CmdletBinding()]
param(
    [Alias('e')]
    [switch]$Execute
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$probe = Join-Path $repo 'tmp/nugetprobe'
if (Test-Path $probe) {
    if ($Execute) { Remove-Item $probe -Recurse -Force; Write-Host 'removed  tmp/nugetprobe/' }
    else { Write-Host 'would remove  tmp/nugetprobe/' }
}
else {
    Write-Host 'absent   tmp/nugetprobe/'
}

$branch = 'fix/ci-credential-outside-workspace'
$merged = & git -C $repo branch --merged dev --format='%(refname:short)'
$all = & git -C $repo branch --format='%(refname:short)'
if ($merged -contains $branch) {
    if ($Execute) {
        & git -C $repo branch -d $branch
        # $ErrorActionPreference does not cover a native command's exit code, and git
        # refuses -d for a branch that is checked out.
        if ($LASTEXITCODE -ne 0) { Write-Host "FAILED   branch $branch (git exit $LASTEXITCODE)" }
        else { Write-Host "removed  branch $branch" }
    }
    else { Write-Host "would remove  branch $branch (merged into dev)" }
}
elseif ($all -contains $branch) { Write-Host "skipped  branch $branch (not merged into dev)" }
else { Write-Host "absent   branch $branch" }

if (-not $Execute) { Write-Host ''; Write-Host 'Dry run: nothing was changed. Pass -Execute to act.' }
