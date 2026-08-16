# Previous

The task completed most recently, kept for one rotation so the next one has context.

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
