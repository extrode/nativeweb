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
- The production first-party-application gap is **closed**: migration 0055 creates the row for
  every deployment, so `db/migrations` alone now yields a serviceable database. Verified by
  running the integration tier against a migrations-only database (258/258) and against the same
  schema with 0055 withheld (33 failures, the exact CI set).
- One deletion awaiting a go-ahead: `db/seed/ci/`, its `ci.yml` step and `CiSeedTests` are now
  redundant. See `../todo.md`.
