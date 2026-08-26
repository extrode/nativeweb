#!/usr/bin/env bash
# Template. Copy into a consuming repo as scripts/build-docs.sh and edit the two marked lines.
#
# Builds the docs site using the shared Docs tool. See docs/docs/consuming-docs.md
# for what the input tree must look like and what the build needs installed.
set -euo pipefail
cd "$(dirname "$0")/.."

# --- edit these two ---------------------------------------------------------
SITE_NAME="nativeweb"          # brand shown in page titles, header and status bar
DOCS="../docs"            # path to the docs checkout, relative to this repo's root
# ----------------------------------------------------------------------------

INPUT="docs"                    # markdown tree: numbered section dirs, 00-*, 01-*, ...
OUTPUT="dist/docs-site"         # flat output, overwritten each build -- gitignore it

dotnet run --project "$DOCS/src/Docs" -- "$INPUT" "$OUTPUT" --site-name "$SITE_NAME"

# Versioned instead, if you want one frozen site per release under $OUTPUT. The version is read
# from THIS repo's git tag, and only a checked-out tag gets one -- anything else builds to
# unversioned/ rather than overwriting a release. See docs/docs/consuming-docs.md.
#
#   dotnet run --project "$DOCS/src/Docs" -- \
#     "$INPUT" "$OUTPUT" --site-name "$SITE_NAME" --versioned

# The build reports any link it dropped. A flat site cannot serve a path into a subdirectory, so
# those are unlinked to plain text rather than shipped dead -- read the list, it is a free audit.
