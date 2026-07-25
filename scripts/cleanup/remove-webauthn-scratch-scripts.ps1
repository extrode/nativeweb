#!/usr/bin/env pwsh
# Removes throwaway repo-root scripts and log output created ad hoc while
# manually driving the WebAuthn e2e test outside the normal `dotnet test`
# invocation during Slice 4 debugging. Only ever touches these known
# repo-root filenames -- never anything under apps/, libs/, tests/, or db/.
$repoRoot = git rev-parse --show-toplevel
$scratchFiles = @(
    "run_all_e2e_tests.sh",
    "run_test.sh",
    "run_test_release.sh",
    "run_test_verbose.sh",
    "test_direct.sh",
    "test_output.txt"
)

foreach ($name in $scratchFiles) {
    $path = Join-Path $repoRoot $name
    if (Test-Path $path) {
        Write-Host "Removing scratch file: $path"
        Remove-Item $path -Force
    } else {
        Write-Host "Nothing to clean up: $path does not exist."
    }
}
