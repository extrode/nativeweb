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
# Captured BEFORE the defaults are applied. Once a variable has been given its
# default it is always set, so a test made after the fact cannot tell an override
# from a default and the guard below would never fire. `:+` rather than `+`: an
# override to the empty string is not an override of anything, and deploy.sh --
# which re-executes itself with these forwarded -- depends on that reading.
PROJECT_SET="${PROJECT:+yes}"
CONFIG_PREFIX_SET="${CONFIG_PREFIX:+yes}"
DB_DIR_SET="${DB_DIR:+yes}"
MIGRATIONS_DIR_SET="${MIGRATIONS_DIR:+yes}"

PROJECT="${PROJECT:-apps/sample/nativeweb.sample.csproj}"

# The configuration section the binary reads, and the migrations that belong to
# it. Both are stamped into RELEASE below, because both are properties of the
# ARTIFACT rather than of the host: the prefix is the string compiled in at
# Program.cs's NativeWebOptions.FromConfiguration call, and the migrations are
# the ones this project's schema needs. deploy.sh's preflight asks "will THIS
# binary find its configuration", so the answer has to travel with the binary
# the way short_sha already does.
#
# A host-side CONFIG_PREFIX would be worse in a specific way: seed_config in
# provision.sh never overwrites, so a wrong first guess would persist forever,
# and the preflight would be validating a file that supplied its own validator.
CONFIG_PREFIX="${CONFIG_PREFIX:-Sample}"
DB_DIR="${DB_DIR:-db}"
MIGRATIONS_DIR="${MIGRATIONS_DIR:-db/sample/migrations}"

# The rendered documentation site, served at /docs by the host's Caddy. Unlike
# the three above this one is OPTIONAL and absent is not an error: a stamped
# product need not have run scripts/build-docs.sh, and refusing to release
# because nobody rendered the docs would be a strange thing for a build script to
# do. When the directory is missing the stamp below is empty and deploy.sh
# installs nothing, leaving whatever is already at /docs alone.
DOCS_DIR="${DOCS_DIR:-docs-site}"
DOCS_DIR="${DOCS_DIR%/}"

# Overriding PROJECT without the other three is the silent case: a stamped
# product would build its own binary and then be preflighted against Sample's
# key names, which pass while the binary reads none of them -- Consequence B of
# the 2026-08-31 review, rebuilt one layer up.
if [[ -n "$PROJECT_SET" ]]; then
	for name in CONFIG_PREFIX_SET DB_DIR_SET MIGRATIONS_DIR_SET; do
		if [[ -z "${!name}" ]]; then
			echo "ERROR: PROJECT is overridden but ${name%_SET} is not." >&2
			echo "       A product that builds its own binary and inherits Sample's configuration" >&2
			echo "       prefix or migrations passes a preflight that proves nothing. Set all three." >&2
			exit 1
		fi
	done
fi

# The stamp names the path INSIDE the artifact, which is not the path the operator
# gave: $DB_DIR arrives as db/ (see the cp below), so DB_DIR=database with
# MIGRATIONS_DIR=database/migrations used to stamp a directory no artifact
# contains. deploy.sh resolves the stamp release-relative and died on the host,
# after the whole NativeAOT publish. With the defaults the two coincide, which is
# why nothing caught it. Found by Fable, 2026-08-31.
# Both are repo-relative and neither keeps a trailing slash. DB_DIR=database/ is
# an ordinary way to type a directory, and it made the case below compare against
# `database//*`, which database/migrations does not match -- so the build refused
# a pair that was correct, with a message saying the opposite.
DB_DIR="${DB_DIR%/}"
MIGRATIONS_DIR="${MIGRATIONS_DIR%/}"

if [[ "$DB_DIR" = /* || "$MIGRATIONS_DIR" = /* || "$DOCS_DIR" = /* ]]; then
	echo "ERROR: DB_DIR, MIGRATIONS_DIR and DOCS_DIR are paths inside this repository, relative" >&2
	echo "       to its root. Got DB_DIR=$DB_DIR MIGRATIONS_DIR=$MIGRATIONS_DIR DOCS_DIR=$DOCS_DIR." >&2
	exit 1
fi

# Absent is fine; present-but-not-a-site is not. A DOCS_DIR that exists without a
# versions.json is either the wrong directory or a build that died partway, and
# shipping it would put a directory listing at /docs under the product's own
# hostname. Named rather than skipped, because the operator asked for it.
ARTIFACT_DOCS_DIR=""
if [[ -d "$REPO_ROOT/$DOCS_DIR" ]]; then
	if [[ ! -f "$REPO_ROOT/$DOCS_DIR/versions.json" ]]; then
		echo "ERROR: DOCS_DIR is $DOCS_DIR, which has no versions.json." >&2
		echo "       That is a versioned site's manifest, so this is not one. Run" >&2
		echo "       scripts/build-docs.sh, or set DOCS_DIR to a directory that has run it." >&2
		exit 1
	fi
	ARTIFACT_DOCS_DIR="docs"
fi

if [[ ! -d "$REPO_ROOT/$MIGRATIONS_DIR" ]]; then
	echo "ERROR: MIGRATIONS_DIR is $MIGRATIONS_DIR, which is not a directory in this repository." >&2
	exit 1
fi

case "$MIGRATIONS_DIR" in
"$DB_DIR"/*)
	ARTIFACT_MIGRATIONS_DIR="db/${MIGRATIONS_DIR#"$DB_DIR"/}"
	;;
*)
	echo "ERROR: MIGRATIONS_DIR is $MIGRATIONS_DIR, which is not under DB_DIR ($DB_DIR)." >&2
	echo "       Only \$DB_DIR travels in the artifact, so migrations outside it would not" >&2
	echo "       be there to apply." >&2
	exit 1
	;;
esac

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
		sed -n '2,31p' "${BASH_SOURCE[0]}"
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

# The source key that NuGet.config maps JauntyQ.* to. Named here rather than
# inlined into the message below because DeployTargetTests reads it back and
# compares it against that mapping: rename the source in NuGet.config alone and
# the remedy would tell an operator to credential a source that does not exist.
JAUNTYQ_FEED_SOURCE="jauntyq-github"
JAUNTYQ_FEED_URL="https://nuget.pkg.github.com/extrode/index.json"
# The user-level config, and the reason the hint below names it. `update source` with no
# --configfile writes to the nearest config that declares the source, which in a checkout of this
# repository is the committed nuget.config -- so the advice this script used to print put the
# operator's token in the working tree while telling them it was under $HOME.
JAUNTYQ_USER_CONFIG="$HOME/.nuget/NuGet/NuGet.Config"

# Restore before the publish, not inside it, for the same reason zstd is checked
# above: so the failure names its own fix.
#
# JauntyQ resolves from a private GitHub Packages feed by default since
# 2026-09-05, and GitHub Packages needs auth even to READ. This script runs in
# WSL, which is a different user profile from the Windows workstation the
# repository sits on -- so the credential a developer added on Windows does not
# apply here, and nothing in the repository can carry one. Measured 2026-09-05 on
# this machine: WSL restored 255 packages and then died on
#
#   error NU1301: Failed to retrieve information about 'JauntyQ.Generator'
#   error NU1301:   Response status code does not indicate success: 401 (Unauthorized).
#
# preceded by twelve identical "could not be authenticated" warnings, none of
# which says that the fix is one command in this shell.
#
# The flag is repeated from the publish below rather than shared through a
# variable, so that both lines read as the mode they select; DeployTargetTests
# asserts the two agree, because a preflight that validates a mode the publish
# does not use proves nothing. The RID and configuration are repeated for a
# sharper reason: a RID-less restore never asks for the NativeAOT ILCompiler
# packs, so it can pass on a machine where the publish then fails on exactly the
# error this block exists to intercept. The configuration is NOT repeated: the
# restore verb takes no -c, and passing it fails every run with MSB1001 "Unknown
# switch" before a single package is asked for (measured 2026-09-05). The publish below keeps its own restore
# rather than taking --no-restore -- ten seconds against a four-minute AOT
# compile, in exchange for a publish that does not depend on this block having
# run.
RESTORE_LOG="$(mktemp)"
echo "build.sh: restoring $PROJECT before the publish"
if ! dotnet restore "$PROJECT" -r "$RUNTIME_ID" -p:JauntyQUsePackages=true --nologo >"$RESTORE_LOG" 2>&1; then
	cat "$RESTORE_LOG" >&2
	# Matched on the authentication signal itself, not on a NuGet error code. NU1301
	# is the generic "could not retrieve information about" code: an offline shell, a
	# proxy or a nuget.org outage raises it too, and telling someone whose network is
	# down to go and fetch a token sends them the wrong way. Both strings below appear
	# in the measured failure, twelve times each; the second comes from the server
	# rather than the SDK, so it survives a non-English DOTNET_CLI_UI_LANGUAGE.
	if grep -qiE '401 \(Unauthorized\)|could not be authenticated by the GitHub Packages service' "$RESTORE_LOG"; then
		echo "" >&2
		echo "build.sh: the restore above could not read the JauntyQ packages." >&2
		echo "          They live on a private feed that needs a token even to READ, and this" >&2
		echo "          shell has its own NuGet profile -- a credential added on Windows does" >&2
		echo "          not reach it. Once, in THIS shell:" >&2
		echo "" >&2
		echo "            mkdir -p \"$(dirname "$JAUNTYQ_USER_CONFIG")\"" >&2
		echo "            [ -f \"$JAUNTYQ_USER_CONFIG\" ] || printf '%s' '<configuration />' > \"$JAUNTYQ_USER_CONFIG\"" >&2
		echo "            dotnet nuget add source $JAUNTYQ_FEED_URL \\" >&2
		echo "              --name $JAUNTYQ_FEED_SOURCE \\" >&2
		echo "              --username <your-github-account> --password <token> \\" >&2
		echo "              --store-password-in-clear-text \\" >&2
		echo "              --configfile \"$JAUNTYQ_USER_CONFIG\"" >&2
		echo "" >&2
		echo "          The token needs read:packages. A gh OAuth token works:" >&2
		echo "          gh auth refresh -h github.com -s read:packages && gh auth token" >&2
		echo "          It is stored in plain text in that file, under \$HOME and never in" >&2
		echo "          this checkout, so use a token scoped to reading packages and" >&2
		echo "          nothing else." >&2
	fi
	rm -f "$RESTORE_LOG"
	exit 1
fi
rm -f "$RESTORE_LOG"

COMMIT="$(git rev-parse HEAD)"
SHORT_SHA="$(git rev-parse --short HEAD)"
BRANCH="$(git rev-parse --abbrev-ref HEAD)"
BUILT_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
DIRTY="clean"
[[ -n "$(git status --porcelain)" ]] && DIRTY="DIRTY"

PUBLISH_DIR="$(mktemp -d)"
trap 'rm -rf "$PUBLISH_DIR"' EXIT

echo "build.sh: publishing $PROJECT for $RUNTIME_ID at $SHORT_SHA ($DIRTY)"

# JauntyQUsePackages=true is stated rather than left to the default. It IS the
# default since 2026-09-05, and the alternative no longer exists in a form this
# script could use: JauntyQ split its generator and runtime out to a public core
# repository, so checkout mode has no sibling to reference and would fail at
# EnsureJauntyQRoot. This line read =false until that day, which selected the
# mode that cannot restore. R22 asks for the build mode to be documented rather
# than discovered; this is where it is documented.
#
# Package mode still restores from whatever the machine's NuGet cache holds when
# the jauntyq-github feed is uncredentialed -- a documented, still-unresolved gap
# (work/todo.md, NuGet.config). A clean host gets NU1101 naming the feed.
dotnet publish "$PROJECT" \
	-c Release \
	-r "$RUNTIME_ID" \
	-p:JauntyQUsePackages=true \
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
# A product whose project is already named nativeweb.csproj publishes straight
# onto the target name, and `mv x x` fails with "are the same file" -- under
# set -e, after the whole NativeAOT publish has been paid for. Given the fixed
# name this script installs, that is a likely name for a product to pick.
if [[ "$PUBLISHED_EXE" != "$PUBLISH_DIR/$APP_BINARY" ]]; then
	mv "$PUBLISHED_EXE" "$PUBLISH_DIR/$APP_BINARY"
fi
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
config_prefix=$CONFIG_PREFIX
migrations_dir=$ARTIFACT_MIGRATIONS_DIR
docs_dir=$ARTIFACT_DOCS_DIR
tree=$DIRTY
EOF

# Migrations travel with the release. deploy.sh runs them from the staged
# directory, not from a checkout on the host -- there is no checkout on the
# host, and there should not be one.
#
# $DB_DIR, not a hardcoded `db`: this line used to copy THIS checkout's db/ no
# matter which project was being built, so a stamped product's artifact carried
# NativeWeb's sample schema and none of its own. The destination stays fixed at
# db/ inside the artifact, because deploy.sh resolves migrations_dir relative to
# the release directory and that layout is the artifact's contract.
cp -r "$DB_DIR" "$PUBLISH_DIR/db"

# The docs travel the same way and for the same reason: there is no checkout on
# the host. Fixed destination inside the artifact, like db/, so deploy.sh reads
# one layout regardless of where the operator keeps the source.
if [[ -n "$ARTIFACT_DOCS_DIR" ]]; then
	cp -r "$DOCS_DIR" "$PUBLISH_DIR/$ARTIFACT_DOCS_DIR"
fi

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
