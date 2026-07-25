#!/usr/bin/env bash
# Removes throwaway console apps used for one-off investigation (reflecting over
# JauntyQ-generated signatures, spiking a candidate NuGet package's NativeAOT
# compatibility, etc.), created ad hoc under the OS temp dir so they never need a
# ProjectReference into this repo. Only ever touches temp-dir scratch copies --
# never anything inside the repo.
set -euo pipefail

TEMP_ROOT="${TEMP:-$HOME/AppData/Local/Temp}"
SCRATCH_DIRS=(
    "jq-inspect"
    "fido2-aot-spike"
    "fido2-reflect"
    "fido2-props-check"
    "fido2-props-check2"
    "fido2check3"
)

for name in "${SCRATCH_DIRS[@]}"; do
    dir="$TEMP_ROOT/$name"
    if [ -d "$dir" ]; then
        echo "Removing scratch tool: $dir"
        rm -rf "$dir"
    else
        echo "Nothing to clean up: $dir does not exist."
    fi
done
