#!/usr/bin/env bash
#
# Builds the deployable NativeWeb artifact. Spec 011 T5 (R12, R22).
#
# RUN THIS IN WSL, NOT ON WINDOWS. NativeAOT does not cross-compile: a linux-x64
# publish has to happen on Linux, and the workstation this repo lives on is
# Windows. That is the whole reason this script exists as a separate step rather
# than being folded into deploy.sh.
#
#   wsl
#   cd /mnt/c/home/code/extrode.com/nativeweb
#   scripts/deploy/build.sh
#
# Output: dist/nativeweb-<short-sha>.tar.zst, containing the publish directory
# plus a RELEASE file naming the commit. deploy.sh reads that file to record what
# is running, which is how R12 is answered on the host without guessing.
#
# Env:
#   OUTPUT_DIR   default <repo>/dist
#   RUNTIME_ID   default linux-x64
#   PROJECT      default apps/sample/nativeweb.sample.csproj -- the project to
#                publish, relative to the repo root. A stamped product overrides
#                it with its own; nothing downstream of this script learns the
#                name, because the executable is installed under a fixed one (see
#                below). The old default was apps/web, which left the repo with
#                the identity provider on 2026-08-31 and took the only deployable
#                target with it: every run of this script since has failed on a
#                missing project file.
#
# Flags:
#   --force      build from a dirty working tree anyway (see below)

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUTPUT_DIR="${OUTPUT_DIR:-$REPO_ROOT/dist}"
RUNTIME_ID="${RUNTIME_ID:-linux-x64}"
PROJECT="${PROJECT:-apps/sample/nativeweb.sample.csproj}"

# The name the executable is installed under, inside the artifact and on the
# host. NOT the project's own name: systemd will not expand a variable in the
# first token of ExecStart, so nativeweb@.service has to name the binary
# literally -- and a unit file that has to be edited per product is a unit file
# that will be edited wrongly. One rename here buys a deploy chain that does not
# care which project produced the binary.
APP_BINARY="nativeweb"
FORCE=0

for arg in "$@"; do
	case "$arg" in
	--force) FORCE=1 ;;
	-h | --help)
		sed -n '2,25p' "${BASH_SOURCE[0]}"
		exit 0
		;;
	*)
		echo "build.sh: unknown argument '$arg'" >&2
		exit 2
		;;
	esac
done

cd "$REPO_ROOT"

if [[ ! -f "$PROJECT" ]]; then
	echo "build.sh: PROJECT '$PROJECT' does not exist under $REPO_ROOT." >&2
	echo "          Pass PROJECT=<path-to-csproj> for the app you mean to deploy." >&2
	exit 1
fi

if [[ "$(uname -s)" == MINGW* || "$(uname -s)" == MSYS* || "$(uname -s)" == CYGWIN* ]]; then
	echo "build.sh: this is a Windows shell. NativeAOT cannot cross-compile to $RUNTIME_ID --" >&2
	echo "          run this from WSL (or any Linux host) instead." >&2
	exit 1
fi

# Checked before the publish, not discovered after it. Found on the first real
# run: a Debian WSL image with no zstd installed compiled for four minutes and
# then failed on the last line, leaving a truncated artifact behind -- and
# `tar --zstd` wants the zstd BINARY, not a library, so nothing earlier notices.
# Ordered after the Windows-shell check on purpose: git-bash has no zstd either,
# and "missing zstd" is the wrong answer to "you are on the wrong OS".
MISSING=()
for tool in dotnet git tar zstd; do
	command -v "$tool" >/dev/null 2>&1 || MISSING+=("$tool")
done
if [[ "${#MISSING[@]}" -gt 0 ]]; then
	echo "build.sh: missing required tool(s): ${MISSING[*]}" >&2
	echo "          on Debian/Ubuntu:  sudo apt-get install -y ${MISSING[*]}" >&2
	exit 1
fi

# A dirty tree produces an artifact whose commit SHA describes something other
# than what is inside it. That is not a style objection: deploy.sh records the
# SHA as the answer to "what is running in production", and a stamp that is
# quietly wrong is worse than no stamp, because it will be trusted during an
# incident. Overridable, because a genuine emergency build is a real thing --
# but it has to be asked for.
if [[ -n "$(git status --porcelain)" ]]; then
	if [[ "$FORCE" -ne 1 ]]; then
		echo "build.sh: working tree is dirty. The artifact would be stamped with a commit" >&2
		echo "          that does not describe its contents. Commit, stash, or pass --force." >&2
		git status --short >&2
		exit 1
	fi
	echo "build.sh: WARNING -- building from a dirty tree because --force was passed." >&2
	echo "          The RELEASE stamp below will say so." >&2
fi

COMMIT="$(git rev-parse HEAD)"
SHORT_SHA="$(git rev-parse --short HEAD)"
BRANCH="$(git rev-parse --abbrev-ref HEAD)"
BUILT_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
DIRTY="clean"
[[ -n "$(git status --porcelain)" ]] && DIRTY="DIRTY"

PUBLISH_DIR="$(mktemp -d)"
trap 'rm -rf "$PUBLISH_DIR"' EXIT

echo "build.sh: publishing $PROJECT for $RUNTIME_ID at $SHORT_SHA ($DIRTY)"

# JauntyQUsePackages=false is stated rather than left to the default. It IS the
# default, but the alternative is a documented, still-unresolved gap -- no
# reachable feed publishes the JauntyQ.* packages (work/todo.md) -- and a deploy
# pipeline that inherited package mode by accident would fail to restore with an
# error about a feed rather than about a mode. R22 asks for the build mode to be
# documented rather than discovered; this is where it is documented.
dotnet publish "$PROJECT" \
	-c Release \
	-r "$RUNTIME_ID" \
	-p:JauntyQUsePackages=false \
	-o "$PUBLISH_DIR" \
	--nologo

# Install the executable under the fixed name the unit file starts. Checked
# rather than assumed: a publish whose assembly name differs from the project
# file name would leave the artifact with no launcher at all, and the first
# thing to notice would be ConditionPathExists silently skipping the unit on the
# host -- an inactive service that never says why.
PUBLISHED_EXE="$PUBLISH_DIR/$(basename "$PROJECT" .csproj)"
if [[ ! -f "$PUBLISHED_EXE" ]]; then
	echo "build.sh: the publish produced no executable named $(basename "$PUBLISHED_EXE")." >&2
	echo "          Either the project sets an AssemblyName that differs from its file name," >&2
	echo "          or the publish did not produce a native binary. Contents:" >&2
	ls -1 "$PUBLISH_DIR" >&2
	exit 1
fi
mv "$PUBLISHED_EXE" "$PUBLISH_DIR/$APP_BINARY"
chmod 0755 "$PUBLISH_DIR/$APP_BINARY"

# The stamp. Read by deploy.sh, and by a human on the host asking what is
# deployed. /version answers the same question from inside the process; this
# answers it without one running, which is the case that matters after a failed
# start.
cat >"$PUBLISH_DIR/RELEASE" <<EOF
commit=$COMMIT
short_sha=$SHORT_SHA
branch=$BRANCH
built_at=$BUILT_AT
built_by=$(id -un)@$(hostname)
runtime=$RUNTIME_ID
project=$PROJECT
tree=$DIRTY
EOF

# Migrations travel with the release. deploy.sh runs them from the staged
# directory, not from a checkout on the host -- there is no checkout on the
# host, and there should not be one.
cp -r db "$PUBLISH_DIR/db"

mkdir -p "$OUTPUT_DIR"
ARTIFACT="$OUTPUT_DIR/nativeweb-$SHORT_SHA.tar.zst"

# A tar that dies partway leaves a truncated file sitting at the exact path
# deploy.sh is told to ship, named for a real commit. Observed: 4096 bytes of a
# 40 MB artifact, and nothing downstream would have looked twice. Widen the trap
# to remove it, and narrow it again once the tar has returned 0.
trap 'rm -rf "$PUBLISH_DIR"; rm -f "$ARTIFACT"' EXIT
tar --zstd -cf "$ARTIFACT" -C "$PUBLISH_DIR" .
trap 'rm -rf "$PUBLISH_DIR"' EXIT

echo "build.sh: wrote $ARTIFACT ($(du -h "$ARTIFACT" | cut -f1))"
echo "build.sh: deploy it with  scripts/deploy/deploy.sh --artifact $ARTIFACT --host <host>"
