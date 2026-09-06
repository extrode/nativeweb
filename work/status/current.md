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

  **`nativeweb-dev` is moving onto the `incus-host` fleet backup**, which closes R14, R15 and
  R17b together rather than one `backup.env` at a time: backup-tool, nightly `pg_dump -Fc` into
  offsite object storage *and* a offsite storage, a daily integrity check and a six-hourly dead-man
  probe. The two-word change is committed in the host's ops repository on
  `chore/nativeweb-dev-into-host-backup` for its owners, since the fleet is theirs.

  **Nothing is backing this host up until that runs.** The MinIO bucket went on 2026-09-06 with
  every backup in it, and no fleet run has captured this VM yet; the 03:17 timer is still enabled
  and now fails nightly against a dead endpoint. Retiring that timer waits on a fleet run that
  demonstrably held this database, so the two never overlap the wrong way.

  **`/etc/nativeweb/*.env` and the age key are in no backup but the weekly tier-3 tar**, which is
  offsite-storage-only. Seven days on one provider for the config that makes the host bootable, against one day
  on two for the data.

  **The age private key is off the fleet as of 2026-09-06**, in the operator's own store, which the
  runbook records without naming, because this repository is public. That closes one of R17b's two
  blockers and not R17b: readable needs the key and the bytes, and the bytes are still only on the
  machine being backed up. The 2026-09-01 key is not being rotated -- it was printed into a session
  during the drill, and the operator decided that reach acceptable for `nativeweb-dev`.

  **The open question in front of the backup work is still open**, and it may delete some of it:
  whether incus-host's Incus/ZFS snapshots leave the box. If they do, the nightly `pg_dump` is
  largely redundant for disaster recovery and R16 is better restated as a snapshot-restore drill.
