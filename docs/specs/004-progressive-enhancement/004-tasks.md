# 004 — Tasks

Task breakdown for `004-spec.md` per `004-plan.md`. One branch per row, each off `dev`,
code + tests together, review before merge, merged `--no-ff`.

- [x] **B1** NativeWeb `feat/render-formatting` — pretty-printer pass in
  `nativeweb.render.generator` (post-inlining re-indent of static chunks, directive blank-line
  removal, pre/textarea/script/style skip, per-depth partial variants, conservative bail-out);
  render unit-test goldens + web integration fallout updated. Carries the 004 spec/plan/tasks
  artifacts in its first commit. (R9–R12)
- [ ] **B2** manzil.my `chore/formatting-fallout` — manzil test assertions updated to the
  formatted output; all three manzil suites green. (R12)
- [x] **B3** NativeWeb `feat/boot-js` — canonical `libs/nativeweb.assets/boot.js`; vendored
  copies + parity tests (apps/web, skeleton); `docs/progressive-enhancement.md` rewritten to the
  decided model and cross-linked from `docs/conventions.md`; boot script tag before htmx in the
  IdP chrome and skeleton layout. (R1–R4, R8)
- [ ] **B4** NativeWeb `feat/nw-retrofit-web` — IdP inline renders retrofitted to `data-nw-*`
  (~20 sites incl. `AdminPage.cs` hx-confirm); every form verified to carry `method`+`action`;
  IdP e2e gains JS-off flows + normalization assertions, including the B3-review case of a
  swapped-in fragment containing a descendant `<a data-nw-target>` (must end up htmx-wired via
  boot.js's queued `htmx.process`, not fall back to full navigation). (R5–R7)
- [ ] **B5** manzil.my `feat/nw-retrofit` — boot.js vendored + layout script tag; 3 forms
  retrofitted; `NoJsFlowTests` extended to all retrofitted flows; normalization fixture e2e
  incl. the swap path; parity test. (R5–R7, R13)
- [ ] **Close-out** — all suites green in both repos; zero-warning AOT publishes within CI
  budgets (record size delta); view-source screenshots of formatted output against published
  binaries; `work/todo.md` formatted-HTML entry removed; CHANGELOG under [Unreleased]; tasklist
  archived.
