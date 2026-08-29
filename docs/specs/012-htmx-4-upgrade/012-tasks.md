# 012 — Tasks
Plan: ./012-plan.md

**T1 and T2 land in one commit.** The plan's risk register requires `noSwap` to be set in the
same change as the library swap: between them, every guard rejection body swaps into the page.

- [x] **T1 Vendor htmx 4.0.0** — files: `apps/web/wwwroot/vendor/htmx/{htmx.min.js,LICENSE}`,
  `skeleton/apps/__product__/wwwroot/vendor/htmx/{htmx.min.js,LICENSE}` — covers: R1, R10, R11 —
  done when: both `htmx.min.js` report `version = '4.0.0'`, the two copies are byte-identical
  (md5 match), the 2.0.10 build is retained beside them under a version-bearing name in both
  trees, and `LICENSE` matches the 4.0.0 distribution.

- [x] **T2 Layout config and glue** — files: `apps/web/Render/Layout.cs`,
  `skeleton/apps/__product__/templates/layout/page.html` — covers: R5, R6, R7, R9 — done when:
  the listener is `htmx:config:request` writing to `event.detail.ctx.request.headers`; the
  `htmx-config` meta uses `includeIndicatorCSS: false` and sets
  `noSwap: [204, 304, "4xx", "5xx"]`; no `implicitInheritance` key is present; and a served page
  shows the indicator stylesheet still not injected.

- [x] **T3 Translate the enhancement layer** — files: `libs/nativeweb.assets/boot.js` (mirrored to
  `apps/web/wwwroot/vendor/nativeweb/boot.js` and
  `skeleton/apps/__product__/wwwroot/vendor/nativeweb/boot.js`) — covers: R2, R3, R4 — done when:
  all six listeners use the 4.0 names; every payload read goes through `detail.ctx`
  (`sourceElement`, `target`, `request.submitter`); `after:swap` re-derives the swapped content
  from `ctx` rather than `event.target`; the three copies are byte-identical; and the file's
  comments describe 4.0 behaviour, not 2.0.10's.

- [x] **T4 Re-verify the two inherited 2.0.10 assumptions** — files:
  `libs/nativeweb.assets/boot.js` (+ mirrors), `tests/nativeweb.web.tests.e2e/` — covers: R2 —
  done when: (a) boot's `DOMContentLoaded` listener is proven to still run before htmx's initial
  scan under 4.0, and (b) the microtask re-`process()` is either kept with a test that fails
  without it, or removed with a test that passes without it. Reading the source is not sufficient
  for either; both need a browser assertion.

- [x] **T5 Inheritance audit** — files: `apps/web/Render/*.cs` (read), any page found to rely on a
  parent — covers: R6 — done when: every rendered page has been checked for an attribute whose
  effect depends on a descendant inheriting it; each one found carries `:inherited`; the expected
  result of zero changes is recorded as a finding, not assumed; `OAuthAuthorizePage`'s shared
  `#oauth-consent` target is named explicitly in that record.

- [x] **T6 Timeout audit** — files: `apps/web/Auth/*.cs` (read), `apps/web/Render/Layout.cs` if an
  allowance is needed — covers: R9 — done when: every browser-reachable endpoint is confirmed to
  complete inside 60,000 ms, or the ones that cannot are listed and given an explicit allowance.

- [ ] **T7 Adopt morph on eligible targets** — files: `apps/web/Render/AdminPage.cs`,
  `apps/web/Render/SettingsPage.cs` — covers: R8 — done when: eligible non-credential regions use
  `data-nw-swap="innerMorph"`; every auth and consent page named in the plan's exclusion list is
  confirmed unchanged; and no global morph config is set.

- [x] **T8 E2E assertions for the new behaviours** — files:
  `tests/nativeweb.web.tests.e2e/ProgressiveEnhancementTests.cs` — covers: R7, R8, R14 — done
  when: a guard rejection (415 or 400) is asserted not to swap into the page; a failed submit on a
  credential form is asserted to leave the credential field empty; a morphed target is asserted to
  preserve scroll or uncommitted input; and each new assertion has been mutation-checked.

- [ ] **T9 Full suite** — files: none (test run) — covers: R14 — done when: unit, integration and
  e2e all pass with zero failures, and the counts are recorded against the pre-upgrade baseline of
  611 / 307 / 67.

- [ ] **T10 Evaluate `hx-alpine-compat`** — files: `docs/specs/012-htmx-4-upgrade/012-plan.md` or
  a decision record — covers: R15 — done when: the extension is either vendored with a stated,
  demonstrated conflict it resolves, or declined in writing. Default is declined; adoption
  requires evidence, not caution.

- [ ] **T11 Re-run the benchmark** — files: benchmark output recorded in
  `docs/01-architecture/native-web-architecture.md` §14 — covers: R16 — done when: throughput and
  RSS under 4.0 are recorded beside the 2.x figures, and any regression is stated. If §14's
  harness is not runnable as it stands, say so and record what was measured instead — do not
  report a figure that was not measured.

- [ ] **T12 Documentation sweep** — files: `docs/01-architecture/progressive-enhancement.md`,
  `docs/01-architecture/native-web-architecture.md` (§15), `docs/02-reference/security-baseline.md`,
  `docs/conventions.md`, `docs/00-quick-start/tutorial-first-feature.md`, `README.md`,
  `AGENTS.md`, `skeleton/AGENTS.md` — covers: R12, R13 — done when: no document names an event,
  attribute or config key that 4.0 does not have; §15's checklist is ticked item by item or
  declined with a reason, including the corrections that `noSwap` takes an array and that the
  beta-era `htmax.min.js` filename warning is void; the `hx-disable` → `hx-ignore` meaning-flip is
  recorded for skeleton consumers; and the removal of localStorage history caching is noted in the
  security baseline.

- [ ] **T13 Layout and hygiene check** — files: none (audit) — covers: R17 — done when:
  `deno run -A ~/.claude/tools/structure-audit.js` reports no errors beyond the two known exempt
  naming warnings, and the repo contains no package manifest, lockfile or package-manager
  invocation it did not contain before.

- [ ] **T14 Records** — files: `work/status/{current,previous,next}.md`, `work/tasklist.md`,
  `CHANGELOG.md`, `docs/specs/012-htmx-4-upgrade/012-spec.md` — covers: R12 — done when: the
  status rotation reflects 012, `CHANGELOG.md` has an entry under `## [Unreleased]`, and the spec
  is `Status: done` only if every acceptance criterion is met — otherwise the unmet ones are named.
