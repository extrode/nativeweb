# Spec 003 R36: measure a published binary's size and its time-to-/healthz, and
# fail CI when either exceeds the budget in publish-budgets.json. A null budget
# means record-only: print the measurement (it becomes the baseline candidate)
# and pass. Run from anywhere; paths are parameters.
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][string]$Name,
    [string]$BudgetFile = '',
    # 5998, matching what ci.yml passes. It used to default to 5999, which no
    # caller ever used: running the script by hand exercised a port the pipeline
    # never touches, so a port-specific failure could only show up in CI.
    [int]$Port = 5998,
    # The configuration section the target app binds NativeWebOptions from --
    # apps/sample passes "Sample" to FromConfiguration, so its telemetry switch
    # is Sample__Telemetry__Enabled and NOT NativeWeb__Telemetry__Enabled. Which
    # is what silently un-gated the OTel-under-AOT check: see the launch below.
    [string]$ConfigPrefix = 'Sample'
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 does not populate $PSScriptRoot in param defaults;
# resolve the sibling budget file here instead so both shells work.
if ([string]::IsNullOrEmpty($BudgetFile)) {
    $BudgetFile = Join-Path $PSScriptRoot 'publish-budgets.json'
}

if (-not (Test-Path $ExePath)) { throw "No published binary at $ExePath" }
$budgets = (Get-Content $BudgetFile -Raw | ConvertFrom-Json).$Name
if ($null -eq $budgets) { throw "No budget entry '$Name' in $BudgetFile" }

# --- 1. Binary size ---------------------------------------------------------
$bytes = (Get-Item $ExePath).Length
Write-Host ("{0}: binary size {1:N0} bytes ({2:N1} MB)" -f $Name, $bytes, ($bytes / 1MB))

# --- 2. Time to /healthz ----------------------------------------------------
# The app boots without any configuration (apps/sample degrades to its in-memory
# store when ConnectionStrings:Sample is empty), so a bare launch is enough for
# liveness. http, not https: no cert exists here and liveness has no cookie in
# play. This read "/readyz not-ready by design" while the target was apps/web;
# apps/sample has only /healthz.
$baseUrl = "http://127.0.0.1:$Port"
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = (Resolve-Path $ExePath).Path
$startInfo.WorkingDirectory = (Split-Path (Resolve-Path $ExePath).Path)
$startInfo.UseShellExecute = $false
$startInfo.EnvironmentVariables['ASPNETCORE_URLS'] = $baseUrl
$startInfo.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Development'
# Telemetry ON for this launch: this is the only place the OpenTelemetry
# registration runs inside a real NativeAOT image, so it is the only thing that
# would catch an AOT-hostile change to it.
#
# It stopped gating on 2026-08-31 and the reason was one word in a variable name.
# The key was NativeWeb__Telemetry__Enabled, which worked while the target was
# apps/web -- that app bound its options from a "NativeWeb" section. apps/sample
# binds from "Sample", so the variable matched nothing, TelemetryEnabled stayed
# false, and AddNativeWebTelemetry returned before registering anything. Nothing
# failed; the check just stopped checking. Hence -ConfigPrefix, and the repo-tier
# fact that holds it to what apps/sample actually passes to FromConfiguration.
#
# Measured on the published win-x64 binary, 2026-08-31, with a value the options
# validator rejects: Sample__Telemetry__Enabled=maybe refused startup with exit
# 78 and "Sample:Telemetry:Enabled must be true or false", while
# NativeWeb__Telemetry__Enabled=maybe started the app normally. The old key was
# not merely the wrong section; it was unread.
#
# No collector exists here; export failures are background warnings.
$startInfo.EnvironmentVariables["${ConfigPrefix}__Telemetry__Enabled"] = 'true'

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$process = [System.Diagnostics.Process]::Start($startInfo)
$startupMs = -1
try {
    while ($stopwatch.Elapsed.TotalSeconds -lt 60) {
        if ($process.HasExited) {
            throw "$Name exited with code $($process.ExitCode) before answering /healthz."
        }
        try {
            $response = Invoke-WebRequest -Uri "$baseUrl/healthz" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) { $startupMs = [int]$stopwatch.Elapsed.TotalMilliseconds; break }
        } catch { Start-Sleep -Milliseconds 50 }
    }
} finally {
    # Stop-Process rather than .Kill($true): the entire-tree Kill overload only
    # exists on .NET Core, and Windows PowerShell 5.1 runs on .NET Framework.
    # The app spawns no children, so a plain kill is enough. Null-guarded so a
    # failed Start doesn't mask its own error with one from the cleanup.
    if ($null -ne $process) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        $process.Dispose()
    }
}

if ($startupMs -lt 0) { throw "$Name never answered /healthz within 60s" }
Write-Host ("{0}: time-to-/healthz {1} ms" -f $Name, $startupMs)

# --- 3. Budget verdicts -----------------------------------------------------
$failed = $false
if ($null -ne $budgets.maxBytes) {
    if ($bytes -gt $budgets.maxBytes) {
        Write-Host ("::error::{0} binary is {1:N0} bytes, over the {2:N0} budget." -f $Name, $bytes, $budgets.maxBytes)
        $failed = $true
    }
} else {
    Write-Host "$($Name): no size budget yet (record-only)."
}
if ($null -ne $budgets.maxStartupMs) {
    if ($startupMs -gt $budgets.maxStartupMs) {
        Write-Host ("::error::{0} took {1} ms to /healthz, over the {2} ms budget." -f $Name, $startupMs, $budgets.maxStartupMs)
        $failed = $true
    }
} else {
    Write-Host "$($Name): no startup budget yet (record-only)."
}

if ($failed) { exit 1 }
