# 015 — Tasks

Plan: ./015-plan.md

T1 gates everything after it. Nothing moves until the invariants are asserted and green.

- [ ] **T1** Create `tests/nativeweb.web.tests.unit` and assert the spec-006 triangle against the
  skeleton's middleware as they stand — files: new test project, referencing the skeleton sources
  — covers: R1, R10 — done when: anonymous responses are byte-stable, mint no token and set no
  cookie; an authenticated request is not served a shared entry; locale separates entries; each
  assertion fails when its invariant is broken.
- [ ] **T2** Assert the pipeline order — files: same tier — covers: R5 — done when: the rate
  limiter is asserted to precede the public-page cache and the test fails when they are swapped.
- [ ] **T3** Move the skeleton's five unit test files into the new tier — files:
  `skeleton/tests/__product__.tests.unit/*.cs` → `tests/nativeweb.web.tests.unit/` — covers: R10 —
  done when: all five pass in the new tier and the skeleton keeps only its product-specific tests.
- [ ] **T4** Create `libs/nativeweb.web` and move the ten middleware plus `Chrome`, `AssetUrls`,
  `Flash`, `StartupConfigGuards`, `ReadinessCache`, `CspReportEndpoint` — files: the new library,
  `skeleton/apps/__product__/**` — covers: R2, R6 — done when: the library builds with 0 warnings
  and T1–T3 still pass against it unchanged.
- [ ] **T5** `NativeWebOptions` and its hand-written `FromConfiguration` — files:
  `libs/nativeweb.web/NativeWebOptions.cs` — covers: R4 — done when: every setting the skeleton's
  `Program.cs` reads is bound, with no reflection, and a test covers a missing and a malformed
  value.
- [ ] **T6** `AddNativeWebWeb` and `UseNativeWebPipeline`, every step public — files:
  `libs/nativeweb.web/PipelineExtensions.cs` — covers: R3, R5 — done when: the ordering test from
  T2 passes against the real extension method rather than a stand-in.
- [ ] **T7** The asset `.targets` — files: `libs/nativeweb.web/build/*.targets` — covers: R7 —
  done when: a consumer referencing the library gets the physical files in its output and
  `AssetUrls` fingerprints them.
- [ ] **T8** `apps/sample` adopts the seam — files: `apps/sample/Program.cs`,
  `apps/sample/nativeweb.sample.csproj` — covers: R9 — done when: the sample serves through the
  framework pipeline and its 468-fact suite is still green.
- [ ] **T9** Shrink `skeleton/apps/__product__/Program.cs` to ≤ 60 lines — files: that file —
  covers: R8 — done when: a stamped product builds and its three tiers pass, and a repo-tier test
  asserts the line ceiling.
- [ ] **T10** Measure and record — files: `docs/02-reference/performance.md` — covers: R11 — done
  when: the pipeline's cost on `apps/sample` is measured against Phase 1's close and written down
  with the bottleneck, whatever the number is.

## Analyze

Every requirement maps to at least one task: R1→T1, R2→T4, R3→T6, R4→T5, R5→T2+T6, R6→T4, R7→T7,
R8→T9, R9→T8, R10→T1+T3, R11→T10, R12→the build, which gates all ten. Every task traces to a plan
section. No orphans, no gaps.
