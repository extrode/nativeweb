# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

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
- Suite counts, **every tier measured on this branch 2026-08-25 — 1,303 facts, 0 failed,
  0 skipped**: 494 web-unit / 298 web-integration / 64 web-e2e / 157 render / 64 scaffold-unit /
  17 scaffold-integration / 4 scaffold-e2e / 175 signin-unit / 27 signin-integration /
  3 signin-e2e.
  The three sign-in tiers then moved again fixing the review findings and stand at **177 / 32 / 3**
  — so the branch total is **1,308**. The other seven tiers were **not re-run after the fixes and
  did not need to be**: the only projects referencing `nativeweb.signin.epass*` are its own five,
  checked rather than assumed.
  Every new fact across both rounds was perturbed and proved red before being kept — six
  perturbations in the fix round, six failures, one per fix.
  Two counts here were **stale, not changed by 009**: web-integration read 296 and render read
  154. 009 touched only `libs/nativeweb.signin.epass*`, so the drift predates it — most likely
  the scaffold-e2e merge that is this branch's base.
  **The 64 web-e2e had not been run since spec 008 and is green.** It runs fine from a worktree,
  as does scaffold-e2e; the note that used to say otherwise was never tested.
- Spec statuses: 001–007 `done`, 009 `done`. **008 says `draft` and, after the 2026-08-25 review,
  should.** Twelve of its fifteen criteria are met and evidenced; R1, R11 and R12 each describe
  something the shipped code deliberately does differently, so flipping the status is the user's
  call rather than a correction. R-by-R table in `008-spec.md`, "Acceptance review".
- The AOT publish gate for the sign-in library measures exit 0, zero IL2026/IL3050, **16,786,944 B**
  exe (16.6 MB before 009); re-run after the review fixes. Note it only compiles natively when given a RID: `PublishAot=true` with
  no `-r` gives the analyser pass and a managed publish, which is what the tests.aot project does
  by default. 009's gate was verified both ways.
