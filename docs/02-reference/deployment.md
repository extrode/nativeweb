# Deployment runbook — apps/web

Spec: `../specs/011-production-deployment/011-spec.md`
Scripts: `../../scripts/deploy/`

The one document for provisioning, releasing, rolling back, backing up and
recovering a NativeWeb production host. Everything here is a command to run or a
decision with a stated default.

> **Two things in this document have never been executed.** The provisioning
> walkthrough and the restore drill are written and untested — spec 011 T15.
> Every elapsed time below reads `NOT YET MEASURED`, and that is literal:
> nobody has provisioned a host from this document or restored a backup taken by
> `backup.sh`. Until they have, R1, R16 and R17 are unmet and this runbook is a
> plan rather than a procedure.
>
> **2026-08-30: an audit sweep read the scripts and found eight defects, four of
> which would have stopped this walkthrough dead on a clean Debian** — Caddy never
> reading the environment file it is told to fill in, a log directory the Caddy
> user cannot write, the firewall skipped on every first provision, and `sudo`
> refusing the variables `deploy.sh` passes on its command line. All eight are
> fixed and each has a test. That is a reason to have more confidence in the
> document, and none at all to think it has been run: static reading cannot find
> the ninth.

## Shape of it

One Linux host. Caddy terminates TLS on 443 and proxies to whichever release
colour is live; two systemd instances of `nativeweb@.service` run on loopback
ports 5011 (blue) and 5012 (green); PostgreSQL runs on the same box, bound to
localhost. A deploy stages into the idle colour, migrates, proves readiness, and
then reloads Caddy — so the live colour serves continuously and a failed release
never reaches the public.

| Path | What |
|---|---|
| `/opt/nativeweb/releases/<sha>/` | Unpacked releases, root-owned |
| `/opt/nativeweb/{blue,green}` | Symlinks into a release |
| `/opt/nativeweb/scripts/` | `backup.sh`, `restore.sh`, `rollback.sh` on the host |
| `/etc/nativeweb/app.env` | Non-secret settings, `0644 root:nativeweb` |
| `/etc/nativeweb/secrets.env` | **Secrets, `0640 root:nativeweb`** |
| `/etc/nativeweb/{blue,green}.env` | The colour → port mapping |
| `/etc/nativeweb/backup.env` | Backup destination and credentials, `0640 root:root` |
| `/etc/caddy/upstream.caddy` | Generated. One line. Which colour is live |
| `/var/lib/nativeweb/keys` | Data-protection keys — **must survive a release swap** |
| `/var/lib/nativeweb/deployments.log` | What was promoted, when, and whether it migrated |

## Decisions on record

Both are R7's, and both are choices rather than defaults.

- **Distribution: Debian stable, x86-64.** `provision.sh` targets apt and systemd;
  Ubuntu LTS works unchanged. Record the tested version here once T15 runs:
  `TESTED ON: NOT YET MEASURED`.
- **`unattended-upgrades` is on, security pocket only, and does NOT reboot.**
  There is one host and no failover, so an unattended 03:00 reboot is an
  unannounced outage. Kernel and libc updates therefore wait for an operator:
  check `/var/run/reboot-required` on a monthly pass and reboot deliberately.

## Provisioning a host

Prerequisites: a Debian host, root over SSH, and a DNS A/AAAA record pointing at
it. Caddy cannot get a certificate before DNS resolves.

```sh
git clone <this repo> && cd nativeweb
sudo scripts/deploy/provision.sh
```

Idempotent — safe to re-run after editing a unit file. Two things it deliberately
will **not** do on a re-run: overwrite an `/etc/nftables.conf` **an operator has
edited** (the ruleset opens with `flush ruleset`, which would drop every other
table on the host — it prints what to merge by hand instead), and restart PostgreSQL when
`listen_addresses` is already local by any spelling. It installs packages,
creates the `nativeweb` system account with no interactive shell, lays out the
directories, confines PostgreSQL to localhost, installs the units, the nftables
ruleset and the Caddy config, bounds journal retention, and enables the backup
timer. It writes **no secrets**; a script that generates production keys is a
script that has seen them.

> The nftables guard used to read *absent, or carrying our marker*, which never
> fired: this same script installs Debian's `nftables` package, and that package
> ships `/etc/nftables.conf` as a conffile. Every **first** provision took the
> "not ours, refusing" branch and left the host with no firewall — R5 unmet, on a
> clean install, with a line of output that read like a considered decision. It
> now also accepts a conffile `dpkg --verify` reports as unmodified. Found in the
> 2026-08-30 audit sweep, one of eight defects in scripts nobody had run yet.

Then, in order:

1. **Fill in `/etc/nativeweb/secrets.env`.** The connection string, the three
   encryption keys and the SMTP settings. `apps/web` refuses to start outside
   Development without `NativeWeb__Smtp__Host`, and degrades **silently** without
   `ContactKey`, `StoreEncryptionKey` or `PairwiseSalt` — see
   [configuration.md](configuration.md), "Required outside Development".
2. **Fill in the blanks in `/etc/nativeweb/app.env`.** Checklist below.
3. **Set `NATIVEWEB_HOST` and `NATIVEWEB_ACME_EMAIL`** in `/etc/default/caddy`,
   then `systemctl restart caddy`.
   `deploy.sh` refuses to promote while any of them is blank. An `EnvironmentFile`
   line with nothing after the `=` sets the variable to the **empty string**, not
   to absent, so the code's `?? "localhost"` fallbacks do not fire — WebAuthn
   would come up with an empty relying-party domain, `/readyz` would still say
   ready, and every credential would fail at verification.
4. **Generate the backup key.**
   ```sh
   sudo age-keygen -o /etc/nativeweb/backup-age.key
   sudo chmod 0600 /etc/nativeweb/backup-age.key
   ```
   > **Store the private half somewhere that is neither this host nor the backup
   > bucket.** This is the single point of failure in the whole design: lose this
   > key and every object in the bucket is unreadable noise, and no amount of
   > working backups helps. A password manager, a second machine, a printed copy
   > in a safe — anywhere, as long as losing the host does not lose the key.
   > `WHERE THE OFF-HOST COPY LIVES: NOT YET RECORDED`

   Put the public half in `BACKUP_AGE_RECIPIENT` in `/etc/nativeweb/backup.env`,
   along with the bucket, endpoint and access keys.
   `provision.sh` also adds a `pg_ident` map so that root — and only root, plus
   each account for its own role — can reach the `nativeweb` database over the
   unix socket as either `nativeweb` or `postgres`. That is how `deploy.sh`,
   `backup.sh` and `restore.sh` authenticate with **no password in any file**.
   Migrations need both: they create objects (and whoever creates them owns them
   — nothing in `db/migrations` issues a `GRANT`), and migration `0002` needs a
   superuser for the citext extension.
5. **Apply migrations** — the first deploy does this, but a fresh database needs
   the citext extension created by a superuser (migration `0002`). See
   `db/migrate.sh`.

### Required configuration

Every one of these is either required or has a default that is wrong for a real
deployment. `deploy.sh` enforces the first two; the rest are on you.

| Key | Why |
|---|---|
| `NativeWeb__ForwardedHeaders__KnownProxies` | **Enforced by `deploy.sh`.** Unset behind Caddy means `RemoteIpAddress` is the proxy and every client in the world shares one rate-limit partition — across sign-in, register and OAuth. Nothing errors and nothing logs it |
| `OTEL_EXPORTER_OTLP_ENDPOINT` **or** `NativeWeb__Telemetry__Enabled=false` | **Enforced by `deploy.sh`.** Telemetry is opt-out; unset means an exporter retrying forever against a collector that is not there |
| `ConnectionStrings__NativeWeb` | A missing one is not fatal: the app serves and `/readyz` reports not-ready. **Enforced by `deploy.sh`** along with every other key `provision.sh` seeds blank |
| `NativeWeb__ContactKey`, `StoreEncryptionKey`, `PairwiseSalt` | Absent, each degrades security silently. Startup refuses outside Development |
| `NativeWeb__Smtp__Host`, `FromAddress` | Startup refuses without the host; the dev senders write login links to the log |
| `NativeWeb__DataProtection__KeyDirectory` | Must be `/var/lib/nativeweb/keys`. The default is under the app directory, which a blue/green swap moves |
| `NativeWeb__CanonicalBaseUrl` | This host's public origin, emitted by robots.txt and sitemap.xml |
| `NativeWeb__WebAuthn__ServerDomain`, `Origins` | Default `localhost` breaks every credential on a real hostname |
| `NativeWeb__DeviceGrant__VerificationUri` | Default `https://localhost/device` is wrong for any real deployment |
| `NativeWeb__FirstPartyClientId` | Points at the row migration `0055` seeds |

## Releasing

**Build on Linux.** NativeAOT does not cross-compile, so a `linux-x64` publish
must happen on Linux. From WSL on the workstation:

```sh
wsl
cd /mnt/c/home/code/extrode.com/nativeweb
scripts/deploy/build.sh                       # refuses a dirty tree; --force overrides
scripts/deploy/deploy.sh --artifact dist/nativeweb-<sha>.tar.zst --host <host>
```

`deploy.sh` copies itself along with the artifact and re-executes on the host
under sudo, so the deploy procedure and the release it deploys are always the
same version.

What it does, in order — **every step before the last leaves production
untouched**:

1. Refuses if `KnownProxies` is unset or telemetry has no destination.
2. Reads which colour is live from `/etc/caddy/upstream.caddy`; picks the other.
3. Exits 0 unchanged if that release is already live.
4. Unpacks into `/opt/nativeweb/releases/<sha>` and points the idle symlink at it.
5. Runs `db/migrate.sh`. **Aborts on failure**, live colour still serving.
6. Starts the idle colour, polls its own `/readyz`. **Abandons on timeout**,
   stops the idle unit, prints the last 40 journal lines, live colour still serving.
7. Rewrites `upstream.caddy` and `systemctl reload caddy` — graceful, so
   in-flight requests finish on the old colour.
8. Records the promotion and prunes old release directories.

Exit 78 in step 6's log is `EX_CONFIG`: a configuration error, not a crash. The
unit will not restart it (`RestartPreventExitStatus=78`), because the config will
not have changed by the time it comes back.

### Migrations run against the previous release

Step 5 happens while the old binary is still serving. Two consequences:

- **Expand now, contract later.** A migration that drops or renames something the
  running version reads takes production down between steps 5 and 7. Additive
  changes only in the release that introduces them; remove the old column in a
  later release.
- **Split long migrations.** `db/migrate.sh` applies each file in one
  transaction, so every lock the file takes is held until its last statement
  commits. Against an idle database that is free; against a live one it blocks
  readers and writers for the file's duration. See
  [migration-lock-review.md](migration-lock-review.md).

### Rolling back

```sh
sudo /opt/nativeweb/scripts/rollback.sh            # dry run
sudo /opt/nativeweb/scripts/rollback.sh --execute
```

A Caddy reload and nothing else — the previous colour is still running, which is
why `deploy.sh` leaves it up. **It refuses a release that applied migrations**,
naming it, because nothing records whether those were an expand or a contract and
this script must not guess. For a contracting release the way back is a forward
release that reverses it.

### What is deployed right now

```sh
curl -s https://<host>/version          # from inside the running process
cat /opt/nativeweb/blue/RELEASE         # or green — works with nothing running
tail /var/lib/nativeweb/deployments.log
```

## Backups

Nightly at 03:17 UTC (plus up to 15 minutes of jitter), `Persistent=true` so a
host that was off catches up rather than skipping a day silently.
`pg_dump -Fc` piped straight into `age` — the plaintext dump never touches disk —
then uploaded to the configured S3-compatible endpoint as
`<host>/<UTC timestamp>.dump.age`, and objects past the retention period
(default 30 days) are deleted.

```sh
sudo /opt/nativeweb/scripts/backup.sh            # dry run: what it would do
sudo /opt/nativeweb/scripts/backup.sh --execute
systemctl list-timers nativeweb-backup.timer
```

**Accepted limit: up to 24 hours of data can be lost.** Nightly granularity is
the decided recovery point; there is no continuous archiving. On record as a
decision, not an oversight.

### Knowing a backup failed

Two mechanisms, covering two different failures:

- A backup that **runs and fails** triggers `nativeweb-backup-failed.service`,
  which logs the last 40 lines and pings the heartbeat URL's `/fail` endpoint.
- A backup that **never runs at all** is invisible to systemd — nothing failed,
  because nothing happened. Only an external dead-man's-switch catches it:
  `backup.sh` pings `BACKUP_HEARTBEAT_URL` on success, and the watcher alerts
  when the ping does not arrive.

> **`BACKUP_HEARTBEAT_URL` is currently unset, so R15 is not met.** A backup that
> silently stops running will not be noticed. Point it at an external check
> (healthchecks.io or equivalent) to close this. `backup.sh` says so in its own
> output on every run rather than letting the gap pass quietly.

## Restoring

**Drill against a scratch database. Never against production by accident** — the
script requires `--overwrite-production` on top of `--execute` for that.

```sh
sudo /opt/nativeweb/scripts/restore.sh --target nativeweb_drill              # dry run
sudo /opt/nativeweb/scripts/restore.sh --target nativeweb_drill --execute
```

> Until 2026-08-30 this drill could not have worked. `provision.sh` scoped its
> `pg_hba` peer-map line to the database `nativeweb`, so `createdb` (which
> connects to the `postgres` maintenance database) and the restore itself (which
> connects to `nativeweb_drill`) both fell through to Debian's default
> `local all all peer`, where OS root maps to no role. The `createdb` failure was
> swallowed by `2>/dev/null || true` and `pg_restore` died on authentication. The
> production overwrite would have worked and the drill would not — the wrong way
> round for a guard whose whole argument is "practise on the scratch copy".

`pg_restore` exiting 0 means the rows loaded, not that the application works
against them. The drill is not finished until you have pointed a NativeWeb
instance at the restored database and **signed in as an account that existed when
the backup was taken**.

| Measure | Value |
|---|---|
| Time to download and restore | `NOT YET MEASURED` |
| Time to a verified sign-in against restored data | `NOT YET MEASURED` |
| Date of the last successful drill | `NEVER RUN` |

## Disaster recovery

Losing the host is an outage, not a failover — one host, by decision. This is the
whole way back, from nothing but this repository and the backup bucket:

1. Provision a new host: **Provisioning a host**, above.
2. Restore the `age` private key from wherever the off-host copy lives. Without
   it, stop — the bucket is noise.
3. `restore.sh --target nativeweb --from-host <old-host> --overwrite-production --execute`.
   **`--from-host` is not optional here if the new machine has a different name.**
   `backup.sh` writes under the *writing* host's short name, and `restore.sh`
   derives the same prefix from the host it is running on — so a rebuilt machine
   reports "no backups found" against a bucket that is full of them. Omit the flag
   only when the replacement carries the old hostname.
4. Build and deploy the last known-good release: **Releasing**, above.
5. Point DNS at the new host and wait for Caddy to obtain a certificate.

| Measure | Value |
|---|---|
| Total time, bare host to serving | `NOT YET MEASURED` |
| Date last exercised | `NEVER RUN` |

## Known gaps

| Gap | Status |
|---|---|
| Provisioning has never been run on a real host | T15, outstanding — R1 unmet |
| The restore drill has never been run | T15, outstanding — R16 unmet |
| Disaster recovery has never been exercised | T15, outstanding — R17 unmet |
| `MemoryDenyWriteExecute=yes` verified on WSL2, not on the target host | T14, closed with a caveat — see below |
| `BACKUP_HEARTBEAT_URL` unset | R15 unmet until an operator sets it |
| CI cannot restore in package mode | Deferred; `build.sh` pins checkout mode and says why |
| The epass.io cutover scope decision | Out of scope here — `../architecture/002-epass-cutover-capability-gap.md` |

**On `MemoryDenyWriteExecute`.** Measured 2026-08-27, Debian 12 under WSL2: the
`linux-x64` publish started, logged "Now listening", and answered `/healthz` 200
and `/readyz` 503 — the no-database state — with the filter on, indistinguishable
from the same binary with it off. The lazy `dlopen` of `libsodium.so` behind
Argon2id never runs at startup, so it was probed separately under the same
filter: `dlopen` succeeded and `sodium_init()` returned 0. What is **not** proven
is the target host — that was a `--user` unit on the WSL2 kernel, not a
`--system` unit on Debian bare metal. If a first boot there dies without ever
logging, this line is the first thing to comment out.
