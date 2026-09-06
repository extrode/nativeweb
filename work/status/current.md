# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Nothing in flight.** The fable review of `origin/dev..dev` is acted on and both branches are
  merged; see `previous.md`.

  **Three requirements are open on `nativeweb-dev` and none of them is code.** R14 and R17b: the
  backup bucket is a MinIO container on `incus-host`, the machine being backed up, so losing
  that machine loses every backup with it. One `backup.env` pointing at storage elsewhere closes
  both. R15: `BACKUP_HEARTBEAT_URL` is unset, so a backup that stops running is unnoticed.

  **The age private key is off the fleet as of 2026-09-06.** It had been printed once during the
  drill and existed nowhere else in writing; the operator now holds an off-host copy, which the
  runbook records without naming the store, because this repository is public.

  **That closes one of R17b's two blockers, not R17b.** Its criterion is that the backup is still
  readable once the host is gone, and readable needs the key and the bytes; the bytes are still
  only in a MinIO container on the machine being backed up, so losing `incus-host` is still
  unrecoverable. R14 is about location alone and is untouched.

  **The 2026-09-01 key is not being rotated.** It was printed into a session and read back out of
  that session's own output during the drill, so it lives in whatever transcript that session
  left; the operator decided on 2026-09-06 that the reach is acceptable for `nativeweb-dev`. The
  runbook records the decision, what would reopen it, and that it does not carry to a production
  key.
