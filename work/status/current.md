# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Nothing in flight.** The CI Node-runtime cutover finished and merged 2026-08-20; see
  `previous.md`. `dev` is pushed as of 2026-08-24.
- The next thing is the justsmtp.com migration (spec 008 R12), and it is **not this repo's work**:
  it runs from `~/.claude/handoffs/justsmtp.com/2026-08-19-epass-signin-migration.md` in its own
  session. Spec 008 therefore stays `Status: draft` here — its own tasks file records that it
  cannot reach `done` until that session lands.
- Scaffolding POC (brainstorm 001 T1.3) is merged; follow-ups on `work/todo.md`, none blocking.
- Suite counts: **494 web-unit / 296 web-integration / 154 render / 64 scaffold-unit /
  17 scaffold-integration / 164 signin-unit / 16 signin-integration / 2 signin-e2e / 64 e2e**.
  web-unit moved 489 → 494 with the CI work: `GitHubPublishedActions_AreOnANode24Major`,
  `NoThirdPartyActionsAreUsed` (replacing `AtLeastOneThirdPartyActionIsPinned`) and the two
  `SdkPinTests` facts. Everything but the 64 web-e2e was run and is green; that tier has not been
  run since spec 008.
- Spec statuses were reconciled 2026-08-24: 001–007 are all `done`. Spec 008 stays `draft` on
  purpose — its R12 is the justsmtp.com migration, which is another repo's work.
- The AOT publish gate for the sign-in library measures exit 0, zero IL2026/IL3050, 16.6 MB exe.
