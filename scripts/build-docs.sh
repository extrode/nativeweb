#!/usr/bin/env bash
# Builds the docs site using the shared Docs tool (sibling repo: ../docs). See that repo's
# docs/consuming-docs.md for what the input tree must look like and what the build needs
# installed.
#
# Versioned: the output is a root holding one frozen site per release, and the site is TRACKED --
# docs-site/ is committed, unlike the gitignored dist/docs-site this used to write. To publish a
# release's docs, check out its tag and run this. The version comes from `git describe
# --exact-match` on this repo, so only a checked-out tag gets a prefix; anything else lands in
# unversioned/ and never touches a release. Rebuilding an old version means checking out its tag
# again -- the tool refuses to overwrite a frozen release.
set -euo pipefail
cd "$(dirname "$0")/.."

SITE_NAME="nativeweb"           # brand shown in page titles, header and status bar
DOCS="../docs"                  # path to the docs checkout, relative to this repo's root

INPUT="docs"                    # markdown tree: numbered section dirs, 00-*, 01-*, ...
OUTPUT="docs-site"              # versioned root, committed -- one directory per release

dotnet run --project "$DOCS/src/Docs" -- "$INPUT" "$OUTPUT" --site-name "$SITE_NAME" --versioned

# The build reports any link it dropped. A flat site cannot serve a path into a subdirectory, so
# those are unlinked to plain text rather than shipped dead -- read the list, it is a free audit.
