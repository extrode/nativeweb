# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **epass sign-in library (spec 008)** — in flight on `feat/008-signin-library`. T1-T14 done and
  committed; T15 (integration, real PostgreSQL), T16 (E2E, Playwright), T17 (docs), T18 (reviews)
  and T19 (the justsmtp handoff) remain.
- Two deliberate deviations from the approved plan, both in `~/.claude/away-questions.md`: the
  conformance fixture ships as its own project (xunit must not be in a production package's
  closure), and `AddEpassSignIn` takes only a configure delegate rather than the plan's
  `(IConfigurationSection, Action<T>)` pair.
- Scaffolding POC (brainstorm 001 T1.3) is merged; follow-ups on `work/todo.md`, none blocking.
- Suite counts: **489 web-unit / 296 web-integration / 154 render / 64 scaffold-unit /
  17 scaffold-integration / 152 signin-unit / 64 e2e**. All green except e2e, not run this session.
- The AOT publish gate for the sign-in library measures exit 0, zero IL2026/IL3050, 16.6 MB exe.
