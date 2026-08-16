#!/usr/bin/env bash
# Removes throwaway console apps used for one-off investigation (reflecting over
# JauntyQ-generated signatures, spiking a candidate NuGet package's NativeAOT
# compatibility, etc.), created ad hoc under the OS temp dir so they never need a
# ProjectReference into this repo. Only ever touches temp-dir scratch copies --
# never anything inside the repo.
#
# Dry run is the default and always exits 0.
#   --execute | -e   act rather than print
#   --delete-dirs    (with --execute) actually remove them. Its own second flag
#                    because each removal is a recursive delete of a directory
#                    outside the repo, so --execute alone is a no-op on purpose.
#
# Retrofitted 2026-08-17: this predates the standard cleanup-script interface
# and used to rm -rf on a bare invocation, with no dry run and no way to ask
# what it would do.
set -euo pipefail

EXECUTE=0
DELETE_DIRS=0
for arg in "$@"; do
    case "$arg" in
        -e|--execute)  EXECUTE=1 ;;
        --delete-dirs) DELETE_DIRS=1 ;;
        *) echo "unknown argument: $arg" >&2; exit 2 ;;
    esac
done

TEMP_ROOT="${TEMP:-$HOME/AppData/Local/Temp}"
SCRATCH_DIRS=(
    "jq-inspect"
    "fido2-aot-spike"
    "fido2-reflect"
    "fido2-props-check"
    "fido2-props-check2"
    "fido2check3"
)

# Safety gate: a recursive delete keyed off an environment variable is only safe
# while that variable really points at a temp directory that exists.
if [ ! -d "$TEMP_ROOT" ]; then
    echo "TEMP_ROOT '$TEMP_ROOT' is not a directory; refusing." >&2
    [ "$EXECUTE" -eq 1 ] && exit 1
    exit 0
fi

found=0
for name in "${SCRATCH_DIRS[@]}"; do
    dir="$TEMP_ROOT/$name"
    if [ ! -d "$dir" ]; then
        echo "nothing to clean up: $name"
        continue
    fi

    found=$((found + 1))
    if [ "$EXECUTE" -eq 1 ] && [ "$DELETE_DIRS" -eq 1 ]; then
        echo "DO   remove $dir (recursive)"
        rm -rf "$dir"
    else
        echo "would remove $dir (--execute --delete-dirs)"
    fi
done

if [ "$found" -eq 0 ]; then
    echo 'Nothing left: this script has already been run.'
elif [ "$EXECUTE" -eq 1 ] && [ "$DELETE_DIRS" -eq 0 ]; then
    echo 'NOTE: --execute without --delete-dirs does nothing here, by design.'
fi

exit 0
