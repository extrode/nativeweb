#!/usr/bin/env bash
#
# Promotes a built artifact to production. Spec 011 T9 (R8, R9, R10, R12, R21).
#
#   scripts/deploy/deploy.sh --artifact dist/nativeweb-abc1234.tar.zst --host id.example.test
#
# Blue/green. The live colour keeps serving through every step below; the new
# release is staged into the idle colour, migrated, started and proved ready
# before Caddy is pointed at it. Any failure before the last step leaves
# production exactly as it was, which is R10 -- not "briefly down", not "down
# only if the binary is broken", but untouched.
#
# Two modes:
#   --host <name>   run from a workstation: copies the artifact and this script
#                   to <name> and re-executes there under sudo. The normal way.
#   --local         already on the host. What --host invokes remotely.
#
# Re-running with an artifact that is already live is a no-op and exits 0 (R8).
#
# Env (host side):
#   READY_TIMEOUT   seconds to wait for /readyz, default 60
#   RELEASES_KEEP   release directories to leave on disk, default 5

set -euo pipefail

BLUE_PORT=5011
GREEN_PORT=5012
RELEASES_DIR=/opt/nativeweb/releases
UPSTREAM_FILE=/etc/caddy/upstream.caddy
DEPLOY_LOG=/var/lib/nativeweb/deployments.log
READY_TIMEOUT="${READY_TIMEOUT:-60}"
RELEASES_KEEP="${RELEASES_KEEP:-5}"

ARTIFACT=""
HOST=""
LOCAL=0

die() {
	echo "deploy.sh: $*" >&2
	exit 1
}

while [[ $# -gt 0 ]]; do
	case "$1" in
	--artifact)
		ARTIFACT="${2:-}"
		shift 2
		;;
	--host)
		HOST="${2:-}"
		shift 2
		;;
	--local)
		LOCAL=1
		shift
		;;
	-h | --help)
		sed -n '2,24p' "${BASH_SOURCE[0]}"
		exit 0
		;;
	*) die "unknown argument '$1'" ;;
	esac
done

[[ -n "$ARTIFACT" ]] || die "--artifact is required"

# ---------------------------------------------------------------------------
# Workstation half: ship and re-execute.
# ---------------------------------------------------------------------------
if [[ "$LOCAL" -ne 1 ]]; then
	[[ -n "$HOST" ]] || die "pass --host <name> to deploy remotely, or --local if already on the host"
	[[ -f "$ARTIFACT" ]] || die "artifact not found: $ARTIFACT"

	remote_artifact="/tmp/$(basename "$ARTIFACT")"
	echo "deploy.sh: copying $(basename "$ARTIFACT") to $HOST"
	scp -q "$ARTIFACT" "$HOST:$remote_artifact"
	scp -q "${BASH_SOURCE[0]}" "$HOST:/tmp/nativeweb-deploy.sh"

	# The script ships with the artifact rather than living on the host, so the
	# deploy procedure and the release it deploys are always the same version.
	# A host-resident copy drifts from the repo silently.
	exec ssh -t "$HOST" \
		"sudo READY_TIMEOUT=$READY_TIMEOUT RELEASES_KEEP=$RELEASES_KEEP bash /tmp/nativeweb-deploy.sh --local --artifact $remote_artifact"
fi

# ---------------------------------------------------------------------------
# Host half.
# ---------------------------------------------------------------------------
[[ "$(id -u)" -eq 0 ]] || die "the host half must run as root (it writes /opt, /etc and restarts units)"
[[ -f "$ARTIFACT" ]] || die "artifact not found: $ARTIFACT"

# --- Preflight: the two settings whose absence is silent ---------------------
#
# Both of these produce a deployment that looks healthy and is not. Checked here
# rather than documented, because a documented check is one an operator can skip
# and this one has no symptom to notice later. R21, and the KnownProxies risk row
# in 011-plan.md.
preflight_config() {
	local combined
	combined="$(cat /etc/nativeweb/app.env /etc/nativeweb/secrets.env 2>/dev/null || true)"

	if ! grep -qE '^\s*NativeWeb__ForwardedHeaders__KnownProxies\s*=\s*\S' <<<"$combined"; then
		die "NativeWeb__ForwardedHeaders__KnownProxies is not set in /etc/nativeweb/.
       Behind Caddy, unset means ASP.NET Core trusts loopback only, RemoteIpAddress is the
       proxy hop, and every client in the world shares one rate-limit partition -- across
       sign-in, register and the OAuth endpoints. Nothing errors and nothing logs it.
       Set it to the proxy's address (127.0.0.1, ::1) and deploy again."
	fi

	if ! grep -qE '^\s*OTEL_EXPORTER_OTLP_ENDPOINT\s*=\s*\S' <<<"$combined" &&
		! grep -qiE '^\s*NativeWeb__Telemetry__Enabled\s*=\s*false\s*$' <<<"$combined"; then
		die "Telemetry has no destination and is not disabled.
       Telemetry is opt-OUT as of 2026-08-17: unset means ON, so this host would stand up an
       OTLP exporter and retry forever against a collector that does not exist.
       Set OTEL_EXPORTER_OTLP_ENDPOINT, or set NativeWeb__Telemetry__Enabled=false.
       Leaving it to the default is the one thing R21 does not allow."
	fi
}

# --- Which colour is live ----------------------------------------------------
live_colour() {
	local port
	port="$(grep -oE '127\.0\.0\.1:[0-9]+' "$UPSTREAM_FILE" 2>/dev/null | head -1 | cut -d: -f2 || true)"
	case "$port" in
	"$BLUE_PORT") echo blue ;;
	"$GREEN_PORT") echo green ;;
	# No upstream file yet: first deploy onto a freshly provisioned host. Treat
	# green as notionally live so the first release lands on blue, which makes
	# a fresh host and a long-running one read the same way in the log.
	*) echo green ;;
	esac
}

port_for() {
	case "$1" in
	blue) echo "$BLUE_PORT" ;;
	green) echo "$GREEN_PORT" ;;
	*) die "unknown colour '$1'" ;;
	esac
}

other_colour() {
	[[ "$1" == blue ]] && echo green || echo blue
}

release_sha_of() {
	# The colour's currently deployed commit, or empty if it has never served.
	sed -n 's/^short_sha=//p' "/opt/nativeweb/$1/RELEASE" 2>/dev/null || true
}

preflight_config

LIVE="$(live_colour)"
IDLE="$(other_colour "$LIVE")"
IDLE_PORT="$(port_for "$IDLE")"

# --- Read the artifact's stamp before unpacking anything ---------------------
STAGE="$(mktemp -d)"
cleanup() { rm -rf "$STAGE"; }
trap cleanup EXIT

tar --zstd -xf "$ARTIFACT" -C "$STAGE"
[[ -f "$STAGE/RELEASE" ]] || die "artifact has no RELEASE file -- was it built by scripts/deploy/build.sh?"
NEW_SHA="$(sed -n 's/^short_sha=//p' "$STAGE/RELEASE")"
[[ -n "$NEW_SHA" ]] || die "artifact RELEASE file has no short_sha"

# --- Idempotence (R8) --------------------------------------------------------
if [[ "$(release_sha_of "$LIVE")" == "$NEW_SHA" ]]; then
	echo "deploy.sh: $NEW_SHA is already live on $LIVE. Nothing to do."
	exit 0
fi

echo "deploy.sh: live=$LIVE  idle=$IDLE  deploying $NEW_SHA to $IDLE (port $IDLE_PORT)"

# --- Stage -------------------------------------------------------------------
RELEASE_DIR="$RELEASES_DIR/$NEW_SHA"
rm -rf "$RELEASE_DIR"
mkdir -p "$RELEASE_DIR"
cp -a "$STAGE/." "$RELEASE_DIR/"
chown -R root:nativeweb "$RELEASE_DIR"
chmod -R g-w,o-rwx "$RELEASE_DIR"
chmod 0755 "$RELEASE_DIR/nativeweb.web"

ln -sfn "$RELEASE_DIR" "/opt/nativeweb/$IDLE"

# --- Migrate (R9) ------------------------------------------------------------
#
# Runs while the LIVE release is still serving, which is the constraint that
# makes expand/contract mandatory: a migration that drops or renames something
# the running version reads takes production down between here and the reload.
# The runbook states the rule; this comment states why the script cannot enforce
# it. db/migrate.sh also holds locks for a whole file, so a long migration
# against a live database blocks readers and writers for its duration -- split
# it rather than assuming the transaction is free.
MIGRATIONS_BEFORE="$(sudo -u nativeweb psql -tAc 'select count(*) from schema_migrations' 2>/dev/null || echo 0)"

echo "deploy.sh: applying migrations"
if ! (cd "$RELEASE_DIR" && ./db/migrate.sh); then
	die "migrations failed. $LIVE is still serving and nothing was promoted.
       The database may be partially migrated -- db/migrate.sh applies each file with its
       schema_migrations row in one transaction, so a failure leaves the failed file
       unapplied and unrecorded, but files before it in the run are committed."
fi

MIGRATIONS_AFTER="$(sudo -u nativeweb psql -tAc 'select count(*) from schema_migrations' 2>/dev/null || echo 0)"
SCHEMA_CHANGED=0
[[ "$MIGRATIONS_AFTER" != "$MIGRATIONS_BEFORE" ]] && SCHEMA_CHANGED=1

# --- Start the idle colour and prove it is ready (R10) -----------------------
echo "deploy.sh: starting nativeweb@$IDLE"
systemctl restart "nativeweb@$IDLE"

ready=0
for ((i = 0; i < READY_TIMEOUT; i++)); do
	if [[ "$(curl -fsS --max-time 2 "http://127.0.0.1:$IDLE_PORT/readyz" 2>/dev/null || true)" == "ready" ]]; then
		ready=1
		break
	fi
	# A unit that has given up restarting will never become ready; waiting the
	# full timeout on it just delays the failure.
	if [[ "$(systemctl is-failed "nativeweb@$IDLE" || true)" == "failed" ]]; then
		break
	fi
	sleep 1
done

if [[ "$ready" -ne 1 ]]; then
	echo "deploy.sh: $IDLE did not become ready within ${READY_TIMEOUT}s. Last 40 log lines:" >&2
	journalctl -u "nativeweb@$IDLE" -n 40 --no-pager >&2 || true
	systemctl stop "nativeweb@$IDLE" || true
	die "deployment abandoned. $LIVE is still serving $(release_sha_of "$LIVE") and Caddy was never touched.
       Exit code 78 in the log above means a configuration error, not a crash -- see
       docs/02-reference/configuration.md."
fi

# --- Promote (the only step that changes what the public sees) ---------------
echo "deploy.sh: promoting $IDLE"
cat >"$UPSTREAM_FILE" <<EOF
# Generated by deploy.sh -- do not edit. Live colour: $IDLE ($NEW_SHA)
reverse_proxy 127.0.0.1:$IDLE_PORT
EOF

# Graceful: in-flight requests finish on $LIVE, new ones land on $IDLE.
systemctl reload caddy

printf '%s\tpromote\t%s\t%s\tschema_changed=%s\tprevious=%s\n' \
	"$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$IDLE" "$NEW_SHA" "$SCHEMA_CHANGED" "$(release_sha_of "$LIVE")" \
	>>"$DEPLOY_LOG"

echo "deploy.sh: $NEW_SHA is live on $IDLE."
if [[ "$SCHEMA_CHANGED" -eq 1 ]]; then
	echo "deploy.sh: this release applied migrations, so rollback.sh will refuse it (R11)."
else
	echo "deploy.sh: no migrations applied -- rollback.sh can return to $LIVE with a Caddy reload."
fi

# --- Prune old release directories ------------------------------------------
# The two live symlinks are never pruned regardless of age, because "keep 5" must
# not be able to delete the thing currently serving.
mapfile -t old < <(find "$RELEASES_DIR" -maxdepth 1 -mindepth 1 -type d -printf '%T@ %p\n' |
	sort -rn | tail -n "+$((RELEASES_KEEP + 1))" | cut -d' ' -f2-)
for dir in "${old[@]:-}"; do
	[[ -z "$dir" ]] && continue
	[[ "$dir" == "$(readlink -f /opt/nativeweb/blue)" ]] && continue
	[[ "$dir" == "$(readlink -f /opt/nativeweb/green)" ]] && continue
	echo "deploy.sh: pruning old release $(basename "$dir")"
	rm -rf "$dir"
done
