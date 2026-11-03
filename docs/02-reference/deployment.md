# Deployment runbook

Spec: `../specs/011-production-deployment/011-spec.md` (the R-numbers below are its requirements)
Scripts: `../../scripts/deploy/`

The one document for provisioning, releasing, rolling back, backing up and
recovering a NativeWeb production host. Everything here is a command to run or a
decision with a stated default.

`build.sh` publishes `apps/sample` by default and takes `PROJECT=<csproj>` for a stamped product's
own app. It installs the executable under the fixed name `nativeweb`, so nothing downstream
(`deploy.sh`, `nativeweb@.service`, Caddy) names the project.

> **Releasing, restoring and rebuilding a guest from backup have all been run on a real host.
> What has never been rehearsed is losing the machine.** The backup bucket was an S3-compatible
> service on the physical host being backed up, so losing that machine loses the guest, the
> replacement and the bucket together. **R14 and R17b are unmet.** R17b needs the backup to be
> readable after the host is gone, which takes both the bytes and the `age` key; the key is held
> off-host and the bytes are not. R14 is about location alone. **R15 is unmet too:**
> `BACKUP_HEARTBEAT_URL` is unset, so a backup that stops running goes unnoticed.

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

Both are choices rather than defaults.

- **Distribution: Debian stable, x86-64.** `provision.sh` targets apt and systemd;
  Ubuntu LTS works unchanged. Tested on Debian 12 (bookworm), x86-64, a VM guest.
- **`unattended-upgrades` is on, security pocket only, and does NOT reboot.**
  There is one host and no failover, so an unattended 03:00 reboot is an
  unannounced outage. Kernel and libc updates therefore wait for an operator:
  check `/var/run/reboot-required` on a monthly pass and reboot deliberately.

## Provisioning a host

Prerequisites: a Debian host, root over SSH, and a DNS A/AAAA record pointing at
it. Caddy cannot get a certificate before DNS resolves.

```bash
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

> The firewall guard accepts an `/etc/nftables.conf` that is absent, that carries our marker, or
> that is Debian's packaged conffile unmodified (`dpkg --verify` reports it clean). Debian's
> `nftables` package ships that file, so a guard that accepted only the first two would refuse
> every first provision and leave the host with no firewall.
Then, in order:

**Every key below is spelled `<Prefix>__…`, where `<Prefix>` is the configuration
section the deployed binary reads** — `Sample` for `apps/sample`, your product's
own name for anything stamped from `skeleton/`. Pass it to `provision.sh --prefix`
and `build.sh` stamps the matching `config_prefix` into the artifact, which is
where `deploy.sh` reads it from.

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
   > Keep the private half in the operator's own store, and do not name that store in this file:
   > a runbook that names the vault holding the key is half of the compromise. A key that was
   > printed into a terminal session exists in whatever transcript, scrollback or shell history that
   > session left behind; do not carry such a key to production. Rotating means `age-keygen`
   > afresh, a new `BACKUP_AGE_RECIPIENT`, and keeping the old private key for as long as objects
   > encrypted to the old recipient are inside the retention window.

   Put the public half in `BACKUP_AGE_RECIPIENT` in `/etc/nativeweb/backup.env`,
   along with the bucket, endpoint and access keys.
   `provision.sh` also adds a `pg_ident` map so that root — and only root, plus
   each account for its own role — can reach the `nativeweb` database over the
   unix socket as either `nativeweb` or `postgres`. That is how `deploy.sh`,
   `backup.sh` and `restore.sh` authenticate with **no password in any file**.
   Migrations need both: they create objects (and whoever creates them owns them, since nothing in
   the migrations issues a `GRANT`), and an extensions migration needs a superuser.
5. **Apply migrations.** The first deploy does this. A migration directory that carries an
   `NNNN_extensions.sql` needs it applied by a superuser; see `db/migrate.sh`.

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
| `OTEL_EXPORTER_OTLP_ENDPOINT`, when `<Prefix>__Telemetry__Enabled=true` | Telemetry is opt-**in**: the framework defaults `Enabled` to false. The preflight fails only when it is on with nowhere to send |


## Releasing

**Build on Linux.** NativeAOT does not cross-compile, so a `linux-x64` publish must happen on
Linux. From the repository root (under WSL on Windows):

```bash
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

A reference run on a cold host (nothing serving, so step 3 had no live colour to compare against
and step 8 had nothing to prune):

| Measure | Value |
|---|---|
| `deploy.sh`, artifact on the host to promoted | **1.774 s** |
| `/readyz` through the public origin | `200`, body `ready`, 0.022 s |
| `/healthz` | `200`, 0.023 s |

The timings include Caddy and TLS but not a WAN round trip. Build time is not recorded: one
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

```bash
sudo /opt/nativeweb/scripts/rollback.sh            # dry run
sudo /opt/nativeweb/scripts/rollback.sh --execute
```

A Caddy reload and nothing else — the previous colour is still running, which is
why `deploy.sh` leaves it up. **It refuses a release that applied migrations**,
naming it, because nothing records whether those were an expand or a contract and
this script must not guess. For a contracting release the way back is a forward
release that reverses it.

### What is deployed right now

```bash
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

A guest behind an edge proxy needs a different Caddyfile from the tracked one: TLS terminates at
the edge, so it is `auto_https off` on `:80` with `trusted_proxies`. The `/docs` block above is the
only part the two share verbatim.

## Backups

Nightly at 03:17 UTC (plus up to 15 minutes of jitter), `Persistent=true` so a
host that was off catches up rather than skipping a day silently.
`pg_dump -Fc` piped straight into `age` — the plaintext dump never touches disk —
then uploaded to the configured S3-compatible endpoint as
`<host>/<UTC timestamp>.dump.age`, and objects past the retention period
(default 30 days) are deleted.

```bash
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

> **`BACKUP_HEARTBEAT_URL` is unset, so R15 is not met by this script.** A backup
> that silently stops running will not be noticed. Point it at an external check
> (a dead-man's-switch service) to close it. `backup.sh` says so in its own output
> on every run rather than letting the gap pass quietly.

## Restoring

**Drill against a scratch database. Never against production by accident** — the
script requires `--overwrite-production` on top of `--execute` for that.

```bash
sudo /opt/nativeweb/scripts/restore.sh --target nativeweb_drill              # dry run
sudo /opt/nativeweb/scripts/restore.sh --target nativeweb_drill --execute
```

`pg_restore` exiting 0 means the rows loaded, not that the application works
against them. The drill is not finished until you have pointed a NativeWeb
instance at the restored database and **served a row that existed when the backup
was taken, while a row written after it is absent**.

> `apps/sample` keeps no accounts in the database -- its only sign-in is a passphrase read from
> `secrets.env` -- so "signed in as an account that existed when the backup was taken" would pass
> against an empty database and against no restore at all. Writing a marker row *after* the backup
> makes the check falsifiable: the restored database must not have it.

| Measure | Value |
|---|---|
| Time to download and restore | **1 s** (10,359-byte dump, 8 MB database) |
| Time to a verified serve against restored data | **1 s** |

Those figures come from a drill that wrote one marker article before the backup and one after it.
The restored database held the first and not the second, and the release served the first 200 and
the second 404 against it. The dump is small because the database was nearly empty; the figures are
a floor, not a capacity measurement.

## Disaster recovery

Losing the host is an outage, not a failover — one host, by decision. This is the
whole way back, from nothing but this repository and the backup bucket.

1. Provision a new host: **Provisioning a host**, above.
2. Restore the `age` private key from the operator's off-host store. Without it, stop: the bucket
   is noise.
3. Install `age` and the AWS CLI if the image does not carry them, and make sure the
   backup bucket accepts connections from the new host's address.
4. Fill `BACKUP_S3_BUCKET`, `BACKUP_S3_ENDPOINT`, `AWS_ACCESS_KEY_ID` and
   `AWS_SECRET_ACCESS_KEY` in the **new** host's `/etc/nativeweb/backup.env`.
   `provision.sh` seeds them empty and `restore.sh` dies on the first of them.
5. Carry `secrets.env` and `app.env` across from wherever they are kept.
6. Set the app role's password to match that `secrets.env`:
   `sudo -u postgres psql -c "alter role nativeweb with password '...'"`. `pg_dump -Fc`
   dumps one database, not roles, so the restore does not bring it.
7. `restore.sh --target nativeweb --from-host <old-host> --overwrite-production --execute`.
   **`--from-host` is not optional here if the new machine has a different name.**
   `backup.sh` writes under the *writing* host's short name, and `restore.sh`
   derives the same prefix from the host it is running on — so a rebuilt machine
   reports "no backups found" against a bucket that is full of them. Omit the flag
   only when the replacement carries the old hostname.
8. Build and deploy the last known-good release: **Releasing**, above.
9. Point DNS at the new host and wait for Caddy to obtain a certificate.

| Measure | Value |
|---|---|
| Total time, bare host to serving | **345 s** (5 m 45 s) |
| `provision.sh --edge <edge-ip>` | **26 s** |

Measured rebuilding a guest on the same physical host: guest created, `provision.sh`, age key
installed, `restore.sh --target nativeweb --from-host <old-host> --overwrite-production --execute`,
`deploy.sh --incus`, then `/healthz` 200, `/readyz` 200 and the pre-backup article served.
**Four caveats on that number:**

- The artifact was **not rebuilt**. The running release was repackaged with `tar --zstd`, the same
  shape `build.sh` emits. A real rebuild adds however long a publish takes to the 345 s.
- The bucket was an S3-compatible service **on the same physical host**. Losing that host loses the
  guest, the replacement guest and the bucket together, so this exercises R17a and leaves R17b and
  R14 unmet. The `age` key is held off-host, which removes one of R17b's two blockers (readable
  needs the key and the bytes) and neither of R14's, which is about location alone.
- Four preconditions were found by running it: the DR host needs the bucket, endpoint and both AWS
  keys in its own `backup.env` (`provision.sh` seeds them empty and `restore.sh` dies on the first),
  `age` and the AWS CLI must be installed, the object storage must allow the new host's address,
  and the app role's password must be set to match the carried `secrets.env`, because `pg_dump -Fc`
  dumps one database, not roles. They are steps 3 to 6.
- `provision.sh` must run **inside the guest**. On a shared physical host it would overwrite
  `/etc/caddy/Caddyfile`, which serves every site on the machine, and its nftables ruleset opens
  with `flush ruleset`.

## Known gaps

| Gap | Status |
|---|---|
| No backup is offsite | **R14 and R17b unmet.** Every backup drilled so far lived on the physical host being backed up. Closed when a backup held on separate storage has been restored from |
| `MemoryDenyWriteExecute=yes` verified on WSL2, not on the target host | Closed with a caveat; see below |
| `BACKUP_HEARTBEAT_URL` unset | **R15 unmet** until an operator sets it, for any deployment running `backup.sh` |

**On `MemoryDenyWriteExecute`.** The unit sets `MemoryDenyWriteExecute=yes`. A `linux-x64` publish was
checked under that filter on Debian 12 under WSL2: it started, logged "Now listening", and answered
`/healthz` 200 and `/readyz` 503 (the no-database state), the same as with the filter off. The lazy
`dlopen` of `libsodium.so` behind Argon2id never runs at startup, so it was probed separately under
the same filter: `dlopen` succeeded and `sodium_init()` returned 0. What is **not** proven is a
`--system` unit on Debian bare metal. If a first boot there dies without ever logging, this line is
the first thing to comment out.
