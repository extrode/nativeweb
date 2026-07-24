#!/usr/bin/env bash
# Removes the throwaway "jq-inspect" reflection scratch console app used to
# discover exact JauntyQ-generated method signatures (created ad hoc under the
# OS temp dir, referencing already-built repo DLLs via <HintPath> so it never
# needs a ProjectReference or its own copy of the generator). Only ever
# touches the temp-dir scratch copy -- never anything inside the repo.
set -euo pipefail

SCRATCH_DIR="${TEMP:-$HOME/AppData/Local/Temp}/jq-inspect"

if [ -d "$SCRATCH_DIR" ]; then
    echo "Removing scratch reflection tool: $SCRATCH_DIR"
    rm -rf "$SCRATCH_DIR"
else
    echo "Nothing to clean up: $SCRATCH_DIR does not exist."
fi
