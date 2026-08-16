# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **CSP inline-script hash scanning** (`fix/csp-hash-scan-context`, merged `--no-ff`;
  `docs/plans/2026-08-16-003-csp-hash-scan-context.md`). `InlineScriptHashes` hashed any static
  `<script` it found regardless of HTML context, so a commented-out script, a `<script>` inside
  `<textarea>`/`<title>`, and one inside another tag's attribute value each added a `script-src`
  entry for a script the page does not ship. `Phase.Outside` now walks tags with comment /
  raw-text / in-tag states carried across node boundaries. The reason it survived: every test
  compared the generated constants against the CSP header built from those same constants.
  Replaced by two real lockstep layers — an AngleSharp theory over every `Csp/*` fixture, and the
  skeleton's app-level test recomputing from the served response (verified on a stamped product,
  and perturbed by injecting a stray hash). Suites 386 / 122 / 256 / 59 green; publish
  zero-warning at 36,932,096 B.

- **Spec 007 — session tokens hashed at rest** (`feat/007-session-token-at-rest`, merged
  `--no-ff`). The `__Host-NativeWeb.Session` cookie value *was* the `bff_store` primary key,
  written verbatim, so any read of that table yielded live bearer credentials for fourteen days.
  Rows are now keyed by `SessionToken.StorageId` — the first 128 bits of SHA-256 over the token,
  kept as a `UUID`, so the column, the index, all eight `db/queries/BffStore/*.sql` and the
  generated data layer are untouched and lookup is still one point lookup. Migration 0054
  converted 106 real rows in `nativeweb_dev` in place; it is one-way, not re-runnable, and writes
  its own `schema_migrations` row inside its transaction because `migrate.sh` applies and records
  separately. Suites 386 / 256 / 59 green, AOT publish zero-warning at 36,932,096 B.
  Review (opus) returned eight findings, all acted on: the endianness/ordering/rollback header,
  the vacuous e2e "current session survives" half (now re-navigates and waits on the profile
  form, perturbation-checked), the SQL parity helper now lifting the expression out of the
  migration file rather than holding a second copy, and R4 downgraded from "met by construction"
  to a speed bump with the real fix — a session key that is not a raw `Guid` — recorded in
  `work/todo.md`.

- **Spec 006 cacheable public pages** (`chore/spec-006-skeleton-parity` + manzil.my
  `feat/cacheable-public-pages`, both merged) — the render generator emits
  `InlineScriptCspHashes` per template (T2), and the skeleton gained
  `PublicPageCache`/`Policy`/`Eviction`, the hash-CSP hook, the layout's nonce and token
  guards, `Chrome.Anonymous` and the output-cache wiring, with its own `/` declared public so
  the mechanism ships exercised (T10). Verified by stamping the skeleton and building it
  zero-warning; 41 mirrored unit tests. Spec `Status: done`, all eleven tasks ticked.
- **Spec 005 B3 + B4 + skeleton flash** -- all merged, spec complete. B3 (manzil
  `feat/ux-defaults`, 2185711): boot.js vendor, `/version` + stamp, Flash tuple API + 15
  adoption sites (bot-traps identical), field errors, native constraints, access keys,
  FormGuard six-copy extraction + 415-order fix; suites 228/92/21 green; review $3.14
  (1 MED settings-trap latency parity, fixed). B3.5 (`feat/skeleton-flash`, c1c960c):
  skeleton flash slot, verified via stamped scratch product. B4 (`docs/pe-doc-fold`,
  99d6cb5): v2 PE doc folded and deleted, spec R1 corrected, conventions cross-linked;
  review found lost browser-native-first content + focus-recovery overstatement, fixed.
