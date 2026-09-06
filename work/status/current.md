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

  **The age private key still needs to be off the fleet.** It was printed once during the drill
  and exists nowhere else in writing. Without it every backup in the bucket is noise, which makes
  it the single point of failure the whole procedure rests on.
