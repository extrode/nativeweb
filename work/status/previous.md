# Previous

The task that just finished: merged into `dev`, not necessarily released. One task, a few bullets.
`/status roll` moves `current` here, `next` into `current`, and pulls a fresh task from
`../tasklist.md` into `next`. When a release is tagged, the same work is summarised in
`../../CHANGELOG.md`.

- **V5 (spec 003)** — `feat/bot-traps` (merged c7b7ccd): `BotTraps` in `nativeweb.security`
  (4 honeypots + signed `form_session` canary via `ITimeLimitedDataProtector`, MinAge 3s /
  MaxAge 24h), template partial for devmarks + `Fields(context)` string overload for the IdP,
  wired into all 11 devmarks handlers and all 23 IdP forms with per-endpoint fake successes
  (R23) and structured trap logging; only sign-out, WebAuthn JSON and OAuth machine endpoints
  are untrapped by design. Test tiers post like browsers (FormFlow/TrapForm), MinAgeSeconds=0
  knob for machine-fast submits, R24 computed-visibility e2e in both apps, explicit trap-trip
  tests both sides. Review (fable + opus workers): 1 blocker fixed (profile error re-render
  dropped trap fields — silent data-loss loop), guard placement/timing-parity/throttle-oracle
  findings all fixed; canary replay breadth documented as accepted bypass. All six suites
  green (333+214+47 web, 196+61+10 devmarks), zero-warning AOT publishes both apps.
