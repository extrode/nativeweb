# Previous

The task completed most recently, kept for one rotation so the next one has context.

- **Overnight backlog** (three branches, merged 2026-08-17). `chore/test-and-skeleton-hygiene`
  removed the two dead OpenApi package pins (approved beforehand: dependency removal) and made
  `check-publish-budgets.ps1` default to the port ci.yml actually passes, guarded by a test that
  reads both files. `feat/render-diagnostics-locations` gave every NWT diagnostic a real file and
  line — they were all `Location.None`, so nothing in a build log was clickable — validated
  `@param` types so a type with a space in it is NWT003 rather than a C# error in generated code,
  and added NWT016, which rejects interpolation inside a tag but outside a quoted attribute value.
  That last one makes `TemplateWriter`'s own safety comment checkable for the first time.
- **IdP correctness backlog** (`fix/idp-correctness-backlog`, merged 2026-08-17). `POST
  /auth/2fa/verify` answered an expired challenge with a bare 302, and that form is htmx-driven,
  so the sign-in page's whole document was swapped into the 2FA card. Now `Redirects.To`, along
  with eight GET-side redirects that are latent only because nothing links to them yet.
  `AppProcessFixture` gained a 15-minute publish budget, event-based draining of both pipes (the
  sequential drain could deadlock the child), the child's last output lines in the exception, and
  `Kill(entireProcessTree: true)`; `ProcessRunTests` covers all three exits. The 400-vs-415
  ordering was examined and deliberately left alone — it is consistent at all 23
  `ValidateRequestAsync` sites, so it is a design decision to make, not a slip to fix; recorded in
  `work/todo.md`.
- **Loose ends** (`fix/loose-ends`, merged 2026-08-17). Closed the last of the three 2026-08-03
  CSP review findings: `IsExecutableScript` now carries the HTML standard's whole JavaScript MIME
  type set, so `text/jscript`, `application/x-javascript`, `text/livescript`, the
  `text/javascript1.x` forms and the `x-` variants are hashed instead of silently blocked on a
  nonce-free page. Pinned by `templates/csp/legacytypes.html` (six executable spellings plus a
  `text/template` control), perturbation-checked. Separately, twelve of the fourteen cleanup
  scripts rejected the `--execute` spelling their headers documented; all now take it, and the
  two that predated the interface and deleted on a bare invocation are dry-run-by-default with a
  second named flag for the deletion.
- Before that: spec 007 (session tokens hashed at rest) and `fix/csp-hash-scan-context` (HTML
  context in the hash scan, plus the two lockstep layers that recompute).
