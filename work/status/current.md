# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Nothing is in flight.** Spec 012 -- the htmx 2.0.10 → 4.0.0 upgrade -- merged into `dev` on
  2026-08-29, unpushed; its summary is in `previous.md`. All fourteen tasks are ticked, but
  **`012-spec.md` is not `done`**: R16 asked for the §14 benchmark re-run and no harness in this
  repo reproduces it, so the requirement is recorded unmet rather than answered.
  - Post-012 web suite: **611 unit / 307 integration / 71 e2e, 0 failed**, build 0 warnings
    0 errors. The +4 e2e are 012's own; the other eight tiers were untouched and their counts
    below stand.

- **Spec 011 still has not reached `done`, and that is unchanged by 012.** T15 -- the provisioning
  walkthrough, the restore drill and the disaster-recovery run -- is executed by a person on a real
  host, and `011-spec.md` stays `draft` until its measured times are in
  `docs/02-reference/deployment.md`. That remains the first thing standing between this repo and a
  production host, ahead of the cutover decision in `next.md`.

- Spec 010 — documentation and tutorials — merged into `dev` on 2026-08-25
  and rolled to `previous.md`; all ten of its tasks are ticked and `010-spec.md` is `done`. Every
  numbered spec in `docs/specs/` is now `done` except 008, which stays `draft` for the reason
  below.
  - **What comes next is a decision, not a task.** `next.md` holds the epass.io production
    cutover, and it is gated on the whole-versus-partial scope question in
    `docs/architecture/002-epass-cutover-capability-gap.md`. Nothing under it can be scheduled
    until that is answered.
  - **Four unscheduled merges landed after 010 and are on `dev` at `1004220`**, none of them a
    tasklist entry, all of them pushed to `origin/dev` on 2026-08-27: `chore/move-to-extrode`
    (cleanup script for the source-root rename), `fix/jauntyq-root-name-agnostic`,
    `feat/docs-site` (the `docs/` restructure below), and `fix/skeleton-probe-case`. `dev` and
    `origin/dev` are level; there is no `main` branch yet, local or remote, because nothing has
    been released.

- **Spec 009 — three consumer seams for the sign-in library.**
  `feat/009-signin-consumer-seams`, merged to `dev` 2026-08-25, unpushed. The three gaps
  justsmtp.com found while migrating onto the library under 008 R12: no way to end a session
  mid-request, no way for a test to mint a ticket, no seam for a consumer's own
  address-plausibility check. All three closed, all three optional.
  - **R9 is not this repo's work and is deliberately unmet here.** justsmtp adopting the seams
    and deleting its three workarounds is a separate branch in a separate repository, sequenced
    after this merge. Until that lands, the seams are compiled rather than used.
  - **The justsmtp migration DID land** (2026-08-19), which retires the note that used to sit
    here. Spec 008's `Status: draft` is now simply stale — logged on `work/todo.md` rather than
    flipped, because confirming its fifteen acceptance criteria is a review of 008, not a line of
    009's work.
  - **A worktree cannot build `NativeWeb.slnx`.** Found on this branch. The
    `Directory.Build.props` half was fixed here (`JauntyQRoot` now probes two depths); the
    solution half needs a decision and is on `work/todo.md`. Per-project builds work with no flag.
- Scaffolding POC (brainstorm 001 T1.3) is merged; follow-ups on `work/todo.md`, none blocking.
  - **The two-lens review found eleven real defects in 009's own work**, all fixed on the branch
    before it merged, one dropped as refuted. Four were behavioural — a policy timeout escaping as
    a 500, `SignOutAsync` leaving `Context.User` authenticated, a second `OnAddressSubmitted`
    silently disabling the first, and `no-store` set by one sign-out path but not the other — and
    four were **prose on this branch asserting something the code did not do**, chiefly the
    testing package being called undeployable while `DevelopmentDependency` was missing. The
    R-by-R table in `009-spec.md` now reads R6 as met for the failure code and NOT for the
    response shape; that half is documentary and pinned by a test. Detail: `009-spec.md`,
    "What review found".
- Suite counts, **all eleven tiers re-measured on `dev` at `1004220` on 2026-08-27 — 1,373 facts,
  0 failed, 0 skipped**: 509 web-unit / 298 web-integration / 64 web-e2e / 157 render /
  16 sample-unit / 64 scaffold-unit / 31 scaffold-integration / 13 scaffold-e2e / 184 signin-unit /
  34 signin-integration / 3 signin-e2e. Solution build is 0 warnings, 0 errors.
  **A twelfth project, `nativeweb.signin.epass.tests.aot`, holds no tests and `dotnet test` skips
  it silently** — `IsTestProject=false`, by design. Its assertion is
  `dotnet publish tests/nativeweb.signin.epass.tests.aot -c Release`, which exits 0 with no IL2026
  and no IL3050. Run it separately or it is not run at all.
  This supersedes the 1,308 measured on `feat/009-signin-consumer-seams` on 2026-08-25. The +65 is
  work merged after that branch, **not drift**: scaffold-integration 17→31 and scaffold-e2e 4→13
  from the scaffolding follow-ups, web-unit 494→509 and signin-unit 177→184 from the JauntyQ-root
  and skeleton-probe fixes, and 16 sample-unit that the 2026-08-25 breakdown omitted entirely.
  **These counts were measured, not perturbed** — the tiers were re-run as a state check, and no
  new fact was written on this pass.
- Spec statuses: 001–007 `done`, 009 `done`. **008 says `draft` and, after the 2026-08-25 review,
  should.** Twelve of its fifteen criteria are met and evidenced; R1, R11 and R12 each describe
  something the shipped code deliberately does differently, so flipping the status is the user's
  call rather than a correction. R-by-R table in `008-spec.md`, "Acceptance review".
- The AOT publish gate for the sign-in library measures exit 0, zero IL2026/IL3050, **16,786,944 B**
  exe (16.6 MB before 009); re-run after the review fixes. Note it only compiles natively when given a RID: `PublishAot=true` with
  no `-r` gives the analyser pass and a managed publish, which is what the tests.aot project does
  by default. 009's gate was verified both ways.
