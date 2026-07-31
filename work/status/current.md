# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **V1 (spec 003, architecture v2)** — `chore/style-enforcement`: absorb the user's
  `.editorconfig` (VS ruleset, var rules) and hand-converted devmarks Program class; restore
  the `[*]` global section; build-enforce via `EnforceCodeStyleInBuild`; convert
  `apps/web/Program.cs` to an explicit Program/Main; fix repo-wide fallout; suites green.
