# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **T6 Search (spec 001, DevMarks)** — Postgres FTS via the GIN index, ranking, combined with
  the tag filter (R14). Worker writing implementation in `.worktrees/feat-devmarks-search`;
  import/export endpoints (T8 remainder) running in parallel in `.worktrees/feat-devmarks-transfer`.
