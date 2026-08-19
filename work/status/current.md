# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Nothing in flight.** Spec 008 finished and merged; see `previous.md`.
- The next thing is the justsmtp.com migration (spec 008 R12), and it is **not this repo's work**:
  it runs from `~/.claude/handoffs/justsmtp.com/2026-08-19-epass-signin-migration.md` in its own
  session. Spec 008 therefore stays `Status: draft` here — its own tasks file records that it
  cannot reach `done` until that session lands.
- Scaffolding POC (brainstorm 001 T1.3) is merged; follow-ups on `work/todo.md`, none blocking.
- Suite counts: **489 web-unit / 296 web-integration / 154 render / 64 scaffold-unit /
  17 scaffold-integration / 164 signin-unit / 16 signin-integration / 2 signin-e2e / 64 e2e**.
  Everything but the 64 web-e2e was run and is green; that tier was not run this session.
- The AOT publish gate for the sign-in library measures exit 0, zero IL2026/IL3050, 16.6 MB exe.
