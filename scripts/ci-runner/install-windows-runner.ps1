<#
.SYNOPSIS
  Installs the self-hosted Windows runner for ci.yml's aot-publish job, as a Windows service.

.DESCRIPTION
  aot-publish links a NativeAOT binary with MSVC's link.exe, which this machine's Visual Studio
  provides. The job picks this runner when the repository variable CI_WINDOWS_RUNNER is set to
  nativeweb-windows; unset, it falls back to windows-latest. Decision:
  docs/decisions/2026-09-24-014-self-hosted-ci-runners.md.

  Dry run by default: prints what it would do and changes nothing. -Execute acts, and needs an
  elevated shell because config.cmd registers a service.

  The token is a registration token, valid for an hour and used once; it is passed through to
  config.cmd and never written anywhere:

    gh api -X POST repos/extrode/nativeweb/actions/runners/registration-token --jq .token

.EXAMPLE
  pwsh scripts/ci-runner/install-windows-runner.ps1 -Token <token>
  pwsh scripts/ci-runner/install-windows-runner.ps1 -Token <token> -Execute
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Token,
    [string] $Root = 'C:\actions-runner\nativeweb',
    [switch] $Execute
)

$ErrorActionPreference = 'Stop'

$version = '2.337.0'
$sha256 = '1150692afa94e71f872017e254ea55b6eece1eece3fe7e3a6d4c93d0a1b85cfc'
$zip = "actions-runner-win-x64-$version.zip"
$url = "https://github.com/actions/runner/releases/download/v$version/$zip"
$repo = 'https://github.com/extrode/nativeweb'
$name = 'nativeweb-windows'
$labels = 'nativeweb-windows'

$configArgs = @(
    '--unattended', '--replace',
    '--url', $repo,
    '--token', '<token>',
    '--name', $name,
    '--labels', $labels,
    '--runasservice'
)

if (-not $Execute) {
    Write-Host "dry run: nothing changes. Pass -Execute to act."
    Write-Host "  download  $url"
    Write-Host "  verify    sha256 $sha256"
    Write-Host "  extract   $Root"
    Write-Host "  configure $Root\config.cmd $($configArgs -join ' ')"
    exit 0
}

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'config.cmd --runasservice registers a Windows service: run this from an elevated shell.'
}

if (Test-Path (Join-Path $Root '.runner')) {
    throw "$Root already holds a configured runner. Remove it first: $Root\config.cmd remove --token <removal-token>."
}

New-Item -ItemType Directory -Force -Path $Root | Out-Null
$download = Join-Path $Root $zip
Invoke-WebRequest -Uri $url -OutFile $download

$actual = (Get-FileHash -Algorithm SHA256 -Path $download).Hash.ToLowerInvariant()
if ($actual -ne $sha256) {
    Remove-Item $download
    throw "sha256 mismatch for ${zip}: expected $sha256, got $actual."
}

Expand-Archive -Path $download -DestinationPath $Root -Force
Remove-Item $download

$configArgs[[array]::IndexOf($configArgs, '<token>')] = $Token
Push-Location $Root
try {
    & .\config.cmd @configArgs
    if ($LASTEXITCODE -ne 0) { throw "config.cmd exited $LASTEXITCODE." }
}
finally {
    Pop-Location
}

Write-Host "Registered $name. Point aot-publish at it with:"
Write-Host "  gh variable set CI_WINDOWS_RUNNER -R extrode/nativeweb -b $labels"
