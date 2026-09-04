# 028 — Tasks

Plan: ./028-plan.md

- [x] T1 One version property, thirteen deletions — files: `Directory.Build.props`, `libs/*/*.csproj` — covers: R1, R2 — done when: `VersionPrefix` and the `IsReleaseBuild`-gated `VersionSuffix` are in `Directory.Build.props`, no packable csproj declares a `<Version>`, and `dotnet pack libs/nativeweb.render` produces `NativeWeb.Render.0.1.0-dev.nupkg`.
- [x] T2 Provenance and README in the package — files: `Directory.Build.targets` — covers: R3, R4 — done when: a packed `NativeWeb.Render` nuspec carries `repository` with both `url` and `commit` and names `README.md` as its readme, and `NativeWeb.Render.Generator` (no README) packs with no NU5039.
- [x] T3 The pack command — files: `tools/pack.ts`, `tools/pack.test.ts`, `.gitignore` — covers: R5, and asserts R2/R3/R4 against the produced files — done when: `deno run -A tools/pack.ts` produces 13 packages into `artifacts/packages/`, verifies each, exits non-zero on any warning, and `deno test -A tools/pack.test.ts` is green.
- [x] T4 Publish on a tag and nowhere else — files: `.github/workflows/publish.yml` — covers: R6 — done when: the workflow triggers only on `v*` tags, holds `packages: write`, fails when the tag disagrees with `VersionPrefix`, and pushes every `.nupkg` to the extrode feed.
- [x] T5 Prove the pack in CI — files: `.github/workflows/ci.yml` — covers: R5 — done when: the `test` job runs `tools/pack.ts` and pushes nothing.
- [x] T6 The skeleton pins what the framework publishes — files: `skeleton/Directory.Packages.props` — covers: R7 — done when: the five `NativeWeb.*` pins carry a comment naming `Directory.Build.props` as their source of truth and match it.
- [x] T7 The guards — files: `tests/nativeweb.repo.tests.unit/PackageVersionTests.cs`, `tests/nativeweb.repo.tests.unit/PublishWorkflowTests.cs` — covers: R9 — done when: both classes are green, and each fails when its subject is broken.
- [x] T8 The versioning policy — files: `docs/02-reference/versioning.md`, `docs/02-reference/breaking-changes.md` — covers: R8 — done when: semver meaning, breaking-change definition, support window and deprecation path each have a stated answer, and `breaking-changes.md` links it.
- [x] T9 Record it — files: `CHANGELOG.md`, `work/status/current.md`, `work/milestones.md`, `work/todo.md`, `docs/architecture/004-consumer-readiness.md`, `docs/specs/028-versioned-package-releases/028-spec.md` — covers: the flow's own requirements — done when: G1 is marked closed with what closed it, G6's stale README count is corrected, milestone 6 reflects the new state, and the spec reads `Status: done`.

## Analyze

Every requirement maps to at least one task: R1 → T1, T7. R2 → T1, T3. R3 → T2, T3. R4 → T2, T3.
R5 → T3, T5. R6 → T4, T7. R7 → T6, T7. R8 → T8. R9 → T3, T7.

Every task traces to a plan section. No orphans, no gaps.
