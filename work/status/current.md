# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Spec 016 — the OAuth relying party** (`feat/016-oauth-relying-party`). Building the client half
  of OAuth as framework: authorization-code with PKCE, a server-side session over a store the
  product implements, silent refresh, and a bearer-introspection filter. Recovering the generic
  three-quarters of `nativeweb.signin.epass` from `f743436^`, dropping the provider-specific
  login-link flow, and replacing the stateless signed ticket with an opaque cookie over
  `ISessionStore` — because the provider rotates refresh tokens and revokes the family on reuse,
  which a stateless design has nowhere to serialize. 27 tasks in `docs/specs/016-oauth-relying-party/016-tasks.md`.

- **Phase 3 of the readiness plan.** T11 (the scaffold defect batch) and T12 (deploy and database
  hygiene) are both done. T12: the deploy chain publishes `apps/sample` by default and names no
  project past `build.sh`; `db/migrate.sh` was retargeted at `db/sample/migrations` rather than
  retired, with the superuser extensions step made optional; and the publish-budget launch now sets
  `Sample__Telemetry__Enabled`, so the OpenTelemetry registration runs inside the AOT image again —
  it had been set under the wrong configuration section since `apps/web` left, and nothing was red.
  Nothing in `scripts/deploy/` has still ever run on a real host: that is T15, below. **D2 is
  answered (2026-08-31): the generated admin stays open by default** — an unguarded model is
  reachable anonymously, and no consumer breaks. Plan 007 is therefore complete: phases 1, 2 and 3
  are all built and merged, and what is left of it is the two decisions, both now taken (D1: ship
  without auth for now; D2: open by default).

- **The seven review findings from the phase-3 branch are fixed** (`fix/021-review-findings`,
  2026-08-31). The two that would have been noticed by an operator rather than a test: the deploy
  preflight demanded ten keys under a prefix no binary binds, nine of which no code reads, so it
  proved nothing while the settings it named stayed unset; and the data protection key ring was
  composed relative to a working directory that is read-only under `ProtectSystem=strict`, which
  cost every form POST a 400 after each deploy. The rest are the scaffold generator emitting
  uncompilable assignments for unrecognised types and for read-only members, `DemoStore` reporting
  404 for a row a concurrent save had replaced, and `skeleton/db/migrate.sh` shipping without the
  run lock and atomic wrap this repo's runner has. Five tests that asserted a substring existed
  somewhere in a file were rewritten to parse the structure they name, each mutation-checked.
  **A second Fable pass over those fixes found six more**, three of which were breaks rather than
  overstatements: the remote deploy path never read the stamp the first fix added, so every
  `--host` and `--incus` deploy would have failed at the migrate step; the migration runner looked
  for `0002_extensions.sql` while the skeleton ships `0001`, so every fresh stamped product would
  have failed its first migration as the app role; and the key-directory guard tested for an empty
  home, which the system account it was written for does not produce. All fixed and
  mutation-checked. **A third pass over those** found five more, including two the second pass
  introduced: the new probe created the directory it tested, which a privileged process does
  happily, and taking the first glob match reinstated the app-role failure for a second extensions
  migration. 648 tests green across the eight suites in `NativeWeb.slnx`. Three review rounds on
  one branch is the record worth keeping: each round's fixes carried defects of their own, and
  only the third came back with nothing that breaks a deploy.

- **The four coverage gaps the 021 audit named are built** (`test/021-coverage-gaps`, 2026-08-31),
  and two of them came back with an answer the audit did not have. The generator's language-version
  floor is **C# 11**, not the C# 10 the audit proposed as the gate: the emitted source needs a raw
  string literal and a static abstract interface member, and the second is not ours to lower, so a
  consumer below 11 could not bind a minimal-API parameter at all. The `[ScaffoldSearch]` gap did
  not exist -- emitting an empty `SearchFields` set already turns two integration tests red, which
  is a fact measured by mutation rather than an entry taken on trust. The other two are new tests:
  the generated update handler's second 404 is now executed through a stub store rather than read
  out of the emitted source, and a refused configuration is proved at the process boundary with
  exit 78 and the key named on stderr. That last one could not be driven through the key-directory
  branch it was written for: `GetFolderPath(UserProfile)` reads the process token on Windows, not
  `USERPROFILE`, so a malformed `BotTraps` value carries the same fact instead. 657 tests green
  across the eight suites. Two things found on the way, both on `work/todo.md`. The first is now
  **fixed** (`fix/static-error-shell-exit`, 2026-08-31): `StaticErrorShell.Load` threw out of
  `Main` instead of taking `FailStartup`'s exit -- the modal-block failure the guard exists to
  prevent -- and the `IWebHostEnvironment` overload now routes a missing or unreadable shell to
  exit 78 with the path on stderr, proved at the process boundary and mutation-checked. 658 green.
  The second is not ours to run: six untracked test directories are `bin`/`obj` residue of the
  departed identity provider, listed in `scripts/cleanup/021-coverage-gaps.ps1`.

- **T15 of spec 011 -- run the deploy scripts against a real host.** Ahead of the cutover, and
  not gated on it: `scripts/deploy/provision.sh` has never been executed, the restore drill has
  never been run, and every elapsed time in `docs/02-reference/deployment.md` reads
  `NOT YET MEASURED`. Until an operator walks a bare Debian host through provision → release →
  rollback → restore and writes the times back, three of spec 011's requirements (R1, R16, R17)
  are unmet and the scripts are untested against anything but `bash -n` and the unit tier.
  R15 is conditional too: `BACKUP_HEARTBEAT_URL` is unset, so a backup that stops running is not
  noticed. This is a person's task, not a session's.

- ~~**The epass.io production cutover.**~~ **Closed 2026-08-31 as a NativeWeb concern** —
  `docs/decisions/2026-08-31-008-epass-cutover-closed.md`. Checked against the real sibling
  checkout that day: epass.io has built and merged its own browser surface, consumes only
  `libs/nativeweb.render` and `libs/nativeweb.security` from here, uses nothing the identity
  provider's departure deleted from either, and its red CI has three causes all in that repo.
  Nothing is owed to it but a push of `dev`, which is the user's call. **The term is retired
  here; do not reopen it from an older document.**
- Behind it, the `work/todo.md` items that need a decision rather than work: the skeleton's
  release process (whether NativeWeb versions and publishes itself), the session key type
  (JauntyQ-wide), and pinning the skeleton's two framework checkouts to tags, which cannot happen
  until either framework repo publishes one. The Node 20 → 24 action bump that used to sit here is
  **done** (2026-08-20) — see `docs/decisions/2026-08-20-001-node-runtime-in-ci.md`.
- `work/milestones.md` was filled on 2026-08-30 with six destinations, so "where this project is
  going" now has a record. Three of the six are `in progress`; T15 above is milestone 3.
