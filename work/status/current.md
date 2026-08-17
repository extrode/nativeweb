# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Bounded backlog run, 2026-08-17.** Four items, taken simplest-first while the user is away:
  this status roll, the JauntyQ generator flake writeup, the `db/migrate.sh` apply-and-record
  transaction fix, the skeleton's package-mode CI, and the 400-vs-415 sweep across 23 POST sites.
  The last two are decisions the user made on the way out (415 first; skeleton builds in local
  mode), not calls taken unilaterally.
- Unit counts moved with the CI closeout: **420 unit / 258 integration / 154 render**, all green
  as of `6d2ec78`.
