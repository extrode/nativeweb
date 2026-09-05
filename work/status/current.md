# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Spec 029 - framework-owned templates. Done on `feat/029-framework-templates`, 2026-09-05.**
  Applies spec 015 R7's argument to the four HTML partials it stopped short of:
  `shared/{antiforgery,bot-traps,banner-ok,banner-error}.html` move into `libs/nativeweb.web` and
  are contributed to every consumer as `AdditionalFiles` by `NativeWeb.Web.targets`, the way that
  file already contributes `wwwroot/**`. `RenderTemplateGenerator.LogicalPath` keys on the last
  `/templates/` segment of a physical path, so the logical names and all 11 call sites are
  unchanged. The reason it matters is `bot-traps.html`, which duplicates five `BotTraps` field-name
  constants as literal text: rename one and every product's copy stops matching with no compile
  error, no test failure and no runtime error. Also closes the gap that `apps/sample` cannot render
  a flash banner at all: the contact form is post-redirect-get now, and `ContactFlashTests` drives
  both banners through a real request. All eleven tasks done, R2's package half stated as
  unproven. Spec, plan and tasks in `docs/specs/029-framework-owned-templates/`.

- **A Fable review of both merged branches, and the twelve fixes it produced.** Fable had not
  seen either branch: 028 and the stamp check both went to `review` (opus) on two lenses. Its
  fourteen findings settled to twelve real, one refuted and one not a finding, every one of them
  verified with a shell rather than taken on trust. Merged on `fix/fable-review-findings`.

- **Milestone 1 - the skeleton builds here, not only in CI. `fix/skeleton-stamp-authorization`,
  2026-09-05.** `tools/stamp-check.ts` stamps a product, builds it and runs its unit tier against
  this working tree; the integration tier is opt-in behind `--with-database` because it needs the
  stamped product's own Postgres role. Its first run found the reason the gap mattered: a stamped
  product registered the authorization services only when a sign-in provider was configured and
  died at startup without one. Both are fixed and merged. The half of this that read "nothing
  replaces `SkeletonParityTests`" was retired on 2026-09-05: that suite compared twenty copied C#
  files against a twin, spec 015 removed the copies and the twin left with the identity provider,
  so the invariant is vacuous. What is left of the milestone is the integration tier's database.

- **Spec 028 - versioned package releases. Building on `feat/028-package-releases`, 2026-09-04.**
  Closes G1 in `docs/architecture/004-consumer-readiness.md` and discharges obligation 2 of
  `docs/decisions/2026-08-30-004-package-mode-for-consumers.md`: one `VersionPrefix` in
  `Directory.Build.props` in place of thirteen per-project `<Version>` lines, an opt-in
  `IsReleaseBuild` that keeps every unmarked pack on `-dev`, repository provenance and the
  library's own README inside each `.nupkg`, `tools/pack.ts` as the one pack command, a
  tag-only `publish.yml`, and `docs/02-reference/versioning.md` stating what a version promises.
  Spec, plan and tasks in `docs/specs/028-versioned-package-releases/`.
