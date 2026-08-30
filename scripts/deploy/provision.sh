#!/usr/bin/env bash
#
# Bare Debian install -> host that can serve NativeWeb. Spec 011 T8
# (R1, R2, R5, R6, R7).
#
#   sudo scripts/deploy/provision.sh
#   sudo scripts/deploy/provision.sh --edge 10.0.0.1
#
# --edge <addr> provisions a host that sits BEHIND a TLS-terminating proxy at
# <addr> instead of holding :443 itself -- an Incus guest on a fleet host, where
# one host-level Caddy fronts every site. Decision 006.
#
# Idempotent by construction: every step either creates something that does not
# exist or asserts something that does. Running it twice changes nothing the
# second time, which is what makes it safe to re-run after editing a unit file.
#
# It deliberately does NOT write secrets. It creates /etc/nativeweb/secrets.env
# with the right ownership and mode and leaves it empty; filling it in is an
# operator step the runbook walks through, because a script that generates
# production keys is a script that has seen them.
#
# What it does not do: deploy. A provisioned host has no release on it. Run
# build.sh then deploy.sh next.

set -euo pipefail

SERVICE_USER=nativeweb
SERVICE_GROUP=nativeweb
APP_ROOT=/opt/nativeweb
DATA_ROOT=/var/lib/nativeweb
CONFIG_ROOT=/etc/nativeweb
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
JOURNAL_MAX_USE="${JOURNAL_MAX_USE:-2G}"
JOURNAL_MAX_AGE="${JOURNAL_MAX_AGE:-30day}"

die() {
	echo "provision.sh: $*" >&2
	exit 1
}
step() { echo "provision.sh: $*"; }

# --edge <addr>: this host sits behind a TLS-terminating proxy at <addr> rather
# than holding :443 itself. Decision 006. It changes three things and nothing
# else -- which Caddyfile is installed, the trusted-proxy address Caddy is given,
# and the forwarded-headers settings seeded into app.env -- so the two modes share
# every other step, including the whole blue/green release layout.
EDGE_PROXY=""
while [[ $# -gt 0 ]]; do
	case "$1" in
	--edge)
		# Same `shift 2` trap deploy.sh's need_value documents: with one argument
		# left, shift 2 returns 1 and `set -e` exits with no message at all.
		[[ $# -ge 2 && -n "$2" ]] || die "--edge needs the edge proxy's address, e.g. --edge 10.0.0.1"
		EDGE_PROXY="$2"
		shift 2
		;;
	-h | --help)
		sed -n '2,23p' "${BASH_SOURCE[0]}"
		exit 0
		;;
	*) die "unknown argument '$1'" ;;
	esac
done

[[ "$(id -u)" -eq 0 ]] || die "must run as root"
command -v apt-get >/dev/null || die "this script targets Debian/Ubuntu (apt-get not found)"

# --- Packages ----------------------------------------------------------------
step "installing packages"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq

# curl/gnupg first: the Caddy repository key needs them.
apt-get install -y -qq \
	ca-certificates curl gnupg debian-keyring debian-archive-keyring apt-transport-https \
	postgresql postgresql-client \
	nftables \
	unattended-upgrades apt-listchanges \
	age zstd jq

# Caddy is not in Debian stable at a version worth running, so it comes from its
# own repository. Guarded so a re-run does not re-add the source.
if [[ ! -f /etc/apt/sources.list.d/caddy-stable.list ]]; then
	step "adding the Caddy repository"
	curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' |
		gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
	curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' \
		>/etc/apt/sources.list.d/caddy-stable.list
	apt-get update -qq
fi
apt-get install -y -qq caddy

# awscli talks to any S3-compatible endpoint via --endpoint-url; the backup
# scripts never name a provider.
command -v aws >/dev/null || apt-get install -y -qq awscli

# --- Service account (R2) ----------------------------------------------------
if ! id -u "$SERVICE_USER" >/dev/null 2>&1; then
	step "creating the $SERVICE_USER system account"
	# --system: no password ageing, no home in /home, uid below 1000.
	# nologin: this account exists to own a process and a directory. Nobody
	# should ever be able to `su - nativeweb` and look around with its access to
	# the database and the data-protection keys.
	adduser --system --group --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"
fi
# Asserted every run, not just at creation: a shell handed back by a later
# `usermod` is exactly the change nobody would announce.
current_shell="$(getent passwd "$SERVICE_USER" | cut -d: -f7)"
[[ "$current_shell" == /usr/sbin/nologin || "$current_shell" == /bin/false ]] ||
	die "$SERVICE_USER has an interactive shell ($current_shell). R2 requires it not to."

# --- Layout ------------------------------------------------------------------
step "laying out $APP_ROOT, $DATA_ROOT and $CONFIG_ROOT"
install -d -o root -g root -m 0755 "$APP_ROOT" "$APP_ROOT/releases" "$APP_ROOT/scripts"
# The service reads its release tree and writes nowhere in it.
install -d -o root -g "$SERVICE_GROUP" -m 0750 "$CONFIG_ROOT"
# The one writable path (ReadWritePaths in the unit). Data-protection keys live
# here so they survive a blue/green swap -- under the release directory they
# would move out from under every issued antiforgery token on every deploy.
install -d -o "$SERVICE_USER" -g "$SERVICE_GROUP" -m 0750 "$DATA_ROOT" "$DATA_ROOT/keys"
# Owned by the Caddy user, not root: the global `log { output file }` block in our
# Caddyfile is opened by the server process, which the Debian package runs as
# `caddy`. root:root 0755 here means Caddy cannot create access.log and refuses to
# start -- and `install -d` re-applies owner and mode to a directory that already
# exists, so this line could also break one the package had got right.
CADDY_LOG_OWNER=root
id -u caddy >/dev/null 2>&1 && CADDY_LOG_OWNER=caddy
install -d -o "$CADDY_LOG_OWNER" -g "$CADDY_LOG_OWNER" -m 0755 /var/log/caddy

# Operator scripts live on the host so the timer units and a human in an
# emergency can reach them without a checkout.
install -o root -g root -m 0755 \
	"$REPO_ROOT/scripts/deploy/backup.sh" \
	"$REPO_ROOT/scripts/deploy/restore.sh" \
	"$REPO_ROOT/scripts/deploy/rollback.sh" \
	"$APP_ROOT/scripts/"

# --- Configuration files -----------------------------------------------------
# Created empty-but-correct if absent, never overwritten. A provision re-run
# must not be able to blank a working deployment's settings.
seed_config() {
	local path="$1" mode="$2" owner="$3" body="$4"
	if [[ ! -f "$path" ]]; then
		step "creating $path"
		printf '%s\n' "$body" >"$path"
	fi
	chown "$owner" "$path"
	chmod "$mode" "$path"
}

# The forwarded-headers stanza differs by mode, and the two settings in it have to
# agree with each other: ForwardLimit is how many X-Forwarded-For entries are
# popped, and a pop only counts while the address it lands on is in KnownProxies.
# Seeded together here so a host cannot end up with one and not the other.
if [[ -n "$EDGE_PROXY" ]]; then
	FORWARDED_STANZA="# REQUIRED. Unset means every client in the world shares one rate-limit
# partition and nothing says so. deploy.sh refuses to promote without it.
#
# Two hops, because --edge was used: the edge proxy at $EDGE_PROXY terminates TLS
# and the Caddy on this host owns the blue/green swap. Both must be trusted, and
# ForwardLimit must match the chain length -- at the default of 1 the app unwinds
# only the local hop and every request appears to come from $EDGE_PROXY.
NativeWeb__ForwardedHeaders__KnownProxies=127.0.0.1,::1,$EDGE_PROXY
NativeWeb__ForwardedHeaders__ForwardLimit=2"
else
	FORWARDED_STANZA='# REQUIRED. Unset means every client in the world shares one rate-limit
# partition and nothing says so. deploy.sh refuses to promote without it.
NativeWeb__ForwardedHeaders__KnownProxies=127.0.0.1,::1'
fi

seed_config "$CONFIG_ROOT/app.env" 0644 "root:$SERVICE_GROUP" \
	"$(printf '%s\n\n%s\n\n%s\n' \
		'# Non-secret application settings. Read by nativeweb@.service.
ASPNETCORE_ENVIRONMENT=Production' \
		"$FORWARDED_STANZA" \
		'# REQUIRED. This deployment public origin, emitted by robots.txt and sitemap.xml.
NativeWeb__CanonicalBaseUrl=

# Must persist across releases. Do not point this inside a release directory.
NativeWeb__DataProtection__KeyDirectory=/var/lib/nativeweb/keys

# Telemetry is opt-OUT. Either point this at a collector or set Enabled=false;
# deploy.sh refuses to promote when neither is done.
# OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:4317
NativeWeb__Telemetry__Enabled=false

# The defaults are localhost and break every credential on a real hostname.
NativeWeb__WebAuthn__ServerDomain=
NativeWeb__WebAuthn__Origins=

# The default is https://localhost/device and is wrong for any real deployment.
NativeWeb__DeviceGrant__VerificationUri=')"

# 0640 root:nativeweb. The service reads it; no other account can. Asserted on
# every run, because this is the file whose mode silently drifting matters most.
seed_config "$CONFIG_ROOT/secrets.env" 0640 "root:$SERVICE_GROUP" \
	'# Secrets. NOT in the repository, NOT world-readable, NOT set with Environment=
# (which systemctl show would expose to any local user).
#
# Fill these in before the first deploy. apps/web refuses to start outside
# Development without the SMTP host, and degrades SILENTLY without the three keys.
ConnectionStrings__NativeWeb=
NativeWeb__ContactKey=
NativeWeb__StoreEncryptionKey=
NativeWeb__PairwiseSalt=
NativeWeb__Smtp__Host=
NativeWeb__Smtp__Username=
NativeWeb__Smtp__Secret=
NativeWeb__Smtp__FromAddress='

seed_config "$CONFIG_ROOT/blue.env" 0644 "root:$SERVICE_GROUP" \
	'ASPNETCORE_URLS=http://127.0.0.1:5011'
seed_config "$CONFIG_ROOT/green.env" 0644 "root:$SERVICE_GROUP" \
	'ASPNETCORE_URLS=http://127.0.0.1:5012'

seed_config "$CONFIG_ROOT/backup.env" 0640 "root:root" \
	'# Read by backup.sh and restore.sh.
BACKUP_S3_BUCKET=
BACKUP_S3_ENDPOINT=
BACKUP_AGE_RECIPIENT=
BACKUP_AGE_IDENTITY=/etc/nativeweb/backup-age.key
BACKUP_RETENTION_DAYS=30
# Unset means a backup that stops running is never noticed. See R15.
BACKUP_HEARTBEAT_URL=
AWS_ACCESS_KEY_ID=
AWS_SECRET_ACCESS_KEY=

# The unit runs as root, and there is no PostgreSQL role called root. Over the
# socket as the app role, through the pg_ident map above -- no password anywhere.
PGDATABASE=nativeweb
PGHOST=/var/run/postgresql
PGUSER=nativeweb'

# The mode assertion R18 asks for, run every time rather than only at creation.
actual_mode="$(stat -c '%a %U:%G' "$CONFIG_ROOT/secrets.env")"
[[ "$actual_mode" == "640 root:$SERVICE_GROUP" ]] ||
	die "$CONFIG_ROOT/secrets.env is '$actual_mode', expected '640 root:$SERVICE_GROUP'"

# --- PostgreSQL (R5) ---------------------------------------------------------
step "confining PostgreSQL to localhost"
PG_CONF="$(sudo -u postgres psql -tAc 'SHOW config_file')"
# Ask PostgreSQL what it is bound to rather than pattern-matching the file. The
# grep-then-restart form matched only one spelling, so `listen_addresses =
# 'localhost' # set by us` or `"localhost"` or an equivalent `'127.0.0.1'` all
# looked wrong on every re-run -- and this script restarts PostgreSQL, which on
# a live host drops every connection both colours are holding, mid-request.
CURRENT_LISTEN="$(sudo -u postgres psql -tAc 'SHOW listen_addresses')"
case "$CURRENT_LISTEN" in
localhost | 127.0.0.1 | '127.0.0.1,::1' | '::1,127.0.0.1' | 'localhost,::1')
	step "PostgreSQL already listens on '$CURRENT_LISTEN' only -- not restarting it"
	;;
*)
	sed -i "s/^#\?listen_addresses.*/listen_addresses = 'localhost'/" "$PG_CONF"
	step "PostgreSQL was listening on '$CURRENT_LISTEN'; restarting it bound to localhost"
	systemctl restart postgresql
	;;
esac
# Belt and braces with the firewall: a bind address and a packet filter fail in
# different ways, and R5 is about the database being unreachable rather than
# about either mechanism working.

if ! sudo -u postgres psql -tAc "select 1 from pg_roles where rolname='$SERVICE_USER'" | grep -q 1; then
	step "creating the $SERVICE_USER database role"
	sudo -u postgres createuser "$SERVICE_USER"
fi
if ! sudo -u postgres psql -tAc "select 1 from pg_database where datname='nativeweb'" | grep -q 1; then
	step "creating the nativeweb database"
	sudo -u postgres createdb -O "$SERVICE_USER" nativeweb
fi
# 0002 creates the citext extension and the app role cannot; db/migrate.sh
# expects a superuser role for that one file only.

# --- How the deploy authenticates to PostgreSQL ------------------------------
#
# deploy.sh, backup.sh and restore.sh all run as root and all need to be BOTH
# the app role (migrations create objects, and whoever creates them owns them --
# nothing in db/migrations issues a GRANT) and the superuser (migration 0002
# creates the citext extension). Over TCP that would mean a password in a file
# for each role. Over the socket with an identity map it means neither: root
# already owns this host, so mapping it to those two roles for this one database
# grants it nothing it could not take anyway.
#
# The service account itself is mapped only to its own role, and nothing maps
# any other account to postgres.
PG_IDENT="$(dirname "$PG_CONF")/pg_ident.conf"
PG_HBA="$(dirname "$PG_CONF")/pg_hba.conf"
MARKER='# nativeweb (spec 011)'

if ! grep -qF "$MARKER" "$PG_IDENT"; then
	step "mapping root and $SERVICE_USER for local migration and backup access"
	cat >>"$PG_IDENT" <<EOF

$MARKER -- see scripts/deploy/provision.sh
nativeweb       root                    $SERVICE_USER
nativeweb       root                    postgres
nativeweb       $SERVICE_USER           $SERVICE_USER
nativeweb       postgres                postgres
EOF
fi

if ! grep -qF "$MARKER" "$PG_HBA"; then
	# Prepended, because pg_hba is first-match-wins and Debian's default
	# `local all all peer` would otherwise answer first and refuse root.
	#
	# Database field is `all`, not `nativeweb`. Scoping it to the one database read
	# as though it were the tighter choice, and was not: the ident map already
	# restricts this line to two roles, while the narrow database field broke both
	# halves of the restore drill. `createdb` connects to the `postgres` maintenance
	# database, and the drill target is `nativeweb_drill` by the runbook's own
	# instruction -- neither is `nativeweb`, so both fell through to
	# `local all all peer`, where OS root matches no role of that name. The drill
	# failed and the production overwrite worked, which is the wrong way round.
	step "allowing local mapped access to the nativeweb database"
	tmp_hba="$(mktemp)"
	cat >"$tmp_hba" <<EOF
$MARKER -- see scripts/deploy/provision.sh
local   all             $SERVICE_USER,postgres          peer map=nativeweb

EOF
	cat "$PG_HBA" >>"$tmp_hba"
	install -o postgres -g postgres -m 0640 "$tmp_hba" "$PG_HBA"
	rm -f "$tmp_hba"
	systemctl reload postgresql
fi

# --- systemd units -----------------------------------------------------------
step "installing systemd units"
install -o root -g root -m 0644 \
	"$REPO_ROOT/scripts/deploy/systemd/nativeweb@.service" \
	"$REPO_ROOT/scripts/deploy/systemd/nativeweb-backup.service" \
	"$REPO_ROOT/scripts/deploy/systemd/nativeweb-backup.timer" \
	"$REPO_ROOT/scripts/deploy/systemd/nativeweb-backup-failed.service" \
	/etc/systemd/system/
systemctl daemon-reload
systemctl enable --now nativeweb-backup.timer

# The colour units are enabled but NOT started: there is no release on disk yet.
systemctl enable nativeweb@blue nativeweb@green >/dev/null

# --- Caddy (R4, R6) ----------------------------------------------------------
step "installing the Caddy configuration"
# One or the other, never both: the edge variant turns auto_https off and listens
# on :80, the standalone one holds :443 and gets its own certificates. Installing
# the wrong one on a fleet guest means Caddy retrying an ACME order that can never
# complete, because the HTTP-01 challenge cannot reach a private bridge address.
if [[ -n "$EDGE_PROXY" ]]; then
	step "  edge mode: behind the proxy at $EDGE_PROXY, no ACME, listening on :80"
	install -o root -g root -m 0644 "$REPO_ROOT/scripts/deploy/caddy/Caddyfile.edge" /etc/caddy/Caddyfile
else
	install -o root -g root -m 0644 "$REPO_ROOT/scripts/deploy/caddy/Caddyfile" /etc/caddy/Caddyfile
fi
if [[ ! -f /etc/caddy/upstream.caddy ]]; then
	install -o root -g root -m 0644 \
		"$REPO_ROOT/scripts/deploy/caddy/upstream.caddy.example" /etc/caddy/upstream.caddy
fi
if [[ -n "$EDGE_PROXY" ]]; then
	# Edge mode needs neither the hostname nor the ACME address -- Caddy here asks
	# for no certificate and matches no hostname -- but it does need the one
	# address it will trust X-Forwarded-* from. Written rather than left blank,
	# because unlike the other two it is known at provision time: it is the
	# argument that put this host in edge mode.
	if ! grep -q NATIVEWEB_EDGE_PROXY /etc/default/caddy 2>/dev/null; then
		cat >>/etc/default/caddy <<EOF
# The TLS-terminating proxy in front of this host. Caddy preserves the
# X-Forwarded-* headers set by this address and replaces any others, so a wrong
# value here means the app sees scheme http and the bridge gateway as the client.
NATIVEWEB_EDGE_PROXY=$EDGE_PROXY
EOF
	fi
elif [[ ! -f /etc/default/caddy ]] || ! grep -q NATIVEWEB_HOST /etc/default/caddy; then
	cat >>/etc/default/caddy <<'EOF'
# Set both before starting Caddy. NATIVEWEB_HOST is the public hostname and is
# what Caddy requests a certificate for; NATIVEWEB_ACME_EMAIL is where Let's
# Encrypt sends expiry warnings if renewal ever stops working.
NATIVEWEB_HOST=
NATIVEWEB_ACME_EMAIL=
EOF
	step "NOTE: set NATIVEWEB_HOST and NATIVEWEB_ACME_EMAIL in /etc/default/caddy before starting Caddy"
fi

# Nothing else makes Caddy read that file. The Debian package's own caddy.service
# has no EnvironmentFile line, so without this drop-in `{$NATIVEWEB_HOST}` in the
# Caddyfile expands to nothing, the site block has an empty address, and
# `systemctl restart caddy` fails to load the config -- with the file we just
# wrote sitting there looking like it was read.
#
# No leading `-` on the path on purpose: the file is written immediately above, so
# its absence is a bug worth hearing about rather than an empty hostname to debug.
step "pointing caddy.service at /etc/default/caddy"
install -d -o root -g root -m 0755 /etc/systemd/system/caddy.service.d
cat >/etc/systemd/system/caddy.service.d/nativeweb-environment.conf <<'EOF'
# Installed by scripts/deploy/provision.sh (spec 011).
[Service]
EnvironmentFile=/etc/default/caddy
EOF
systemctl daemon-reload

# --- Firewall (R5) -----------------------------------------------------------
step "installing the nftables ruleset"
# NOT unconditional. nativeweb.nft opens with `flush ruleset`, which drops every
# table in every family -- so overwriting /etc/nftables.conf on a re-run silently
# destroys anything added since provisioning: another table, a fail2ban chain, a
# hand-added allow. The header claims running this twice changes nothing, and
# this was the line that made that false.
#
# The first version of this guard was absent-or-ours, which never fires on a real
# host: line 49 above installs Debian's `nftables` package, and that package ships
# /etc/nftables.conf as a conffile. So every FIRST provision took the "not ours"
# branch and left R5 unmet, on a fresh host, silently -- the opposite of what the
# guard was for. A conffile the package installed and nobody has edited is ours to
# replace; `dpkg --verify` is what distinguishes that from an operator's ruleset.
NFT_MARKER='# NativeWeb spec 011 ruleset'

# The ruleset ships `define ssh_ports = { 22 }`. Installing that verbatim on a
# host whose sshd moved is an immediate lockout: the running session's port is
# dropped, 22 is closed upstream and unbound locally, and there is no console
# failover. So the define is rewritten from what sshd reports it is bound to --
# the one source that cannot disagree with reality.
#
# `sshd -T` prints one `port N` line per listening port, already resolved from
# every drop-in under sshd_config.d, in the order sshd applies them.
ssh_ports_from_sshd() {
	# `|| true` so an absent or erroring sshd yields the empty string rather than
	# killing the script. Without it, `set -euo pipefail` turns the failed pipeline
	# into a silent exit at the SSH_PORTS assignment below -- before the die that
	# explains the problem, so the operator sees a bare nonzero exit. A guest with
	# no sshd at all is not exotic: it is decision 006's stated target, and the one
	# real --edge run never reached this branch because the flush guard skipped it.
	sshd -T 2>/dev/null | awk '$1 == "port" { printf "%s%s", sep, $2; sep = ", " }' || true
}

nft_with_ssh_ports() {
	local ports="$1"
	# Anchored to the whole define line so a stray `ssh_ports` in a comment is
	# not rewritten, and so a ruleset that has lost the line fails the check
	# below rather than being installed unchanged.
	sed -E "s/^define ssh_ports = \{[^}]*\}$/define ssh_ports = { ${ports} }/" \
		"$REPO_ROOT/scripts/deploy/nftables/nativeweb.nft"
}

# In edge mode the public surface is not public. The stock ruleset accepts
# { 80, 443 } from any source, which on a fleet guest means every neighbour on
# the bridge can reach a listener whose X-Forwarded-* headers Caddy trusts --
# and 443 is not even bound, because Caddyfile.edge runs with auto_https off.
# Same anchored-substitution discipline as ssh_ports, checked by the caller.
nft_restricted_to_edge() {
	local edge="$1"
	sed -E "s|^(\t\t)tcp dport \{ 80, 443 \} accept$|\1ip saddr ${edge} tcp dport 80 accept|"
}
nft_conf_is_untouched_package_default() {
	dpkg --verify nftables 2>/dev/null | grep -qE '[[:space:]]/etc/nftables\.conf$' && return 1
	dpkg -S /etc/nftables.conf >/dev/null 2>&1
}
if [[ ! -f /etc/nftables.conf ]] || grep -qF "$NFT_MARKER" /etc/nftables.conf \
	|| nft_conf_is_untouched_package_default; then
	SSH_PORTS="$(ssh_ports_from_sshd)"
	if [[ -z "$SSH_PORTS" ]]; then
		die "sshd -T reported no listening port, so the firewall's SSH allowance cannot be
       derived. Installing the ruleset with its default of 22 would lock this host out if
       sshd is anywhere else. Fix sshd -T, or install /etc/nftables.conf by hand."
	fi
	NFT_RENDERED="$(nft_with_ssh_ports "$SSH_PORTS")"
	# The substitution failing silently is the failure this whole block exists to
	# prevent, so it is checked rather than assumed.
	if [[ "$NFT_RENDERED" != *"define ssh_ports = { $SSH_PORTS }"* ]]; then
		die "could not rewrite 'define ssh_ports' in nativeweb.nft. Refusing to install a
       ruleset whose SSH allowance was not confirmed."
	fi

	if [[ -n "$EDGE_PROXY" ]]; then
		NFT_RENDERED="$(printf '%s\n' "$NFT_RENDERED" | nft_restricted_to_edge "$EDGE_PROXY")"
		# Checked the same way, and for a sharper reason: a silent no-op here does
		# not fail, it installs the world-open rule while every message this script
		# prints says the listener is reachable only from the edge.
		if [[ "$NFT_RENDERED" != *"ip saddr $EDGE_PROXY tcp dport 80 accept"* ]]; then
			die "could not restrict the public surface to $EDGE_PROXY in nativeweb.nft.
       Refusing to install a ruleset that opens 80 and 443 to every host on the network
       while --edge says only the edge can reach this guest."
		fi
		step "restricting the public surface to $EDGE_PROXY on port 80 (edge mode)"
	fi
	step "opening SSH on port(s) $SSH_PORTS, as reported by sshd -T"

	# Checked with `nft -c` BEFORE it is written, not loaded with `nft -f` after
	# the service is already enabled. The old order had the worst failure mode in
	# this file: a ruleset that does not parse was still installed and nftables
	# still enabled, so errexit ended the run with a broken /etc/nftables.conf on
	# disk -- and at the next boot nftables.service fails and the host comes up
	# with NO ruleset at all. A firewall that fails to install must not become a
	# host with no firewall.
	NFT_STAGED="$(mktemp)"
	printf '%s\n' "$NFT_RENDERED" >"$NFT_STAGED"
	if ! nft -c -f "$NFT_STAGED"; then
		rm -f "$NFT_STAGED"
		die "the rendered nftables ruleset does not parse. Refusing to install it: a broken
       /etc/nftables.conf leaves this host with no ruleset at all after a reboot."
	fi
	install -o root -g root -m 0644 "$NFT_STAGED" /etc/nftables.conf
	rm -f "$NFT_STAGED"
	systemctl enable --now nftables
	nft -f /etc/nftables.conf
	NFT_INSTALLED=yes
else
	NFT_INSTALLED=no
	step "SKIPPED: /etc/nftables.conf exists and is not ours -- not overwriting it"
	echo "       This ruleset starts with 'flush ruleset', so installing it would drop every"
	echo "       table on this host. Merge scripts/deploy/nftables/nativeweb.nft by hand, or"
	echo "       move the existing file aside and re-run. R5 is NOT met until you do."
	if [[ -n "$EDGE_PROXY" ]]; then
		echo "       In edge mode that includes the one rule this guest needs, which was NOT added:"
		echo "         ip saddr $EDGE_PROXY tcp dport 80 accept"
		echo "       Until it exists, the edge cannot reach Caddy and every proxied request fails."
	fi
fi

# --- Log retention (R6) ------------------------------------------------------
step "bounding journal retention at $JOURNAL_MAX_USE / $JOURNAL_MAX_AGE"
install -d -m 0755 /etc/systemd/journald.conf.d
cat >/etc/systemd/journald.conf.d/nativeweb.conf <<EOF
# R6: neither log source may fill the disk. Caddy's access log is bounded
# separately by roll_size/roll_keep in the Caddyfile.
[Journal]
Storage=persistent
SystemMaxUse=$JOURNAL_MAX_USE
MaxRetentionSec=$JOURNAL_MAX_AGE
EOF
systemctl restart systemd-journald

# --- Unattended security updates (R7) ----------------------------------------
step "enabling unattended security upgrades"
cat >/etc/apt/apt.conf.d/51nativeweb-unattended <<'EOF'
// R7 wants a RECORDED decision, not a default.
//
// Security pocket only: this host is an identity provider and a surprise
// feature upgrade is a bigger risk than a delayed non-security fix.
//
// Automatic-Reboot is OFF, deliberately. There is one host and no failover, so
// an unattended 03:00 reboot is an unannounced outage. Kernel and libc updates
// therefore need an operator to reboot; the runbook says to check
// /var/run/reboot-required on the monthly pass.
Unattended-Upgrade::Automatic-Reboot "false";
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF
systemctl enable --now unattended-upgrades

# --- Done --------------------------------------------------------------------
if [[ -n "$EDGE_PROXY" ]]; then
	# Told the truth per branch. This used to say "add the firewall rule" in both
	# cases: a no-op instruction when provision installed the ruleset itself (it
	# now contains the rule), and the single most important step when it skipped.
	if [[ "$NFT_INSTALLED" == yes ]]; then
		EDGE_FIREWALL_NOTE="The installed ruleset already restricts :80 to $EDGE_PROXY."
	else
		EDGE_FIREWALL_NOTE="The firewall was NOT installed, so add by hand:
       ip saddr $EDGE_PROXY tcp dport 80 accept"
	fi
	CADDY_NEXT_STEP="2. Caddy needs no hostname or ACME address in edge mode; NATIVEWEB_EDGE_PROXY is
     already set to $EDGE_PROXY. $EDGE_FIREWALL_NOTE
       systemctl restart caddy"
	DEPLOY_NEXT_STEP="5. Then:                       scripts/deploy/deploy.sh --artifact dist/... --incus <instance>"
else
	CADDY_NEXT_STEP="2. Set NATIVEWEB_HOST and NATIVEWEB_ACME_EMAIL in /etc/default/caddy, then:
       systemctl restart caddy"
	DEPLOY_NEXT_STEP="5. Then:                       scripts/deploy/deploy.sh --artifact dist/... --host <this host>"
fi

cat <<EOF

provision.sh: host is provisioned. Nothing is serving yet -- there is no release on disk.

Next, in order:
  1. Fill in $CONFIG_ROOT/secrets.env and the blank values in $CONFIG_ROOT/app.env.
  $CADDY_NEXT_STEP
  3. Generate the backup key and STORE THE PRIVATE HALF OFF THIS HOST:
       age-keygen -o $CONFIG_ROOT/backup-age.key
       chmod 0600 $CONFIG_ROOT/backup-age.key
     Put its public half in BACKUP_AGE_RECIPIENT in $CONFIG_ROOT/backup.env.
     If this key is lost, every backup in the bucket is unreadable noise.
  4. From a WSL/Linux checkout:  scripts/deploy/build.sh
  $DEPLOY_NEXT_STEP

Full walkthrough: docs/02-reference/deployment.md
EOF
