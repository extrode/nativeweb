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
  drill and existed nowhere else in writing; the operator now holds an off-host copy, so the
  single point of failure the whole procedure rested on is closed. It moves neither R14 nor R17b
  -- both are about where the dump lives, not where the key does -- and the runbook does not name
  the store, because this repository is public.
