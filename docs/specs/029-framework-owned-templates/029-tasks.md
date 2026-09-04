# 029 — Tasks
Plan: ./029-plan.md

- [ ] T1 Move the four templates into the framework — files: `libs/nativeweb.web/templates/shared/{antiforgery,bot-traps,banner-ok,banner-error}.html`, `libs/nativeweb.web/nativeweb.web.csproj` — covers: R1 — done when: all four are under `libs/nativeweb.web/templates/shared/` by `git mv`, the csproj packs `templates\**\*` to `PackagePath="templates\"` beside the existing `wwwroot` line, and `tools/pack.ts` still verifies `NativeWeb.Web`.
- [ ] T2 Contribute them from the targets — files: `libs/nativeweb.web/build/NativeWeb.Web.targets` — covers: R2, R3, R5 — done when: a `NativeWebTemplatesDirectory` property mirrors `NativeWebAssetsDirectory`, an item group gated on `NativeWebImportTemplates != false` adds `$(NativeWebTemplatesDirectory)**\*.html` as `AdditionalFiles`, and `apps/sample` builds with its own copies still present (the collision guard lands in T3).
- [ ] T3 Make an override declared and an undeclared clash a build error — files: `libs/nativeweb.web/build/NativeWeb.Web.targets` — covers: R4 — done when: `<NativeWebTemplateOverride Include="shared\x.html" />` removes that file from the framework set and logs a `<Message>` naming it; a `<Target BeforeTargets="CoreCompile">` intersects the framework set with the product's `templates/**/*.html` on logical path and raises an error listing every undeclared clash. Proved by building `apps/sample` before T4 and seeing the error name all four.
- [ ] T4 Delete the consumers' copies — files: `apps/sample/templates/shared/{antiforgery,bot-traps}.html`, `skeleton/apps/__product__/templates/shared/{antiforgery,bot-traps,banner-ok,banner-error}.html` — covers: R6 — done when: neither tree carries a framework-owned template, no csproj changed, and `apps/sample` plus a stamped product both build and render as before.
- [ ] T5 Pin the bot-trap markup to the `BotTraps` constants — files: `tests/nativeweb.web.tests.unit/BotTrapTemplateTests.cs` — covers: R7 — done when: the test reads the field-name constants off the `BotTraps` type by reflection (not a hand-copied list) and asserts both directions — every constant appears as a `name="…"` in the template, and every `name="…"` is a constant — and fails when either side is renamed alone.
- [ ] T6 Refuse a reintroduced copy — files: `tests/nativeweb.repo.tests.unit/` (extend `AssetParityTests.cs` or add alongside it) — covers: R8 — done when: a framework-owned template placed under a consumer's `templates/` fails the repo tier unless that consumer's csproj declares the matching `NativeWebTemplateOverride`.
- [ ] T7 Give the sample layout a flash — files: `apps/sample/templates/layout/page.html`, the 11 layout call sites in `apps/sample/*.cs` — covers: R9 — done when: the layout declares `@param string FlashOk` / `@param string FlashError` and renders the two banners behind call-site `{{if}}` guards as the skeleton's layout does, every call site supplies both, and each anonymous or output-cached page passes `""`.
- [ ] T8 Make the contact form post-redirect-get — files: `apps/sample/PublicForms.cs` — covers: R9 — done when: a successful contact submission sets a flash and redirects, and the redirected GET renders the success banner.
- [ ] T9 Prove the flash through a real request — files: `tests/nativeweb.sample.tests.integration/` — covers: R9 — done when: an integration test posts the contact form, follows the redirect, and asserts the response body carries the banner markup and that the flash cookie is expired in the same response.
- [ ] T10 Re-run the stamped product end to end — files: none (verification) — covers: R10 — done when: `deno run -A tools/stamp-check.ts` reports every step `ok` with a non-zero test count, and the stamped product's rendered chrome is unchanged.
- [ ] T11 Record it — files: `docs/02-reference/breaking-changes.md`, `libs/nativeweb.web/README.md`, `libs/nativeweb.assets/README.md`, `CHANGELOG.md`, `work/status/current.md`, `docs/specs/029-framework-owned-templates/029-spec.md` — covers: R12 — done when: the breaking change tells a consumer to declare the override or delete the copy, the assets README no longer says each app vendors its own templates, and the spec reads `Status: done` with R2's package-mode caveat stated.

## Analyze

Every requirement maps to at least one task: R1 → T1; R2, R3, R5 → T2; R4 → T3; R6 → T4; R7 → T5;
R8 → T6; R9 → T7, T8, T9; R10 → T10; R12 → T11. Every task traces to a plan section: T1–T3 → §1
and §2, T4 → §3, T5, T6, T9 → §5, T7, T8 → §4, T10 → §5, T11 → §6.

**R11 (no new trim or AOT warning) has no task, deliberately.** `TreatWarningsAsErrors` is
repo-wide, so it is enforced by every build in T2, T4 and T10 rather than by an assertion of its
own. A task asserting it would be a test of MSBuild.

**R2 is partly unprovable on this branch.** Both in-repo consumers use `ProjectReference`, so
`PackageReference` delivery is exercised only by `tools/pack.ts` verifying the package carries
`templates/`, not by a restore from the feed — nothing has been published. T11 states the caveat
in the spec rather than letting `Status: done` imply the untested half.
