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
# Three modes:
#   --host <name>    run from a workstation: copies the artifact and this script
#                    to <name> and re-executes there under sudo. The normal way.
#                    --port <n> when sshd is not on 22; ssh_config's Port for the
#                    host is used when this is omitted, so a Host alias already
#                    carrying a Port needs nothing here.
#   --incus <inst>   run on an Incus host: pushes both into the guest <inst> and
#                    re-executes there. For a guest that runs no sshd because it
#                    is managed by `incus exec` -- decision 006.
#   --local          already on the host. What the other two invoke on the target.
#
# Re-running with an artifact that is already live is a no-op and exits 0 (R8).
#
# Env (host side):
#   READY_TIMEOUT   seconds to wait for /readyz, default 60
#   RELEASES_KEEP   release directories to leave on disk, default 5
#   MIGRATIONS_DIR  the release-relative migrations directory to apply. Normally
#                   unset: build.sh stamps it into the artifact's RELEASE file and
#                   this reads it from there. There is no default here any more.
#                   The old one was db/sample/migrations, this repository's own,
#                   so a stamped product that overrode PROJECT at build time and
#                   said nothing here had the demo articles table applied to its
#                   production database and its own migrations skipped, in
#                   silence. Setting this overrides the stamp.

set -euo pipefail

BLUE_PORT=5011
GREEN_PORT=5012
RELEASES_DIR=/opt/nativeweb/releases
UPSTREAM_FILE=/etc/caddy/upstream.caddy
DEPLOY_LOG=/var/lib/nativeweb/deployments.log
READY_TIMEOUT="${READY_TIMEOUT:-60}"
RELEASES_KEEP="${RELEASES_KEEP:-5}"
# Both now come off the artifact's RELEASE stamp, below. An explicit environment
# override still wins; what changed is that inheriting a default is no longer
# possible, because the default was this repository's own and a stamped product
# silently got it.
# `:+`, not `+`: this script re-executes ITSELF on the far side of both remote
# transports, and both forward MIGRATIONS_DIR unconditionally -- so the host half
# always sees the variable set, to the empty string when the operator overrode
# nothing. Under `+` that counted as an override, the RELEASE stamp was never
# read, and migrate.sh was handed the release root, where it dies on the compgen
# guard. Every --host and --incus deploy failed at the migrate step; only --local
# ever reached the stamp. Found by Fable, 2026-08-31.
MIGRATIONS_DIR_SET="${MIGRATIONS_DIR:+yes}"
MIGRATIONS_DIR="${MIGRATIONS_DIR:-}"
CONFIG_PREFIX=""

ARTIFACT=""
HOST=""
INSTANCE=""
LOCAL=0
SSH_PORT=""

die() {
	echo "deploy.sh: $*" >&2
	exit 1
}

# `shift 2` with one argument left returns 1, which under `set -e` exits with no
# message at all -- so `deploy.sh --artifact` used to die silently and the
# friendly "--artifact is required" below was unreachable. Take the operand only
# when there is one.
need_value() {
	[[ $# -ge 2 && -n "$2" ]] || die "$1 needs a value"
}

while [[ $# -gt 0 ]]; do
	case "$1" in
	--artifact)
		need_value "$@"
		ARTIFACT="$2"
		shift 2
		;;
	--host)
		need_value "$@"
		HOST="$2"
		shift 2
		;;
	--incus)
		need_value "$@"
		INSTANCE="$2"
		shift 2
		;;
	--port | -p)
		need_value "$@"
		SSH_PORT="$2"
		shift 2
		;;
	--local)
		LOCAL=1
		shift
		;;
	-h | --help)
		sed -n '2,28p' "${BASH_SOURCE[0]}"
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
	# Two transports that both end in `--local` on the target, so taking both would
	# deploy twice to two different places from one invocation. Rejected rather
	# than resolved by precedence: there is no reading of `--host a --incus b` that
	# is obviously what was meant.
	[[ -z "$HOST" || -z "$INSTANCE" ]] ||
		die "--host and --incus are alternative transports; pass one, not both"
	[[ -n "$HOST" || -n "$INSTANCE" ]] ||
		die "pass --host <name> to deploy over ssh, --incus <instance> to deploy into an Incus guest, or --local if already on the target"
	[[ -f "$ARTIFACT" ]] || die "artifact not found: $ARTIFACT"

	# --- SSH port ------------------------------------------------------------
	#
	# ssh takes -p, scp takes -P, and both must agree or the artifact and the
	# re-execution go to different places -- which on a host with a second sshd is
	# a deploy that half-happens. Built once here as two arrays rather than
	# interpolated at three call sites.
	#
	# Refused with --incus, which speaks the Incus control socket and opens no
	# connection a port could apply to. Silently ignoring it would let someone
	# believe they had targeted a port that was never consulted.
	ssh_port_args=()
	scp_port_args=()
	if [[ -n "$SSH_PORT" ]]; then
		[[ -n "$HOST" ]] || die "--port applies to the ssh transport; --incus takes no port"
		[[ "$SSH_PORT" =~ ^[0-9]+$ ]] && ((SSH_PORT >= 1 && SSH_PORT <= 65535)) ||
			die "--port takes a TCP port between 1 and 65535, got '$SSH_PORT'"
		ssh_port_args=(-p "$SSH_PORT")
		scp_port_args=(-P "$SSH_PORT")
	fi

	# --- Incus transport -----------------------------------------------------
	#
	# Same shape as the ssh half below -- private staging directory, ship the
	# artifact and this script, re-execute --local on the far side, remove the
	# staging directory whatever happens -- with `incus file push` and `incus exec`
	# in place of scp and ssh.
	#
	# No sudo here, and no deploy account: `incus exec` enters the guest as root
	# through the host's control socket. That is the whole reason this transport
	# exists rather than an sshd in every guest (decision 006) -- the privilege
	# already belongs to whoever can talk to Incus on the host.
	if [[ -n "$INSTANCE" ]]; then
		command -v incus >/dev/null ||
			die "incus not found. --incus runs ON the Incus host; from a workstation, ssh there first."

		# Checked before anything is pushed. `incus file push` into a stopped guest
		# succeeds -- it writes straight into the filesystem -- so without this the
		# artifact lands, `incus exec` fails with a message about the instance not
		# running, and a full release tarball is left inside a stopped guest.
		# The trailing `|| true` is what makes the next line reachable. Under
		# `set -euo pipefail` a command substitution whose pipeline fails takes the
		# whole script down at the assignment, so on a mistyped instance name this
		# died at exit 127 with zero bytes of output -- incus's own error eaten by
		# 2>/dev/null and the message below never printed. Measured, not reasoned:
		# the same shape in a scratch script exits 127 silently without it, and
		# prints the die with it.
		instance_state="$(incus info "$INSTANCE" 2>/dev/null | awk '$1 == "Status:" { print $2; exit }' || true)"
		[[ -n "$instance_state" ]] || die "no such Incus instance: $INSTANCE"
		[[ "$instance_state" == "RUNNING" ]] ||
			die "Incus instance $INSTANCE is $instance_state, not RUNNING"

		remote_dir="$(incus exec "$INSTANCE" -- mktemp -d /tmp/nativeweb-deploy.XXXXXXXX || true)"
		# One trailing CR, because the guest's mktemp may emit CRLF through
		# `incus exec`. There is no matching `%$'\n'` strip: `$(...)` already
		# removes every trailing newline, so that line was dead and its presence
		# implied a case that cannot arise.
		remote_dir="${remote_dir%$'\r'}"
		[[ -n "$remote_dir" ]] || die "could not create a staging directory in $INSTANCE"
		remote_artifact="$remote_dir/$(basename "$ARTIFACT")"

		echo "deploy.sh: pushing $(basename "$ARTIFACT") into $INSTANCE:$remote_dir"

		# Everything from the first push onward runs under one status capture, not
		# just the deploy step. A failed push -- guest disk full part-way through a
		# multi-hundred-MB artifact is the realistic mode -- used to exit through
		# errexit BETWEEN mktemp and the cleanup, leaving a partial artifact in the
		# staging directory: precisely the leak this block was written to prevent,
		# reachable on exactly the runs where the operator retries and leaves
		# another. `|| status=$?` rather than a bare call followed by `status=$?`,
		# because under `set -e` the bare form never reaches the assignment.
		status=0
		{
			# `incus file push <src> <instance>/<abs-path>` -- the instance name and
			# the path are one argument, and $remote_dir already begins with a slash.
			incus file push "$ARTIFACT" "$INSTANCE$remote_artifact" &&
				incus file push "${BASH_SOURCE[0]}" "$INSTANCE$remote_dir/deploy.sh" &&
				incus exec "$INSTANCE" \
					--env READY_TIMEOUT="$READY_TIMEOUT" \
					--env RELEASES_KEEP="$RELEASES_KEEP" \
					--env MIGRATIONS_DIR="$MIGRATIONS_DIR" \
					-- bash "$remote_dir/deploy.sh" --local --artifact "$remote_artifact"
		} || status=$?
		incus exec "$INSTANCE" -- rm -rf "$remote_dir" || true
		exit $status
	fi

	# --- SSH transport -------------------------------------------------------

	# A private staging directory, not /tmp directly. Root fetches and executes
	# this script on the far side, and a predictable path in a world-writable
	# directory is an arbitrary-root-execution window for any local user who wins
	# the race between the scp and the sudo -- or who pre-creates the name as a
	# symlink and redirects the write. mktemp -d is 0700 and unguessable.
	remote_dir="$(ssh "${ssh_port_args[@]}" "$HOST" 'mktemp -d /tmp/nativeweb-deploy.XXXXXXXX')"
	[[ -n "$remote_dir" ]] || die "could not create a staging directory on $HOST"
	remote_artifact="$remote_dir/$(basename "$ARTIFACT")"

	echo "deploy.sh: copying $(basename "$ARTIFACT") to $HOST:$remote_dir"
	scp -q "${scp_port_args[@]}" "$ARTIFACT" "$HOST:$remote_artifact"
	scp -q "${scp_port_args[@]}" "${BASH_SOURCE[0]}" "$HOST:$remote_dir/deploy.sh"

	# The script ships with the artifact rather than living on the host, so the
	# deploy procedure and the release it deploys are always the same version.
	# A host-resident copy drifts from the repo silently.
	#
	# Quoted on the remote side as well: an artifact path containing a space used
	# to split into two arguments there, and the staging directory is generated
	# rather than chosen. The directory is removed afterwards -- each deploy used
	# to leave a full artifact in /tmp for ever.
	# `sudo VAR=value cmd` is not a way to pass configuration: Debian's default
	# sudoers runs with `env_reset` and no `SETENV` tag, so sudo refuses the
	# assignment and exits before anything is staged -- on every run of the
	# documented path, not an edge case. `sudo env VAR=value cmd` is the portable
	# form: sudo sees only `env` as the command, and env does the assigning on the
	# far side of the privilege boundary.
	exec ssh -t "${ssh_port_args[@]}" "$HOST" \
		"sudo env READY_TIMEOUT=$(printf %q "$READY_TIMEOUT") RELEASES_KEEP=$(printf %q "$RELEASES_KEEP") \
		 MIGRATIONS_DIR=$(printf %q "$MIGRATIONS_DIR") \
		 bash $(printf %q "$remote_dir/deploy.sh") --local --artifact $(printf %q "$remote_artifact"); \
		 status=\$?; rm -rf $(printf %q "$remote_dir"); exit \$status"
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

	# The keys provision.sh seeds EMPTY, which is the case these two guards used
	# to miss entirely: they checked the two keys provision.sh seeds populated, so
	# they were pre-satisfied on every provisioned host and could never fire, while
	# the blank ones sailed through. An EnvironmentFile line with nothing after the
	# `=` sets the variable to the empty string rather than leaving it unset, so
	# `?? "localhost"` in Program.cs does NOT rescue it -- WebAuthn ends up with an
	# empty relying-party domain and every credential fails at verification time,
	# with /readyz still saying ready.
	# EVERY key name below is built from $CONFIG_PREFIX, read off the artifact.
	# The ten keys this loop used to demand all carried a NativeWeb prefix, and
	# nine of them are read by no code in this repository at all -- they belonged
	# to the identity provider that left on 2026-08-31. The tenth was spelled for
	# a section the binary does not bind. So the gate hard-failed every deploy on
	# values nothing would ever read, and the two checks that DID matter were
	# spelled the same wrong way: they passed while the settings they name stayed
	# unset. A preflight that passes when the thing it prevents is happening is
	# worse than no preflight, because it is also an assurance.
	if ! grep -qE "^\s*${CONFIG_PREFIX}__ForwardedHeaders__KnownProxies\s*=\s*\S" <<<"$combined"; then
		die "${CONFIG_PREFIX}__ForwardedHeaders__KnownProxies is not set in /etc/nativeweb/.
       Behind Caddy, unset means ASP.NET Core trusts loopback only, RemoteIpAddress is the
       proxy hop, and every client in the world shares one rate-limit partition. Nothing
       errors and nothing logs it.
       Set it to the proxy's address (127.0.0.1, ::1) and deploy again."
	fi

	# Either spelling: the reference app reads ConnectionStrings:<Prefix>, a
	# stamped product reads <Prefix>:Db:ConnectionString. Absent, the reference app
	# serves its in-memory demo store with a green /healthz and a product times out
	# the /readyz poll with no named cause.
	if ! grep -qE "^\s*ConnectionStrings__${CONFIG_PREFIX}\s*=\s*\S" <<<"$combined" &&
		! grep -qE "^\s*${CONFIG_PREFIX}__Db__ConnectionString\s*=\s*\S" <<<"$combined"; then
		die "no database connection string in /etc/nativeweb/.
       Set ConnectionStrings__${CONFIG_PREFIX} or ${CONFIG_PREFIX}__Db__ConnectionString.
       Without one this host answers /healthz green while serving no database at all."
	fi

	# The reference app's admin is behind a passphrase outside Development (spec 020),
	# and a Production binary refuses to start without one -- so this check is not
	# what keeps the host up. It is what names the cause here, at promote time,
	# rather than in a journal line after the new colour has already failed /readyz.
	# Only for the reference app, whose prefix is Sample: a stamped product signs in
	# through its OAuth provider and reads no such key, and a gate on a value nothing
	# reads is the failure the comment above this function describes.
	if [[ "$CONFIG_PREFIX" == "Sample" ]] &&
		! grep -qE "^\s*${CONFIG_PREFIX}__Admin__Passphrase\s*=\s*\S" <<<"$combined"; then
		die "${CONFIG_PREFIX}__Admin__Passphrase is not set in /etc/nativeweb/secrets.env.
       Outside Development the admin at /admin is reached only through the passphrase
       form at /signin, and a Production binary exits 78 at startup without one."
	fi

	# The path half is what makes this pass only when the app can do the thing.
	# Unset, the framework falls back to a directory under the process's home,
	# and the unit has no home: User=nativeweb is --system --no-create-home, so
	# the path resolves under WorkingDirectory=/opt/nativeweb/<colour>, which
	# ProtectSystem=strict makes read-only. The data-protection key ring then
	# cannot persist, every restart remints the antiforgery keys, and every form
	# POST answers 400 after each deploy with /readyz still green. Any value
	# outside ReadWritePaths fails the same way, by the kernel's decision rather
	# than the app's -- hence the prefix check, not just a non-empty check.
	local key_dir
	key_dir="$(grep -E "^\s*${CONFIG_PREFIX}__DataProtection__KeyDirectory\s*=" <<<"$combined" | tail -n 1 | sed 's/^[^=]*=\s*//')"
	if [[ -z "$key_dir" ]]; then
		die "${CONFIG_PREFIX}__DataProtection__KeyDirectory is empty or absent in /etc/nativeweb/.
       Set it to /var/lib/nativeweb/keys. Unset, the key ring lands under a read-only
       WorkingDirectory and cannot persist: every form POST 400s after each deploy and
       nothing reports it."
	fi
	if [[ "$key_dir" != /var/lib/nativeweb/* ]]; then
		die "${CONFIG_PREFIX}__DataProtection__KeyDirectory is $key_dir, which is outside
       /var/lib/nativeweb. ReadWritePaths in nativeweb@.service names that directory and
       ProtectSystem=strict makes the rest of the filesystem read-only, so the key ring
       could not be written there whatever the app intends."
	fi

	# Fail only when telemetry is ON with nowhere to send it. The gate used to
	# assert the opposite -- that unset means on -- which was true of the app that
	# left. NativeWebOptions defaults Telemetry:Enabled to false, so the old gate
	# enforced a state the binder cannot produce and demanded busywork for it.
	if grep -qiE "^\s*${CONFIG_PREFIX}__Telemetry__Enabled\s*=\s*true\s*$" <<<"$combined" &&
		! grep -qE '^\s*OTEL_EXPORTER_OTLP_ENDPOINT\s*=\s*\S' <<<"$combined"; then
		die "${CONFIG_PREFIX}__Telemetry__Enabled is true and OTEL_EXPORTER_OTLP_ENDPOINT is not set.
       This host would stand up an OTLP exporter and retry forever against a collector that
       does not exist. Set the endpoint, or leave telemetry off."
	fi
}

# --- Which colour is live ----------------------------------------------------
live_colour() {
	local port
	# The reverse_proxy DIRECTIVE, not the first port-shaped string in the file.
	# upstream.caddy.example documents both colours in its comment header, and
	# provision.sh installs that file verbatim, so the old `grep -oE '127...'`
	# read line 12's comment instead of the directive. It happened to agree on a
	# fresh host and would have disagreed the moment the live colour was green:
	# deploy.sh would have called green idle and deployed over the release that
	# was serving.
	port="$(grep -E '^\s*reverse_proxy\s' "$UPSTREAM_FILE" 2>/dev/null |
		grep -oE '127\.0\.0\.1:[0-9]+' | head -1 | cut -d: -f2 || true)"
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

# --- One deploy at a time ----------------------------------------------------
#
# Two concurrent runs both read the same live colour, both stage into the same
# idle one, both restart it and both rewrite the upstream file -- and the log
# that rollback.sh trusts ends up in an order that matches neither. db/migrate.sh
# takes an advisory lock, but that serialises the SQL only; it does nothing about
# two releases being unpacked into one directory. Non-blocking on purpose: a
# second deploy should say so and stop, not queue behind the first and then run
# against a host that has changed underneath it.
exec 9>/var/lib/nativeweb/deploy.lock
flock -n 9 || die "another deploy is already running on this host (/var/lib/nativeweb/deploy.lock).
       Wait for it to finish; do not force it. Two deploys at once stage into the same colour."

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

# The configuration prefix is a property of the BINARY -- it is the string
# compiled in at the Program.cs call to NativeWebOptions.FromConfiguration -- so
# the preflight cannot ask "will this binary find its configuration" before the
# artifact has been read. Hence the preflight moved down here from above the
# lock: nothing between the two mutates the host, so the reorder costs nothing.
CONFIG_PREFIX="$(sed -n 's/^config_prefix=//p' "$STAGE/RELEASE")"
[[ -n "$CONFIG_PREFIX" ]] || die "artifact RELEASE file has no config_prefix.
       It predates the 2026-08-31 stamp; rebuild it with scripts/deploy/build.sh.
       Guessing the prefix would mean preflighting keys this binary may never read."

if [[ -z "$MIGRATIONS_DIR_SET" ]]; then
	MIGRATIONS_DIR="$(sed -n 's/^migrations_dir=//p' "$STAGE/RELEASE")"
	[[ -n "$MIGRATIONS_DIR" ]] || die "artifact RELEASE file has no migrations_dir.
       It predates the 2026-08-31 stamp; rebuild it with scripts/deploy/build.sh, or pass
       MIGRATIONS_DIR explicitly. The old default was this repository's own sample schema,
       which a product would have applied to its production database and reported success."
fi

# Not overridable and not required. Empty means the artifact carries no docs,
# which build.sh writes when the source tree had none -- there is nothing for an
# operator to decide here, so unlike MIGRATIONS_DIR there is no environment
# escape hatch. An artifact predating the stamp reads empty too, and behaves as
# an artifact without docs: /docs keeps serving whatever is already there.
DOCS_DIR="$(sed -n 's/^docs_dir=//p' "$STAGE/RELEASE")"

if [[ -n "$DOCS_DIR" && ! -d "$STAGE/$DOCS_DIR" ]]; then
	die "artifact RELEASE stamps docs_dir=$DOCS_DIR but the artifact has no such directory.
       The build wrote the stamp and then failed to copy the site, or the artifact was
       repacked. Serving /docs from a symlink into nothing is a 404 for the whole site."
fi

preflight_config

# --- Idempotence (R8) --------------------------------------------------------
if [[ "$(release_sha_of "$LIVE")" == "$NEW_SHA" ]]; then
	echo "deploy.sh: $NEW_SHA is already live on $LIVE. Nothing to do."
	exit 0
fi

echo "deploy.sh: live=$LIVE  idle=$IDLE  deploying $NEW_SHA to $IDLE (port $IDLE_PORT)"

# --- Stage -------------------------------------------------------------------
RELEASE_DIR="$RELEASES_DIR/$NEW_SHA"

# Release directories are keyed by SHA, colours are not -- so redeploying an
# older SHA that is still LIVE on the other colour used to `rm -rf` the tree that
# colour is serving from, taking its WorkingDirectory and its rollback target
# with it. The idempotence check above only compares the live colour's SHA
# against the artifact, which is a different question.
if [[ "$(readlink -f "/opt/nativeweb/$LIVE" 2>/dev/null || true)" == "$(readlink -f "$RELEASE_DIR" 2>/dev/null || true)" ]] &&
	[[ -d "$RELEASE_DIR" ]]; then
	die "$NEW_SHA is the release $LIVE is serving from right now.
       Staging it into $IDLE would delete and recreate the directory the live process is
       running out of. If you meant to roll back to it, use rollback.sh; if you meant to
       redeploy it, promote something else first."
fi

rm -rf "$RELEASE_DIR"
mkdir -p "$RELEASE_DIR"
cp -a "$STAGE/." "$RELEASE_DIR/"
chown -R root:nativeweb "$RELEASE_DIR"
# Absolute, not subtractive. `cp -a "$STAGE/."` copies the mode of the SOURCE
# directory onto $RELEASE_DIR, and $STAGE is `mktemp -d`, which is 0700 by
# definition -- so the release directory arrives with no group bits at all. The
# old `chmod -R g-w,o-rwx` only ever removed bits, so it could not put back what
# mktemp never granted, and the service account could not chdir into its own
# release: "Failed at step CHDIR ... Permission denied", status=200/CHDIR. The
# subdirectories were fine throughout, because those modes came from the tarball,
# which is why the first real deploy on 2026-09-01 failed on the top level alone.
#
# X, capitalised: the execute bit follows for directories and for files that
# already have one, so this neither strips the launcher nor makes data executable.
chmod -R u=rwX,g=rX,o= "$RELEASE_DIR"
# The launcher's fixed name, set by build.sh, started by nativeweb@.service. It
# is checked here rather than left to ConditionPathExists on the host: a unit
# whose condition is false is inactive and SILENT, so an artifact with no
# launcher would look like a deploy that worked and a colour that never came up.
[[ -f "$RELEASE_DIR/nativeweb" ]] ||
	die "the staged release has no 'nativeweb' executable. Artifacts built before
       2026-08-31 name it after the project (nativeweb.web); rebuild with
       scripts/deploy/build.sh."
# The docs are the one part of a release a stranger is meant to read, and the
# process that serves them is Caddy, which runs as its own account and is not in
# the nativeweb group. `o=` above therefore makes /docs a 403 rather than a 404 --
# a failure that looks like a permissions bug in the web server. Widened here,
# for this subtree only: the release root stays o=, so nothing else in the
# directory becomes world-readable on the way.
#
# The traversal bit on $RELEASE_DIR itself has to come with it, or the widest
# permissions in the world on docs/ are unreachable through a 0750 parent.
if [[ -n "$DOCS_DIR" ]]; then
	chmod o=x "$RELEASE_DIR"
	chmod -R a=rX,u=rwX "$RELEASE_DIR/$DOCS_DIR"
fi

chmod 0755 "$RELEASE_DIR/nativeweb"
# db/migrate.sh is executed as ./db/migrate.sh below. It is mode 100644 in the git
# index, and `cp -a` preserves that, so on any build host that reports real file
# modes -- which is every Linux one, including the disaster-recovery clone -- the
# release could be staged and then never promoted. It only looked fine because the
# development checkout is on drvfs, where everything reads 0777.
#
# if/fi rather than `[[ ... ]] && chmod`: under `set -e` a false test as the whole
# command is a failing command, and the deploy would abort here on the very case
# the test exists to tolerate.
if [[ -f "$RELEASE_DIR/db/migrate.sh" ]]; then
	chmod 0755 "$RELEASE_DIR/db/migrate.sh"
fi

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
# db/migrate.sh defaults to the DEVELOPMENT database and role -- nativeweb_dev
# and nativeweb_app, over TCP, needing a password. provision.sh creates neither:
# the host has the role `nativeweb` and the database `nativeweb`. Invoked bare,
# as it was, the migration step could only ever fail on a provisioned host -- or,
# worse, migrate a stray nativeweb_dev if one existed. Every value it reads is
# named here rather than inherited -- MIGRATIONS_DIR included, since 2026-08-31.
# Left out of this list it defaults inside the runner, and the runner's default is
# the scaffolding demo's schema: a product deploy would create the demo articles
# table in production, skip its own migrations, and still report success. The path
# is release-relative because the runner is invoked from $RELEASE_DIR.
#
# Over the unix socket, so nothing needs a password: provision.sh maps the root
# account to both roles for this database in pg_ident, and root is already the
# account running this script.
migrate_env=(
	PGHOST=/var/run/postgresql
	PGPORT=5432
	PGDATABASE=nativeweb
	APP_ROLE=nativeweb
	SUPERUSER_ROLE=postgres
	MIGRATIONS_DIR="$RELEASE_DIR/$MIGRATIONS_DIR"
)

# The same connection the migration runner uses, so the two cannot disagree about
# which database was counted. No `|| echo 0`: a probe that fails must not read as
# "no migrations", because that is what rollback.sh's refusal is keyed on -- a
# release that DID migrate would be recorded as one that did not, and rolled back
# onto a schema the old binary has never seen. R11.
count_migrations() {
	PGHOST=/var/run/postgresql PGPORT=5432 \
		psql -U nativeweb -d nativeweb -tAc 'select count(*) from schema_migrations'
}

# Absent on a database that has never been migrated, which is a legitimate state
# and the only one allowed to answer zero.
if ! MIGRATIONS_BEFORE="$(count_migrations 2>/dev/null)"; then
	MIGRATIONS_BEFORE=0
	FIRST_MIGRATION_RUN=1
else
	FIRST_MIGRATION_RUN=0
fi

echo "deploy.sh: applying migrations"
if ! (cd "$RELEASE_DIR" && env "${migrate_env[@]}" ./db/migrate.sh); then
	die "migrations failed. $LIVE is still serving and nothing was promoted.
       The database may be partially migrated -- db/migrate.sh applies each file with its
       schema_migrations row in one transaction, so a failure leaves the failed file
       unapplied and unrecorded, but files before it in the run are committed."
fi

if ! MIGRATIONS_AFTER="$(count_migrations)"; then
	die "migrations reported success but schema_migrations could not be counted afterwards.
       Refusing to record whether this release changed the schema: rollback.sh reads that field
       as its only gate, and a wrong 'no' there lets a rollback point the old binary at a new
       schema. $LIVE is still serving and Caddy was never touched."
fi

SCHEMA_CHANGED=0
[[ "$MIGRATIONS_AFTER" != "$MIGRATIONS_BEFORE" ]] && SCHEMA_CHANGED=1
# A first migration run has no "before" to compare against, and every rollback
# target predates the schema by definition.
[[ "$FIRST_MIGRATION_RUN" -eq 1 ]] && SCHEMA_CHANGED=1

# --- Start the idle colour and prove it is ready (R10) -----------------------
echo "deploy.sh: starting nativeweb@$IDLE"
# Unguarded, a non-zero restart (masked unit, missing colour env file, missing
# binary) exits the script through `set -e` right here -- skipping the journal
# dump, the stop, and the "$LIVE is still serving" message below, and leaving the
# operator a bare exit code for the one failure that most needs explaining.
if ! systemctl restart "nativeweb@$IDLE"; then
	journalctl -u "nativeweb@$IDLE" --no-pager -n 40 || true
	systemctl stop "nativeweb@$IDLE" || true
	die "nativeweb@$IDLE would not start at all -- see the journal above.
       $LIVE is still serving $(release_sha_of "$LIVE") and Caddy was never touched."
fi

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

# The docs move with the release, so /docs describes what is serving. Through
# the colour symlink rather than at $RELEASE_DIR directly, so rollback.sh can
# repoint it with the same one line and neither script has to know a release SHA.
#
# Only when this artifact carries docs. An artifact built without them leaves the
# link alone rather than breaking it: a product that renders its docs on some
# releases and not others should keep serving the last set it had, and a dangling
# symlink is a 404 for the whole site.
if [[ -n "$DOCS_DIR" ]]; then
	ln -sfn "/opt/nativeweb/$IDLE/$DOCS_DIR" /opt/nativeweb/docs
fi

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
