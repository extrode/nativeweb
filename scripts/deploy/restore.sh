#!/usr/bin/env bash
#
# Restores a backup into a database. Spec 011 T12 (R16, R17).
#
#   sudo scripts/deploy/restore.sh --target nativeweb_drill                    # dry run
#   sudo scripts/deploy/restore.sh --target nativeweb_drill --execute
#   sudo scripts/deploy/restore.sh --target nativeweb_drill --object 2026-08-27T031700Z.dump.age --execute
#
# Restoring over production additionally needs --overwrite-production, which is
# the second flag the repo-wide script interface requires for anything
# irreversible past --execute. There is no way to do it by accident, and that is
# the point: this script's normal use is the drill, not the emergency.
#
# THE DRILL IS THE REQUIREMENT. R16 is not met by this file existing -- it is met
# by a person having run it end to end against a scratch database, signed in as an
# account that existed at backup time, and written the elapsed time into the
# runbook. A restore procedure nobody has executed is a guess with a shebang.
#
# Config: /etc/nativeweb/backup.env (same file backup.sh reads)
#   BACKUP_AGE_IDENTITY   path to the age private key, default /etc/nativeweb/backup-age.key

set -euo pipefail

# Overridable for the same reason as backup.sh: the production guard below is
# worth driving for real, and a test cannot write to /etc.
CONFIG="${BACKUP_CONFIG:-/etc/nativeweb/backup.env}"
TARGET=""
OBJECT=""
EXECUTE=0
OVERWRITE_PRODUCTION=0
PRODUCTION_DB="${PRODUCTION_DB:-nativeweb}"

die() {
	echo "restore.sh: $*" >&2
	exit 1
}

while [[ $# -gt 0 ]]; do
	case "$1" in
	--target)
		TARGET="${2:-}"
		shift 2
		;;
	--object)
		OBJECT="${2:-}"
		shift 2
		;;
	--execute | -e)
		EXECUTE=1
		shift
		;;
	--overwrite-production)
		OVERWRITE_PRODUCTION=1
		shift
		;;
	-h | --help)
		sed -n '2,22p' "${BASH_SOURCE[0]}"
		exit 0
		;;
	*) die "unknown argument '$1'" ;;
	esac
done

[[ -n "$TARGET" ]] || die "--target <database> is required. Name the database explicitly; there is no default."
[[ -f "$CONFIG" ]] || die "missing $CONFIG"
# shellcheck disable=SC1090
set -a && . "$CONFIG" && set +a

: "${BACKUP_S3_BUCKET:?set BACKUP_S3_BUCKET in $CONFIG}"
: "${BACKUP_S3_ENDPOINT:?set BACKUP_S3_ENDPOINT in $CONFIG}"
AGE_IDENTITY="${BACKUP_AGE_IDENTITY:-/etc/nativeweb/backup-age.key}"

s3() { aws --endpoint-url "$BACKUP_S3_ENDPOINT" "$@"; }

# Matches backup.sh's derivation exactly -- `hostname -s` is GNU-only and the
# unit tier drives both scripts under git-bash. If these two ever disagree,
# restore.sh looks in a prefix backup.sh never wrote to and reports "no backups
# found" for a bucket that is full of them.
HOST_LABEL="$(hostname 2>/dev/null || echo unknown)"
HOST_LABEL="${HOST_LABEL%%.*}"
PREFIX="$BACKUP_S3_BUCKET/$HOST_LABEL"

# --- The production guard ----------------------------------------------------
if [[ "$TARGET" == "$PRODUCTION_DB" && "$OVERWRITE_PRODUCTION" -ne 1 ]]; then
	die "--target names the production database ($PRODUCTION_DB).
       Restoring over it destroys every row written since the backup was taken -- which during
       an incident is exactly what you want, and during a drill is the accident this guard
       exists to prevent.
       If you mean it: add --overwrite-production --execute.
       If you are drilling: pick a scratch name, e.g. --target ${PRODUCTION_DB}_drill."
fi

# --- Which object ------------------------------------------------------------
if [[ -z "$OBJECT" ]]; then
	# The naming scheme is date-sortable UTC precisely so "latest" is a sort and
	# not a metadata call: no API differences between providers to depend on.
	OBJECT="$(s3 s3 ls "$PREFIX/" | awk '{print $4}' | grep -E '\.dump\.age$' | sort | tail -1)"
	[[ -n "$OBJECT" ]] || die "no backups found under $PREFIX/"
fi

echo "restore.sh: object    $PREFIX/$OBJECT"
echo "restore.sh: target    $TARGET"
echo "restore.sh: identity  $AGE_IDENTITY"

if [[ "$EXECUTE" -ne 1 ]]; then
	echo
	echo "restore.sh: DRY RUN. Nothing was downloaded, decrypted or written."
	echo "            Re-run with --execute to restore."
	exit 0
fi

[[ -r "$AGE_IDENTITY" ]] || die "cannot read the age identity at $AGE_IDENTITY.
       Without it every object in the bucket is unreadable noise. The runbook records where the
       off-host copy is kept; if that copy has never been tested, this is the moment it was
       supposed to have been."

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
chmod 0700 "$WORK"

START="$(date +%s)"

echo "restore.sh: downloading"
s3 s3 cp "$PREFIX/$OBJECT" "$WORK/dump.age"

echo "restore.sh: decrypting"
age -d -i "$AGE_IDENTITY" -o "$WORK/dump" "$WORK/dump.age"

echo "restore.sh: recreating $TARGET"
# --clean --if-exists over dropping the database: the target may be in use by a
# drill app, and pg_restore's own object-level clean is more precise than
# dropdb/createdb, which fails outright on an open connection.
createdb "$TARGET" 2>/dev/null || true
pg_restore --dbname="$TARGET" --clean --if-exists --no-owner --no-privileges "$WORK/dump"

ELAPSED=$(($(date +%s) - START))

echo
echo "restore.sh: restored $OBJECT into $TARGET in ${ELAPSED}s."
echo
echo "restore.sh: THE RESTORE IS NOT VERIFIED YET. pg_restore exiting 0 means the rows loaded,"
echo "            not that the application works against them. Finish the drill:"
echo "              1. point a NativeWeb instance at $TARGET"
echo "              2. sign in as an account that existed when the backup was taken"
echo "              3. write ${ELAPSED}s, plus the time steps 1-2 took, into"
echo "                 docs/02-reference/deployment.md"
echo "            R16 is met by step 3, not by this script exiting 0."
