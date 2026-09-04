# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Spec 028 - versioned package releases. Building on `feat/028-package-releases`, 2026-09-04.**
  Closes G1 in `docs/architecture/004-consumer-readiness.md` and discharges obligation 2 of
  `docs/decisions/2026-08-30-004-package-mode-for-consumers.md`: one `VersionPrefix` in
  `Directory.Build.props` in place of thirteen per-project `<Version>` lines, an opt-in
  `IsReleaseBuild` that keeps every unmarked pack on `-dev`, repository provenance and the
  library's own README inside each `.nupkg`, `tools/pack.ts` as the one pack command, a
  tag-only `publish.yml`, and `docs/02-reference/versioning.md` stating what a version promises.
  Spec, plan and tasks in `docs/specs/028-versioned-package-releases/`.
