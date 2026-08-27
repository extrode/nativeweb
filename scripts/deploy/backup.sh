#!/usr/bin/env bash
#
# Nightly encrypted offsite backup of the production database. Spec 011 T11
# (R13, R14, R15).
#
#   sudo scripts/deploy/backup.sh            # dry run: says what it would do
#   sudo scripts/deploy/backup.sh --execute  # dump, encrypt, upload, prune
#
# Bare invocation is the dry run, per the repo-wide script interface. The timer
# passes --execute.
#
# pg_dump -Fc | age | S3-compatible object storage. age rather than the
# provider's server-side encryption because the threat this addresses is the
# provider (or a leaked access key), not a stolen disk -- an object nobody but
# this host's key holder can read is safe in a bucket that turns out to be public.
#
#   THE age KEY IS THE WHOLE THING. Lose it and every backup in the bucket is
#   noise. It must be stored somewhere that is neither this host nor this bucket.
#   The runbook says where; R16's restore drill is what proves the stored copy
#   works, and until that drill has been run this script's output is untested.
#
# Config: /etc/nativeweb/backup.env
#   BACKUP_S3_BUCKET        s3://bucket/prefix          (required)
#   BACKUP_S3_ENDPOINT      https://...                 (required; provider-neutral)
#   BACKUP_AGE_RECIPIENT    age1...                     (required)
#   BACKUP_RETENTION_DAYS   default 30
#   BACKUP_HEARTBEAT_URL    success ping                (optional -- see R15 below)
#   PGDATABASE/PGHOST/...   passed through to pg_dump

set -euo pipefail

# Overridable so the unit tier can drive the guards below against a scratch file
# rather than asserting them by reading this script. The default is the only
# value any host uses.
CONFIG="${BACKUP_CONFIG:-/etc/nativeweb/backup.env}"
EXECUTE=0

die() {
	echo "backup.sh: $*" >&2
	exit 1
}

for arg in "$@"; do
	case "$arg" in
	--execute | -e) EXECUTE=1 ;;
	-h | --help)
		sed -n '2,30p' "${BASH_SOURCE[0]}"
		exit 0
		;;
	*) die "unknown argument '$arg'" ;;
	esac
done

[[ -f "$CONFIG" ]] || die "missing $CONFIG"
# shellcheck disable=SC1090
set -a && . "$CONFIG" && set +a

: "${BACKUP_S3_BUCKET:?set BACKUP_S3_BUCKET in $CONFIG}"
: "${BACKUP_S3_ENDPOINT:?set BACKUP_S3_ENDPOINT in $CONFIG}"
: "${BACKUP_AGE_RECIPIENT:?set BACKUP_AGE_RECIPIENT in $CONFIG}"
RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-30}"
PGDATABASE="${PGDATABASE:-nativeweb}"

# The unit runs as root, and no PostgreSQL role called root exists. Left to the
# defaults, pg_dump used the local socket and peer-authenticated as `root`, so
# the nightly backup failed on the first run of every host ever provisioned --
# the timer would have gone red from day one. Named here, over the socket,
# through the pg_ident map provision.sh installs for exactly this.
export PGHOST="${PGHOST:-/var/run/postgresql}"
export PGUSER="${PGUSER:-nativeweb}"

# Short host name without `hostname -s`: that flag is GNU-only and the unit tier
# drives this script under git-bash, where it is a hard error rather than a
# fallback. Same result, no dependency on which hostname(1) is installed.
HOST_LABEL="$(hostname 2>/dev/null || echo unknown)"
HOST_LABEL="${HOST_LABEL%%.*}"

STAMP="$(date -u +%Y-%m-%dT%H%M%SZ)"
OBJECT="$BACKUP_S3_BUCKET/$HOST_LABEL/$STAMP.dump.age"

s3() { aws --endpoint-url "$BACKUP_S3_ENDPOINT" "$@"; }

# --- R15: silence must never read as success --------------------------------
#
# A backup that stops running produces exactly the same observable state as one
# that runs perfectly: an operator who is not looking sees nothing either way.
# The heartbeat inverts that -- an external watcher expects a ping on a schedule
# and alerts when it does not arrive, so the failure mode becomes "no ping",
# which is loud. The OnFailure= unit covers a backup that runs and fails; only
# the heartbeat covers one that never runs at all.
#
# Unset is allowed and SAID, because the alternative is a script that quietly
# leaves a requirement unmet.
heartbeat() {
	if [[ -z "${BACKUP_HEARTBEAT_URL:-}" ]]; then
		echo "backup.sh: NOTE -- BACKUP_HEARTBEAT_URL is unset, so R15 is NOT met on this host."
		echo "           A backup that silently stops running will not be noticed. Point it at an"
		echo "           external dead-man's-switch check to close that."
		return 0
	fi
	curl -fsS --max-time 10 "$BACKUP_HEARTBEAT_URL" >/dev/null ||
		echo "backup.sh: WARNING -- heartbeat ping failed. The backup succeeded; the monitor was not told." >&2
}

if [[ "$EXECUTE" -ne 1 ]]; then
	echo "backup.sh: DRY RUN -- nothing will be dumped, uploaded or deleted."
	echo "  database    $PGDATABASE"
	echo "  would write $OBJECT"
	echo "  encrypt to  $BACKUP_AGE_RECIPIENT"
	echo "  retention   $RETENTION_DAYS days; objects older than that would be deleted:"
	s3 s3 ls "$BACKUP_S3_BUCKET/$HOST_LABEL/" 2>/dev/null |
		awk -v cutoff="$(date -u -d "$RETENTION_DAYS days ago" +%Y-%m-%d)" \
			'$1 < cutoff { print "    " $4 }' || echo "    (bucket not listable from here)"
	heartbeat_note=""
	[[ -z "${BACKUP_HEARTBEAT_URL:-}" ]] && heartbeat_note=" (unset -- R15 unmet)"
	echo "  heartbeat   ${BACKUP_HEARTBEAT_URL:-none}$heartbeat_note"
	echo
	echo "backup.sh: re-run with --execute to perform the backup."
	exit 0
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
chmod 0700 "$WORK"

echo "backup.sh: dumping $PGDATABASE"
# -Fc: custom format, compressed and selectively restorable. Piped straight into
# age so the plaintext dump never lands on disk -- an unencrypted copy in /tmp,
# even briefly, is a copy of every credential this service holds.
pg_dump -Fc "$PGDATABASE" | age -r "$BACKUP_AGE_RECIPIENT" -o "$WORK/dump.age"

SIZE="$(stat -c %s "$WORK/dump.age")"
# A dump that is implausibly small usually means pg_dump failed and the pipe
# carried its error text into age, which encrypts it just as happily.
[[ "$SIZE" -gt 4096 ]] || die "encrypted dump is only $SIZE bytes -- treating that as a failed pg_dump, not a small database"

echo "backup.sh: uploading $OBJECT ($SIZE bytes)"
s3 s3 cp "$WORK/dump.age" "$OBJECT"

# --- Prune -------------------------------------------------------------------
CUTOFF="$(date -u -d "$RETENTION_DAYS days ago" +%Y-%m-%d)"
echo "backup.sh: pruning objects older than $CUTOFF"

# Listed into a variable first, NOT straight into `done < <(...)`. A process
# substitution's exit status is invisible to both `set -e` and `pipefail`, so a
# failed listing -- expired credentials, wrong endpoint, network -- produced an
# empty loop, a success heartbeat and "done.", while retention quietly stopped
# being enforced. That is the same silence-reads-as-success failure R15 exists to
# close, one layer down.
if ! LISTING="$(s3 s3 ls "$BACKUP_S3_BUCKET/$HOST_LABEL/")"; then
	die "the backup uploaded, but listing $BACKUP_S3_BUCKET/$HOST_LABEL/ for pruning failed.
       Not treating that as success: retention is not being enforced, and a prune that fails
       silently every night grows the bucket without bound. The dump itself is safe."
fi

while read -r key; do
	[[ -z "$key" ]] && continue
	echo "backup.sh: deleting $key"
	s3 s3 rm "$BACKUP_S3_BUCKET/$HOST_LABEL/$key"
done < <(awk -v cutoff="$CUTOFF" '$1 < cutoff { print $4 }' <<<"$LISTING")

heartbeat
echo "backup.sh: done."
