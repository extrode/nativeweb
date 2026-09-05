# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Spec 011 T15's other two halves ran on real hosts. Done on
  `feat/011-t15-restore-and-dr-drill`, 2026-09-05.** The restore drill and the disaster-recovery
  rebuild both happened on `incus-host`, and spec 011 is `done`. Measured: backup **1 s**
  (10,359 bytes, the first successful backup this project has taken), restore into a scratch
  database **1 s**, verified serve against restored data **1 s**, bare guest to serving **345 s**.
  `nw-dev.example.com` was serving 200 throughout and its live colour never changed.

  **R16 was amended before the drill because it could not fail.** It required signing in as an
  account that existed at backup time; `apps/sample` keeps no accounts in the database, so it
  passed against the drill database, an empty database and no restore alike. The replacement --
  serve a pre-backup row, do not serve a post-backup one -- failed on its first attempt, which is
  the point: the idle colour was an older release with no `/articles/{slug}` route and answered
  404 for both, so a marker-based check caught what the wording could not.

  A fable review of the plan before execution returned eleven findings and prevented the worst of
  them: running `provision.sh` with a shell on the fleet host would have overwritten the single
  `/etc/caddy/Caddyfile` serving every site on the machine, and loaded an nftables ruleset opening
  with `flush ruleset`. Four more were confirmed by running it -- the DR host needs its own bucket
  credentials, `age` and the AWS CLI, an allowance at the object store, and the app role's
  password set by hand -- and are now steps 3a-3d of the procedure.

  **What is still not true:** the bucket is MinIO on the same physical host, so nothing is offsite;
  `BACKUP_HEARTBEAT_URL` is unset, so R15 stays unmet; and the artifact was repackaged from the
  surviving host rather than rebuilt, because `build.sh` still needs the JauntyQ feed credential.
