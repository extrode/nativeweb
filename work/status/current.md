# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- Nothing in flight. The CI chain is closed: run 32020940278 on `dev` is the first fully green
  run, with `aot-publish` plus all three test tiers passing (409 unit / 258 integration /
  62 E2E). E2E had never executed on a runner before this one.
- The `extrode-legacy-org` org package grant that blocked `fix/ci-package-feed` is **no longer blocking** --
  restore now authenticates and resolves `JauntyQ.*` from GitHub Packages on the runner.
- Next decision, not a task: the first-party application row exists in CI and in local dev, but
  a fresh production database still has none. Before the epass.io cutover it becomes either a
  migration or a documented provisioning step. See `../todo.md`.
