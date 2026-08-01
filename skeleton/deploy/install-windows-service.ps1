# install-windows-service.ps1 -- register the published __PRODUCT__ binary as a
# Windows service (spec 003 R3, the Windows counterpart of __product__.service).
#
# Creates the service with sc.exe: delayed auto-start, restart-on-failure, and
# ASPNETCORE_* environment variables written to the service's registry
# Environment value (sc.exe create has no env argument, and neither does
# New-Service -- the REG_MULTI_SZ under
# HKLM\SYSTEM\CurrentControlSet\Services\<name> is how the SCM passes them).
#
# Removal (not done here -- this script only installs):
#   sc.exe stop __product__
#   sc.exe delete __product__
#
# Content root: a Windows service starts with its working directory set to
# %SystemRoot%\System32, not the directory of the binary, so wwwroot/ and
# appsettings*.json would not resolve. ASP.NET Core reads --contentRoot from
# the command line, so the binPath is
#   "<exe>" --contentRoot "<directory of exe>"
# which is the whole fix; no AppDirectory equivalent is needed.
#
# Dry run is the default: a bare invocation prints every action and changes
# nothing, and always exits 0. -Execute / -e is the only way to act, and an
# -Execute run exits non-zero if a precondition fails.
#
# Usage:
#   pwsh deploy/install-windows-service.ps1
#   pwsh deploy/install-windows-service.ps1 -Execute

[CmdletBinding()]
param(
    # The published single-file binary. Relative paths resolve against the
    # current directory, which is the repo root for a `dotnet publish -o publish`.
    [string]$ExePath = '.\publish\__product__.exe',
    [string]$ServiceName = '__product__',
    # Loopback only: a reverse proxy terminates TLS and forwards here. Set
    # __PRODUCT__:ForwardedHeaders:KnownProxies to the proxy address, or
    # X-Forwarded-For is ignored and every client shares one rate-limit partition.
    [string]$Urls = 'http://127.0.0.1:5000',
    [Alias('e')][switch]$Execute
)

$ErrorActionPreference = 'Stop'

$mode = if ($Execute) { 'EXECUTE' } else { 'DRY RUN (pass -Execute / -e to act)' }
Write-Host "== install-windows-service: $ServiceName [$mode] =="

# -- 1. preconditions -------------------------------------------------------

function Assert-Precondition([bool]$ok, [string]$message) {
    if ($ok) { return }
    if ($Execute) { Write-Error $message }
    Write-Host "NOTE: $message"
}

$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
Assert-Precondition $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) `
    'Creating a service requires an elevated shell.'

$exeFull = [System.IO.Path]::GetFullPath($ExePath, (Get-Location).Path)
Assert-Precondition (Test-Path -LiteralPath $exeFull -PathType Leaf) `
    "Published binary not found at $exeFull -- run dotnet publish apps/__product__ -c Release first."

Assert-Precondition (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) `
    "Service $ServiceName already exists -- stop and 'sc.exe delete $ServiceName' before reinstalling."

$contentRoot = Split-Path -Parent $exeFull
$binPath = "`"$exeFull`" --contentRoot `"$contentRoot`""

# ASPNETCORE_ENVIRONMENT is set explicitly: without it the app defaults to
# Production anyway, but the deployment should say so rather than rely on it.
# The connection string is deliberately NOT set here -- add
# __PRODUCT____Db__ConnectionString to the same registry value by hand, or
# supply it as a machine-level environment variable, so it never lands in a
# script under version control.
$environment = @(
    "ASPNETCORE_URLS=$Urls"
    'ASPNETCORE_ENVIRONMENT=Production'
)
$registryKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"

# -- 2. actions -------------------------------------------------------------

Write-Host "-- 1. sc.exe create $ServiceName binPath= $binPath start= delayed-auto"
Write-Host "-- 2. sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/60000"
Write-Host "-- 3. set $registryKey\Environment (REG_MULTI_SZ):"
foreach ($entry in $environment) { Write-Host "      $entry" }

if (-not $Execute) {
    Write-Host 'Dry run complete; nothing written.'
    exit 0
}

# sc.exe wants a space after each `option=`, and it does not set $? on failure,
# so every call is checked against $LASTEXITCODE.
function Invoke-Sc([string[]]$scArguments) {
    & sc.exe @scArguments | Write-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Error "sc.exe $($scArguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

# obj= NetworkService, not the LocalSystem default: the app needs no local
# privilege at all, only an outbound socket to Postgres. Built-in account, so
# no password argument is required.
Invoke-Sc @('create', $ServiceName, 'binPath=', $binPath, 'obj=', 'NT AUTHORITY\NetworkService', 'start=', 'delayed-auto', 'DisplayName=', '__PRODUCT__')
# Two fast restarts then a slow one, so a crash loop (bad connection string,
# port in use) backs off instead of hammering the SCM. reset= 86400 clears the
# failure count after a day of staying up.
Invoke-Sc @('failure', $ServiceName, 'reset=', '86400', 'actions=', 'restart/5000/restart/5000/restart/60000')

Set-ItemProperty -Path $registryKey -Name 'Environment' -Value $environment -Type MultiString

Write-Host "Installed $ServiceName (delayed auto-start, not started)."
Write-Host "Next: set __PRODUCT____Db__ConnectionString in $registryKey\Environment, then sc.exe start $ServiceName."
exit 0
