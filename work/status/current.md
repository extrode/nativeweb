# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- Nothing in flight. `fix/ci-package-feed` is merged into `dev`: the repo now has a
  `nuget.config` pointing `JauntyQ.*` at GitHub Packages, ci.yml authenticates with the job's
  own `GITHUB_TOKEN`, and CI runs on `dev` and `main` instead of skipping exactly the branches
  work lands on.
- **Blocked on one action outside the repo:** the `extrode-legacy-org` org has to grant this repo read
  access to the `jauntyq` packages. Until then CI's restore fails 401 rather than NU1101 — the
  wiring is right and the credential is not yet permitted.
