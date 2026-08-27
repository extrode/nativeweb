#!/usr/bin/env bash
#
# Bare Debian install -> host that can serve NativeWeb. Spec 011 T8
# (R1, R2, R5, R6, R7).
#
#   sudo scripts/deploy/provision.sh
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
install -d -o root -g root -m 0755 /var/log/caddy

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

seed_config "$CONFIG_ROOT/app.env" 0644 "root:$SERVICE_GROUP" \
	'# Non-secret application settings. Read by nativeweb@.service.
ASPNETCORE_ENVIRONMENT=Production

# REQUIRED. Unset means every client in the world shares one rate-limit
# partition and nothing says so. deploy.sh refuses to promote without it.
NativeWeb__ForwardedHeaders__KnownProxies=127.0.0.1,::1

# REQUIRED. This deployment public origin, emitted by robots.txt and sitemap.xml.
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
NativeWeb__DeviceGrant__VerificationUri='

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
	step "allowing local mapped access to the nativeweb database"
	tmp_hba="$(mktemp)"
	cat >"$tmp_hba" <<EOF
$MARKER -- see scripts/deploy/provision.sh
local   nativeweb       $SERVICE_USER,postgres          peer map=nativeweb

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
install -o root -g root -m 0644 "$REPO_ROOT/scripts/deploy/caddy/Caddyfile" /etc/caddy/Caddyfile
if [[ ! -f /etc/caddy/upstream.caddy ]]; then
	install -o root -g root -m 0644 \
		"$REPO_ROOT/scripts/deploy/caddy/upstream.caddy.example" /etc/caddy/upstream.caddy
fi
if [[ ! -f /etc/default/caddy ]] || ! grep -q NATIVEWEB_HOST /etc/default/caddy; then
	cat >>/etc/default/caddy <<'EOF'
# Set both before starting Caddy. NATIVEWEB_HOST is the public hostname and is
# what Caddy requests a certificate for; NATIVEWEB_ACME_EMAIL is where Let's
# Encrypt sends expiry warnings if renewal ever stops working.
NATIVEWEB_HOST=
NATIVEWEB_ACME_EMAIL=
EOF
	step "NOTE: set NATIVEWEB_HOST and NATIVEWEB_ACME_EMAIL in /etc/default/caddy before starting Caddy"
fi

# --- Firewall (R5) -----------------------------------------------------------
step "installing the nftables ruleset"
# NOT unconditional. nativeweb.nft opens with `flush ruleset`, which drops every
# table in every family -- so overwriting /etc/nftables.conf on a re-run silently
# destroys anything added since provisioning: another table, a fail2ban chain, a
# hand-added allow. The header claims running this twice changes nothing, and
# this was the line that made that false.
NFT_MARKER='# NativeWeb spec 011 ruleset'
if [[ ! -f /etc/nftables.conf ]] || grep -qF "$NFT_MARKER" /etc/nftables.conf; then
	install -o root -g root -m 0644 "$REPO_ROOT/scripts/deploy/nftables/nativeweb.nft" /etc/nftables.conf
	systemctl enable --now nftables
	nft -f /etc/nftables.conf
else
	step "SKIPPED: /etc/nftables.conf exists and is not ours -- not overwriting it"
	echo "       This ruleset starts with 'flush ruleset', so installing it would drop every"
	echo "       table on this host. Merge scripts/deploy/nftables/nativeweb.nft by hand, or"
	echo "       move the existing file aside and re-run. R5 is NOT met until you do."
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
cat <<EOF

provision.sh: host is provisioned. Nothing is serving yet -- there is no release on disk.

Next, in order:
  1. Fill in $CONFIG_ROOT/secrets.env and the blank values in $CONFIG_ROOT/app.env.
  2. Set NATIVEWEB_HOST and NATIVEWEB_ACME_EMAIL in /etc/default/caddy, then:
       systemctl restart caddy
  3. Generate the backup key and STORE THE PRIVATE HALF OFF THIS HOST:
       age-keygen -o $CONFIG_ROOT/backup-age.key
       chmod 0600 $CONFIG_ROOT/backup-age.key
     Put its public half in BACKUP_AGE_RECIPIENT in $CONFIG_ROOT/backup.env.
     If this key is lost, every backup in the bucket is unreadable noise.
  4. From a WSL/Linux checkout:  scripts/deploy/build.sh
  5. Then:                       scripts/deploy/deploy.sh --artifact dist/... --host <this host>

Full walkthrough: docs/02-reference/deployment.md
EOF
