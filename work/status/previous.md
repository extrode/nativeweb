# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **V6 (spec 003)** — `feat/security-baseline` (merged c16e828): env-gated HSTS,
  deny-by-default Permissions-Policy (publickey-credentials kept on the spec default for
  WebAuthn), CSP + `base-uri 'none'`/`form-action 'self'` with report-uri/report-to into a
  rate-limited `POST /csp-report` (8 KB raw read, never deserialized, control-chars flattened
  before logging); `__Host-` session cookies both apps with the whole e2e tier moved to https
  (per-run self-minted PFX — Chromium refuses the `__Host-` prefix on http://localhost,
  verified empirically); Kestrel 128 KB body ceiling (+5 MB /import raise), 30 s request
  timeouts; migration 0052 append-only `audit_log` + 8 success-only admin writes;
  RFC 9116 security.txt (placeholder contacts — needs decision); CI CVE gate in package
  mode failing on findings or a vacuous audit; `docs/security-baseline.md`. Review
  (fable review-deep + opus workers): blocker fixed — devmarks was missing
  ForwardedHeaders so all its per-IP rate limits collapsed behind a proxy; IPv6 /64
  bucketing for every rate-limit partition; session-token-at-rest hashing backlogged.
  All six suites green (335+222+49 web, 196+66+12 devmarks).
