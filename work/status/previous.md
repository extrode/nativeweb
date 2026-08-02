# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

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
