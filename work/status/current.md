# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Nothing in flight.** The scaffold generator's seven silent-wrong-answer defects are fixed and
  merged, three on 2026-09-06 and the four the review of that branch left open on the same day;
  see `previous.md`. What is left of that audit is one note in `../todo.md` about a cascaded
  refusal reporting the outer fault before the inner one, which costs a second build and hides
  nothing.

  **`nativeweb-dev` is moving onto the `incus-host` fleet backup**, which is how R14, R15 and
  R17b close -- together, rather than one `backup.env` at a time: backup-tool, nightly `pg_dump -Fc`
  into offsite object storage *and* a offsite storage, a daily integrity check and a six-hourly dead-man
  probe. All three stay **unmet, closing** until a fleet run has held this database. The two-word
  change is merged to the host's ops repository's `dev`; `/opt/host-backup` on the host still runs
  the pre-review scripts, and redeploying is a founder call there.

  **Nothing is backing this host up until that runs.** The MinIO bucket went on 2026-09-06 with
  every backup in it, and no fleet run has captured this VM yet. The 03:17 timer is still enabled
  and fails nightly against a dead endpoint -- inert, not cover: the upload dies and the `EXIT`
  trap removes the dump, so disabling it widens no window. It waits for a host pass.

  **`/etc/nativeweb/*.env` reaches a backup for the first time** under the fleet, in the weekly
  tier-3 tar, offsite-storage-only: seven days on one provider against one day on two for the data. `backup.sh`
  never carried those files at all, so this is a gain rather than a loss -- on paper. On the host
  tier 3 has never produced a snapshot: 25 instances against 15 classified, and the coverage check
  exits before the first export. Ten need a founder call in the host's ops repo before that tier
  runs at all.

  **The age private key is off the fleet as of 2026-09-06**, in the operator's own store, which the
  runbook records without naming, because this repository is public. That closes one of R17b's two
  blockers and not R17b: readable needs the key and the bytes, and the bytes are still only on the
  machine being backed up. The 2026-09-01 key is not being rotated -- it was printed into a session
  during the drill, and the operator decided that reach acceptable for `nativeweb-dev`.
