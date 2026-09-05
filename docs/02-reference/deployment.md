# Deployment runbook

> **The application this runbook used to deploy left the repository on 2026-08-31**
> (`../decisions/2026-08-31-007-idp-leaves-nativeweb.md`). `scripts/deploy/` was retargeted the
> same day: `build.sh` publishes `apps/sample` by default and takes `PROJECT=<csproj>` for a
> stamped product's own app, and it installs the executable under the fixed name `nativeweb`, so
> nothing downstream — `deploy.sh`, `nativeweb@.service`, Caddy — names the project. The warning
> below still stands: nothing here has been run on a real host.

Spec: `../specs/011-production-deployment/011-spec.md`
Scripts: `../../scripts/deploy/`

The one document for provisioning, releasing, rolling back, backing up and
recovering a NativeWeb production host. Everything here is a command to run or a
decision with a stated default.

> **Releasing has now been executed for real; restoring and disaster recovery
> have not.** On 2026-09-01 a `linux-x64` artifact built by `build.sh` was
> deployed by `deploy.sh` to `nativeweb-dev` (10.0.0.30, Debian 12, an Incus
> guest on `incus-host`) and promoted to blue, and the result is public at
> `https://nw-dev.example.com`. That closes R1. **R16 and R17 remain unmet** —
> nobody has restored a backup taken by `backup.sh`, and the elapsed times in
> **Restoring** and **Disaster recovery** below still read `NOT YET MEASURED`
> because they are literal.
>
> **Three attempts were needed and two of them were defects in these scripts,**
> which is what a first real run is for. Both are fixed and both now have a test:
> `ConditionPathExists` sat in `[Service]`, where systemd logs "Unknown key name"
> and ignores it, so the skip it promises had never happened on any host; and
> `chmod -R g-w,o-rwx` could not restore group bits that `cp -a` from a `mktemp -d`
> stage never granted, so the release directory arrived `drwx------` and systemd
> failed the unit at `status=200/CHDIR`. The third refusal was not a defect — the
> host carried `NativeWeb__*` keys from a provisioning run predating the config
> prefix, and the preflight said so and stopped, which is the behaviour it is for.
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
  Ubuntu LTS works unchanged. `TESTED ON: Debian 12 (bookworm), x86-64,
  2026-09-01` — an Incus guest, provisioned and deployed to.
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

**Every key below is spelled `<Prefix>__…`, where `<Prefix>` is the configuration
section the deployed binary reads** — `Sample` for `apps/sample`, your product's
own name for anything stamped from `skeleton/`. Pass it to `provision.sh --prefix`
and `build.sh` stamps the matching `config_prefix` into the artifact, which is
where `deploy.sh` reads it from. Until 2026-08-31 every key here was spelled
`NativeWeb__`, a section no binary in this repository binds: the preflight demanded
nine keys nothing reads and passed on the two that mattered while they stayed unset.

1. **Fill in `/etc/nativeweb/secrets.env`.** The connection string, and nothing
   else. Two spellings are seeded and one of them applies:
   `ConnectionStrings__<Prefix>` for `apps/sample`,
   `<Prefix>__Db__ConnectionString` for a stamped product. Delete the other.
2. **Fill in the blanks in `/etc/nativeweb/app.env`.** Checklist below.
3. **Set `NATIVEWEB_HOST` and `NATIVEWEB_ACME_EMAIL`** in `/etc/default/caddy`,
   then `systemctl restart caddy`.
   `deploy.sh` refuses to promote while any of the four keys below is blank. An
   `EnvironmentFile` line with nothing after the `=` sets the variable to the
   **empty string**, not to absent, so a code fallback does not fire.
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
   — nothing in the migrations issued a `GRANT`), and migration `0002` needed a
   superuser for the citext extension.
5. **Apply migrations** — the first deploy does this, but a fresh database needs
   the citext extension created by a superuser (migration `0002`). See
   `db/migrate.sh`.

### Required configuration

Four keys, all enforced by `deploy.sh`'s preflight, all spelled with the prefix
stamped into the artifact. Every check fails when the app would misbehave and
passes when it would not — a preflight that passes while the thing it prevents is
happening is worse than none, because it is also an assurance.

| Key | Why |
|---|---|
| `<Prefix>__ForwardedHeaders__KnownProxies` | Unset behind Caddy means `RemoteIpAddress` is the proxy hop and every client in the world shares one rate-limit partition. Nothing errors and nothing logs it |
| `ConnectionStrings__<Prefix>` **or** `<Prefix>__Db__ConnectionString` | Absent, `apps/sample` serves its in-memory demo store with a green `/healthz`, and a stamped product times out the `/readyz` poll with no named cause |
| `<Prefix>__DataProtection__KeyDirectory` | Must be under `/var/lib/nativeweb`. That is the only path `ReadWritePaths` names, and `ProtectSystem=strict` makes the rest read-only. Unset, the key ring lands under a read-only working directory and cannot persist: every restart remints the antiforgery keys and every form POST answers 400 after each deploy, with `/readyz` green. The app now refuses to start rather than doing that silently |
| `OTEL_EXPORTER_OTLP_ENDPOINT`, when `<Prefix>__Telemetry__Enabled=true` | Telemetry is opt-**in**: the framework defaults `Enabled` to false. The preflight fails only when it is on with nowhere to send. Until 2026-08-31 this gate asserted the opposite, which was true of the app that left the repo and demanded busywork ever after |

**Nine keys were deleted from this table on 2026-08-31**: `ContactKey`,
`StoreEncryptionKey`, `PairwiseSalt`, both `Smtp` settings, both `WebAuthn`
settings, `DeviceGrant__VerificationUri`, `CanonicalBaseUrl` and
`FirstPartyClientId`. No code in this repository binds any of them — they left
with the identity provider — and `FirstPartyClientId` pointed at a row seeded by
migration `0055`, which left with `db/migrations`. The preflight required all of
them, so deploying the scripts' own default project meant inventing values for a
departed product's settings.

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

Measured on the first real release, `nativeweb-dev`, 2026-09-01 — the fourth
attempt, after the two defects in the note at the top of this document were
fixed. A cold host: nothing was serving, so step 3 had no live colour to compare
against and step 8 had nothing to prune.

| Measure | Value |
|---|---|
| `deploy.sh`, artifact on the host to promoted | **1.774 s** |
| Release | `7436517`, promoted to blue |
| `/version` after promotion | `0.1.0+743651707dbfdce70f3bc723f845446d6128088e` |
| `/readyz` through the public origin | `200`, body `ready`, 0.022 s |
| `/healthz` | `200`, 0.023 s |
| `/` | `302` to `/admin`, 0.050 s |
| Date last exercised | 2026-09-01 |

The timings are from `incus-host` over the LAN and through the edge, so they
include Caddy and TLS but not a WAN round trip. Build time is not recorded here:
the publish ran on `build-host` rather than the workstation, and one
measurement on one build box is not a figure this runbook should quote.

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

### The documentation site, at `/docs`

`scripts/build-docs.sh` renders `docs/` into the committed, versioned `docs-site/`,
and from there it travels in the artifact exactly as `db/` does. Nothing extra to
run at release time:

| Where | What |
|---|---|
| `docs-site/` in the repo | the tracked site, one frozen directory per release plus `unversioned/` |
| `docs_dir=` in `RELEASE` | `docs` when this artifact carries a site, empty when it does not |
| `/opt/nativeweb/docs` | a symlink to the live colour's `docs/`, repointed at promotion and at rollback |
| `handle_path /docs/*` in the Caddyfile | serves it; `redir /docs /docs/ 308` in front, because the index's links are relative |

**Absent docs are not an error.** A product that has never run `build-docs.sh`
builds and deploys normally, and `/docs` is a 404 until it does. A `DOCS_DIR` that
exists *without* a `versions.json` **is** an error: that is either the wrong
directory or a half-finished build, and shipping it would put a directory listing
under the product's own hostname.

**The docs follow the release, so a rollback rolls them back too** — which matters
less than it sounds, because the site is versioned: every release's documentation
stays in the tree whichever one is serving.

One thing to know before copying the tracked `Caddyfile` onto a host: **a guest
behind an edge proxy needs a different one.** `nativeweb-dev` terminates TLS at
`incus-host`, so its Caddyfile is `auto_https off` on `:80` with
`trusted_proxies`, and the `/docs` block above is the only part the two share
verbatim.

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
instance at the restored database and **served a row that existed when the backup
was taken, while a row written after it is absent**.

> **That criterion replaced "signed in as an account that existed when the backup
> was taken" on 2026-09-05.** `apps/sample` keeps no accounts in the database --
> its only sign-in is a passphrase read from `secrets.env` -- so the old wording
> passed against the drill database, against an empty database, and against no
> restore at all. It tested nothing. The replacement is falsifiable: write a
> marker row *after* the backup, and the restored database must not have it.

Drill of 2026-09-05, on `nativeweb-dev`, against MinIO on the same fleet host:
`drill-pre-20260905` was written before the backup and `drill-post-20260905`
after it. The restored database held the first and not the second, and the
current release served `/articles/restore-drill-pre` 200 and
`/articles/restore-drill-post` 404 against it.

| Measure | Value |
|---|---|
| Time to download and restore | **1 s** (10,359-byte dump, 8 MB database) |
| Time to a verified serve against restored data | **1 s** |
| Date of the last successful drill | **2026-09-05** |

The dump is small because the database is nearly empty; these figures are a
floor, not a capacity measurement.

## Disaster recovery

Losing the host is an outage, not a failover — one host, by decision. This is the
whole way back, from nothing but this repository and the backup bucket:

1. Provision a new host: **Provisioning a host**, above.
2. Restore the `age` private key from wherever the off-host copy lives. Without
   it, stop — the bucket is noise.
3a. Install `age` and the AWS CLI if the image does not carry them, and make sure the
   backup bucket accepts connections from the new host's address.
3b. Fill `BACKUP_S3_BUCKET`, `BACKUP_S3_ENDPOINT`, `AWS_ACCESS_KEY_ID` and
   `AWS_SECRET_ACCESS_KEY` in the **new** host's `/etc/nativeweb/backup.env`.
   `provision.sh` seeds them empty and `restore.sh` dies on the first of them.
3c. Carry `secrets.env` and `app.env` across from wherever they are kept.
3d. Set the app role's password to match that `secrets.env`:
   `sudo -u postgres psql -c "alter role nativeweb with password '...'"`. `pg_dump -Fc`
   dumps one database, not roles, so the restore does not bring it.
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
| Total time, bare host to serving | **345 s** (5 m 45 s) |
| Date last exercised | **2026-09-05** |

Measured 2026-09-05 rebuilding `nativeweb-dr` on `incus-host`: guest created,
`provision.sh --edge 10.0.0.1` (26 s), age key installed from the operator's
off-host copy, `restore.sh --target nativeweb --from-host nativeweb-dev
--overwrite-production --execute`, `deploy.sh --incus`, then `/healthz` 200,
`/readyz` 200 and the pre-backup article served. **Four caveats on that number:**

- The artifact was **not rebuilt**. `build.sh` needs the JauntyQ feed credential
  (see the gap table below), so the running release was repackaged from
  `nativeweb-dev` with `tar --zstd`, the same shape `build.sh` emits. A real
  rebuild adds however long a publish takes to the 345 s.
- The bucket is MinIO **on the same physical host**. Losing `incus-host`
  loses the guest, the replacement guest, the bucket and the key together, so this
  exercises the mechanics of R17 and nothing about the offsite property.
- Four preconditions the numbered list above did not state were found by running
  it: the DR host needs the bucket, endpoint and both AWS keys in its own
  `backup.env` (`provision.sh` seeds them empty and `restore.sh` dies on the
  first), `age` and the AWS CLI must be installed, the object storage must allow
  the new host's address, and the app role's password must be set to match the
  carried `secrets.env` -- `pg_dump -Fc` dumps one database, not roles. They are
  now steps 3a-3d.
- `provision.sh` must run **inside the guest**. On the fleet host it would
  overwrite `/etc/caddy/Caddyfile`, which serves every site on the machine, and
  its nftables ruleset opens with `flush ruleset`.

## Known gaps

| Gap | Status |
|---|---|
| ~~Provisioning has never been run on a real host~~ | **Closed 2026-09-01** — provisioned and deployed to on `nativeweb-dev`. R1 met |
| ~~The restore drill has never been run~~ | **Closed 2026-09-05** — drilled on `nativeweb-dev`, R16 met under the amended criterion above |
| ~~Disaster recovery has never been exercised~~ | **Closed 2026-09-05** — `nativeweb-dr` rebuilt from repo plus backup in 345 s, R17 met with the four caveats above |
| ~~The nightly backup fails on `nativeweb-dev`~~ | **Closed 2026-09-05** — bucket, endpoint and credentials filled in, first successful backup taken. The bucket is MinIO on the same fleet host, so this is not yet an offsite backup |
| `MemoryDenyWriteExecute=yes` verified on WSL2, not on the target host | T14, closed with a caveat — see below |
| `BACKUP_HEARTBEAT_URL` unset | R15 unmet until an operator sets it |
| The build host needs a JauntyQ feed credential | `build.sh` pins **package** mode (checkout mode has had no sibling to reference since JauntyQ split on 2026-09-05). The feed needs auth even to read, and WSL has its own NuGet profile, so a credential added on Windows does not reach the shell that builds. The script preflights the restore and prints the `dotnet nuget update source` command; an operator runs it once per shell |
| ~~The epass.io cutover scope decision~~ | **Closed 2026-08-31, and not a NativeWeb concern at all** — `../decisions/2026-08-31-008-epass-cutover-closed.md`. Nothing in this runbook is gated on it. |

**On `MemoryDenyWriteExecute`.** Measured 2026-08-27, Debian 12 under WSL2: the
`linux-x64` publish started, logged "Now listening", and answered `/healthz` 200
and `/readyz` 503 — the no-database state — with the filter on, indistinguishable
from the same binary with it off. The lazy `dlopen` of `libsodium.so` behind
Argon2id never runs at startup, so it was probed separately under the same
filter: `dlopen` succeeded and `sodium_init()` returned 0. What is **not** proven
is the target host — that was a `--user` unit on the WSL2 kernel, not a
`--system` unit on Debian bare metal. If a first boot there dies without ever
logging, this line is the first thing to comment out.
