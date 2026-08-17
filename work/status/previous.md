# Previous

The task completed most recently, kept for one rotation so the next one has context.

- **Away backlog slate** (seven branches, merged 2026-08-18). The items the user picked on the
  way out, plus the two they decided rather than deferred. `db/migrate.sh` now takes a
  session-level advisory lock through a psql coproc before it reads anything, so a second runner
  queues and says so instead of racing `is_tracked`; verified by `db/test-run-lock.sh` rather than
  an xUnit fact, because launching the runner from .NET on Windows deadlocks on the redirected
  pipe and the failure would look exactly like the bug. `Redirects.To` answers a POST with 303 and
  a GET with 302 -- Minimal APIs has no 303 helper, so `Location` is set by hand. The 415 sweep
  reached the last six handlers, taking guarded handlers from 17 to 23 and turning six tolerant
  ternaries into plain `ReadFormAsync`. `CompressedAssetTests` covers the one asset path only
  `dotnet publish` exercises. `.editorconfig` gained field/local/parameter naming rules -- and the
  find behind them: IDE1006 was off, so every naming rule in that file, including one written at
  `severity = warning` on day one, had never run. The solution measured at a single violation.
  `docs/migration-lock-review.md` records which migrations would hurt against a live database.

- **Bounded backlog run** (four branches, merged 2026-08-17). Taken simplest-first while the user
  was away, on the scope they set on the way out. `fix/migrate-record-in-transaction` made
  `db/migrate.sh` apply and record in one transaction, asks the server whether the wrap survived
  rather than trusting its log, and lets a concurrent double-apply fail loudly.
  `chore/skeleton-local-mode-ci` rebuilt the skeleton's CI in checkout mode — the package mode it
  forced could never restore, because the private feed does not exist — with the framework checked
  out as a SIBLING of the product: nested, `Directory.Packages.props` discovery walks up out of the
  clone into the product's copy and restore dies NU1008 on both JauntyQ projects, measured. That
  run also closed a vacuous pass of my own: a `(?m)` job-header regex that would not step over a
  `\r`, so the sweep found no jobs and reported green. `fix/415-before-antiforgery` is the
  400-vs-415 decision below, now taken. The JauntyQ generator flake is written up.
- **CI closeout and the first-party application gap** (many branches, merged 2026-08-17). The CI
  chain went green end to end for the first time, including `aot-publish` and all three test
  tiers. Behind it: migration 0055 creates the first-party application row for every deployment,
  which is what `db/seed/ci` had been papering over for CI alone — measured 33/258 integration
  failures with 0055 withheld against 258/258 with it. `RealmResolver.ResolveFirstPartyAsync`
  now returns null rather than throwing, both call sites answer 503, and a three-valued boot
  check refuses to start outside Development only when the database *answers* and the row is
  absent. `db/seed/ci` is retired and deleted; `CiSeedTests` was rewritten to assert the
  inverse, that ci.yml applies no seed at all. Fable's review caught a real defect in
  `DemoteBelowEnvironmentVariables`, which was wrong for more than one added source and had four
  passing tests that were all single-source. The skeleton's `msvc-dev-cmd` is SHA-pinned, guarded
  by `ActionPinTests` over both repositories' workflows. Cleanup ran: 97 merged branches, 29
  scratch files and 8 repro databases removed.
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
- Before that: `fix/loose-ends` (the last 2026-08-03 CSP review finding, plus the cleanup-script
  `--execute` interface), spec 007 (session tokens hashed at rest) and `fix/csp-hash-scan-context`.
