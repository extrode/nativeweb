# 001 — NativeWeb as a framework: direction

Created: 2026-08-19
Revised: 2026-08-19 after independent review by Fable and GLM-5.3 (§11)
Status: brainstorm — D1-D5 taken in discussion, then **D1 and D5 amended by review**. Open
questions in §10 are unresolved and several now block work.

Reconciles `docs/admin-cms-handoff.md` (see §9). Inherits the binding stack rules in
`AGENTS.md`; where this doc and `AGENTS.md` disagree, `AGENTS.md` wins.

---

## 0. The gate: who is this framework for?

Both reviewers independently ranked this first, and it was absent from the first draft.

`AGENTS.md` binds JauntyQ to **local checkout only**, permits **self-hosted-SaaS use of the
generated output**, and forbids redistributing JauntyQ source or binaries or shipping this
product's binary to third parties. Every ring-0 deliverable below either contains
JauntyQ-generated code or only compiles against JauntyQ.

That produces two very different projects:

| | **Internal framework** | **Public framework** |
|---|---|---|
| Audience | epass.io, justsmtp.com, ghosthive.com — all self-hosted, all ours | Anyone |
| JauntyQ licence | Satisfied | **Blocked at the root** |
| Published RPS benchmark (T2.1) | Nice to have | Essential |
| `dotnet new` pack on public nuget (T2.2) | Pointless | Essential |
| Package publishing (Q1) | Private feed, fine | Needs a licence renegotiation |

**Everything in tiers 1-3 is worth doing for the internal case.** Roughly half of tier 2 is
worth doing only for the public case. This doc assumes **internal** until decided otherwise,
and D1's trailing "and others" is narrowed to *other products of ours* accordingly.

**Q0 is therefore the first question, and it gates T2.1 and T2.2.**

---

## 1. Premise: what makes a framework fun, and what AOT forbids

The frameworks NativeWeb is measured against sell the same thing — you write intent, the
framework writes the boilerplate. In every case the mechanism is **runtime reflection**, and
NativeAOT forbids it.

| Framework | The loved feature | Mechanism | Available to us |
|---|---|---|---|
| Django | `Post.objects.filter(...)`, auto-admin | Metaclasses, runtime introspection | No |
| Flask | `@app.route` decorators | Runtime registration | Yes — Minimal APIs already |
| FastAPI | Pydantic binding, auto `/docs` | Runtime type-hint introspection | No |
| Rails | `params.require(:post).permit(...)`, scaffold | `method_missing`, runtime codegen | No |
| Laravel | `$user->posts` | `__get` magic | No |

The compile-time equivalent is **stronger for a fixed, known check and weaker for anything
discovered late** — no REPL exploration, no runtime-registered third-party extension. The first
draft claimed "strictly stronger"; that was overstated, and the FastAPI comparison in
particular was unfair, since a 422 surfaces in dev before deploy just as a compile error does.

The honest version of the pitch: **for the checks a web app actually gets wrong in production —
an unindexed query, an unescaped field, a missing tenant predicate — a generator catches them at
build time and a reflective framework catches them in the incident channel.** JauntyQ already
demonstrates this (`JNT8012`, `JNT1006`, `JNT1009`). That is a real and defensible claim, and it
is narrower than the one first written.

The corollary stands: every ergonomic win here is a source generator, because no second
mechanism exists. That bounds the design space and keeps startup cost at zero.

---

## 2. Baseline, measured 2026-08-19

| | Today |
|---|---|
| `apps/web` | 10,458 lines of C#, **0** `.sql` files |
| SQL corpus | `db/migrations` **55**, `db/queries` **190**, consumed via `libs/nativeweb.data`'s glob |
| Four routes (`/auth/register` + `/auth/register/verify`, GET+POST) | `RegisterEndpoints.cs`, **393 lines** |
| Admin | `AdminEndpoints.cs`, **940 lines**, hand-written |
| HTML pages | 13 C# files of raw string literals; `HtmlEncoder.Default.Encode` called **by hand**, per field |
| `nativeweb.render.generator` | Exists; drives **2 email templates**, **0** HTML pages |
| `[AsParameters]`, `IEndpointFilter`, `AddEndpointFilter` | **Zero uses anywhere in the repo** |
| Apps proving the libraries | **One.** DevMarks left for manzil.my at `0760c7c` |
| Published performance numbers | **Zero** |

The last row of the SQL split matters for §4: `db/` sits at the **repo root**, not inside any
project, so schema coupling flows through a path no project-reference check will ever see.

Every POST handler repeats the same preamble by hand: resolve services or 503, check
`HasFormContentType` or 415, validate antiforgery or 400, `ReadFormAsync`, then
`form["firstName"].ToString()` once per field, then hand-rolled validation.

**But the zero-uses row is the important one.** We have never tried the stock platform
mechanisms for this. How much of the measured pain is self-inflicted under-use of ASP.NET
rather than a gap a generator must fill is *unknown*, and T1.2 must answer it before writing a
generator (§7).

Stated plainly: **NativeWeb today is epass.io with some libraries extracted, not a framework.**

---

## 3. Decisions

**D1 — It becomes a framework consumed by epass.io, justsmtp.com and ghosthive.com.**
*Amended:* scope is **our own products** pending Q0. Pragmatically, without a big-bang
extraction — but see §4, where "move nothing" turned out to be unavailable.

**D2 — Tier 1 first.** Scaffolding built on today's boilerplate would bake the boilerplate into
the generator, the one ordering mistake that is expensive to undo. *Amended by review:* a
performance baseline moves ahead of the binder work, because the binder is exactly the layer
that could cost throughput and there is nothing to compare against (§7).

**D3 — Shipped framework artifacts are .NET only.** The scaffolding CLI is `dotnet new`
templates plus, where templates cannot express it, a `dotnet tool` global tool. No Deno, no
Node, no second runtime in anything a consumer installs. Any exception is discussed first.

Two clarifications this does *not* overturn:
- **Repo tooling stays Deno**, per global `AGENTS.md`. `tools/` and `scripts/` are ours and are
  never installed by a consumer. The distinction is *shipped artifact* versus *build-time
  convenience*, not language preference.
- **HTMX, Alpine and `boot.js`** are vendored browser assets; Playwright's Node driver is a
  test-time dependency of Microsoft's package. Neither is new, neither is in scope.

`dotnet new` beats the Deno CLI first proposed: it is the idiom every .NET developer knows, it
needs no install beyond the SDK, and template packs are a supported distribution format.

**D4 — A proof-of-concept consumer, timeboxed to two days**, decided after tier 1. *Amended by
review:* both reviewers argue this is the critical path, not a nice-to-have, because every open
question below is blocked on information only a second consumer produces. See Q5.

**D5 — `[AdminModel]` is rejected** in favour of an orthogonal attribute split (§8). *Amended:*
the split is four axes, not three, and `[ScopedBy]` is **downgraded from decided to proposed**
pending a feasibility answer (§8, Q3).

---

## 4. What NativeWeb *is*: the ring boundary

The most useful finding from surveying `libs/`: **most of what is named `nativeweb.*` today is
not framework material.**

| Ring | Contents | Reusable by justsmtp / ghosthive? |
|---|---|---|
| **0 — Framework core** | `nativeweb.render` + `.render.generator`, `nativeweb.assets`, the generic part of `nativeweb.security` (`BotTraps`, `OpaqueTokens`, `Sha256Hex`), plus tier 1-2 below | Yes. This is the framework |
| **1 — Optional batteries** | `nativeweb.mail`, scaffolding generator, session plumbing, **the OIDC relying-party library (§7 T1.0)** | Yes, opt-in |
| **2 — epass.io product** | `nativeweb.auth` (28 files: OAuth server, WebAuthn, TOTP, HIBP, SSO modes, realms, token issuance), `nativeweb.data` + `db/` (190 epass queries), `apps/web` | **No** |

`libs/nativeweb.auth` is misnamed. It is an identity provider, not an auth framework.
justsmtp.com does not want `SilentConsent.cs`; it wants to *be a client of* epass.io. Shipping
ring 2 as "the framework" is the worst binding decision available and current naming invites it.

### "Move nothing yet" does not survive contact

Both reviewers made the same objection and it is correct: **the rings cut through csproj
boundaries, and a reference-direction check operates on projects.** Concretely:

- `nativeweb.security` is split by *file* between rings 0 and 2 (`BotTraps` vs
  `ContactProtector`, `PairwiseSubject`).
- `Pkce.cs` and `SessionToken.cs` sit in ring-2 `nativeweb.auth`, but any OAuth *client* needs
  them — that is all three consumers.
- The session cookie handler (`SessionAuthenticationHandler.cs`) is in `apps/web`, not a library
  at all.
- `db/` is at the repo root and is invisible to any project-level check.

So **some extraction is unavoidable**, and the honest version of the strategy is *minimal
splits now, everything else deferred*:

1. Split `nativeweb.security` along the ring line. Small, mechanical, unblocks the check.
2. Extract the OAuth-client primitives out of `nativeweb.auth` into the ring-1 relying-party
   library (T1.0), which the first consumer needs on day one regardless.
3. Then add the reference-direction check, and only then is "let the consumer force the rest"
   a rule rather than a hope.

The first draft's rule — "whatever the first consumer pulls is by definition ring 0 or 1" — was
circular, as GLM noted. Usage *discovers candidates*; it must not *define* the boundary, or the
first convenient reach into ring 2 retroactively legalises itself.

Only one tracked source file under `libs/` still mentions epass: a comment at
`nativeweb.auth/LoginLinkSending.cs:22`. The neutral-naming work did the hard part; what remains
is the boundary, not the names.

---

## 5. How downstream repos consume it

**Reuse the JauntyQ dual-mode pattern**, with its known defect stated rather than glossed:

- **Checkout mode** (default): `ProjectReference` to `$(NativeWebRoot)`, guarded by an
  `EnsureNativeWebRoot` sentinel-csproj `Exists` probe. For co-development.
- **Package mode** (`-p:NativeWebUsePackages=true`): pinned `NativeWeb.*` packages.

The sentinel probe is not optional. MSBuild only *warns* (MSB9008) on a missing
`ProjectReference` path, so an unresolved root silently drops projects from a build that still
reports green. `EnsureJauntyQRoot` already works this way and the same trap applies here.

**Correction to the first draft:** it called this pattern "already exercised in CI" and "already
solves exactly this problem". That is false. **CI's package-mode restore has an unresolved
feed-reachability gap** — the package half has never restored end to end, for JauntyQ or for
anything. Copying it means copying a mechanism whose critical mode is known-broken.

Two further consequences the first draft missed:

- **Checkout mode across four repos means lockstep upgrades.** Every consumer builds against
  NativeWeb `HEAD`; there is no pinning. That is tolerable for two repos and unpleasant for four.
- **Package mode needs a compatibility contract** that does not exist: semver guarantees, a
  support window, a deprecation path. Tag-only `dev`→`main` is an *application* release process.

Q1 is therefore larger than "publish or not".

---

## 6. Will not do

| | Why |
|---|---|
| Runtime plugin loading, `Assembly.LoadFile`, `Reflection.Emit`, Roslyn scripting | Hard NativeAOT boundary. Handoff §1 stands |
| Fluent/LINQ query builder over JauntyQ | Rewriting the ORM. SQL-first is JauntyQ's thesis |
| Models → migrations (Django `makemigrations`) | Inverts JauntyQ's schema-as-source-of-truth |
| `select_related` / eager loading / relationship declarations | Wanted, but **JauntyQ's job**. Joint ask, Q3 |
| React, Tailwind, client-rendered SPA | Settled in `AGENTS.md` |
| SQLite for development | `AGENTS.md` binds PostgreSQL only. `admin-cms-handoff.md` §0 and `native-web-architecture.md` §3 are both stale and need correcting |
| A generic reflection-based "admin over anything" fallback | Handoff §6, still right |

Note the fourth row is *not* the same as migration **orchestration** — which migrations have run
against which consumer's database, and upgrade ordering when the framework's expected schema
changes between versions. That is framework-level whoever generates the SQL, and it is currently
in no ring and no tier (Q6).

---

## 7. Will do, in order

Reordered from the first draft on review.

### Tier 1 — credibility.

**T1.0 The OIDC relying-party library.** *New, added by review; both reviewers flagged its
absence independently.* Every named consumer needs "let my users sign in via epass.io" on day
one. Django ships `contrib.auth`, Rails has devise, Laravel has Breeze; NativeWeb currently
ships bot traps. The ingredients exist but are scattered across ring 2 (`Pkce.cs`,
`SessionToken.cs`) and `apps/web` (the session handler). This is the extraction the first
consumer forces immediately, so it is also the natural first ring-1 deliverable (§4).

**T1.1 A clone that builds, and getting-started docs.** *Correction:* the first draft said
"blocked on nothing". It is blocked — a clean clone needs either a JauntyQ checkout or a working
package mode, and package mode does not restore (§5). Q1 gates this.

**T1.2 Kill the endpoint preamble — but measure the stock platform first.** The repo has **zero**
uses of `[AsParameters]`, `IEndpointFilter` or `AddEndpointFilter`. Before writing a generator,
rewrite `RegisterEndpoints.cs` using only stock ASP.NET: `[AsParameters]` binding plus an
endpoint filter carrying antiforgery, 415 and the 503-degrade. Measure the line count. **If that
gets most of the 393→~120 win, the generator's remaining job is validation attributes and
compile-time field discipline, which is a much smaller and better-specified piece of work.**
This is a half-day spike that could save weeks and it is now a precondition, not an option.

**T1.3 Point the render generator at HTML pages, not only email.** The generator exists.
Contextual encoding by construction deletes an XSS class that is currently 13 files of
hand-written `Encode()` calls. Unaffected by T1.2's outcome.

### Tier 2 — evidence and ergonomics.

**T2.1 Benchmark — moved ahead of T1.2's implementation.** Fable's ordering argument: the binder
and filter layers are exactly what could cost throughput, so without a baseline taken *now* there
is no before/after and no regression gate. GLM's counter, accepted: "roughly a day" was wrong for
a *publishable* number with methodology, environment and baselines. Split it — **an internal
baseline is a day and is a prerequisite; a publishable benchmark is a separate, larger piece of
work gated on Q0.**

**T2.2 `dotnet new nativeweb` template pack** plus a `dotnet tool` for what templates cannot
express. *Moved after the second consumer*, on D2's own logic: a template pack encodes the
project layout, and tier 3 reshapes it. Two caveats from review: templates rot without a CI job
that instantiates, builds and tests them; and `rails generate`'s real value is **idempotent
modification of existing projects**, which `dotnet new` does not do at all. That is the hard part
and it was dismissed in a parenthesis.

### Tier 3 — the Django killer feature.

**T3.1 Scaffolding generator** (§8). `AdminEndpoints.cs`'s 940 hand-written lines are the proof
it is needed. Larger than tiers 1 and 2 combined; do not start before T1.2 resolves.

### Tier 4 — speculative.

**T4.1 CMS blocks**, handoff phases 4-7. The block-as-code / page-as-data split is the right
design and stays design-of-record. Building it before a content site exists invents requirements.

---

## 8. The scaffolding attributes: replacing `[AdminModel]`

One attribute cannot answer the questions a real screen asks, and conflating them is why Django
applications outgrow `django.contrib.admin` and hand-write views.

**Four axes, not three.** Review added the fourth and it is the one Rails' strong params exists
for:

| Axis | Question | Django | Rails scaffold | Proposed |
|---|---|---|---|---|
| Generate what | which views exist | admin only | all seven RESTful | `[Scaffold]`, per-operation opt-out |
| Who may | authorization | `is_staff` | not addressed | `[ScaffoldPolicy]` |
| Which rows | row-level scope | not addressed | not addressed | `[ScopedBy]` — **proposed, see below** |
| **Which fields, per operation** | mass assignment | `fields`/`exclude` | `permit` | **unresolved** |

Three corrections forced by review:

**a. Not `[Authorize]`.** Reusing `Microsoft.AspNetCore.Authorization.AuthorizeAttribute` on a
model class is a silent no-op — it is endpoint metadata — so a generator bug degrades to
*unauthenticated scaffolded routes*. Defining our own attribute of the same name creates
ambiguous references in every consumer file. Name it `[ScaffoldPolicy]`, or fold it into
`[Scaffold]`.

**b. Authorization is per-operation, not per-model.** A user lists their own applications but
only an operator deletes any. One policy per model cannot express epass.io's own screens, so the
first real screen hand-writes views again — the exact failure the section set out to avoid.

**c. `[NeverExpose]` fails open and must be inverted.** A deny-list means a newly added
sensitive column is exposed by default, which contradicts the fail-closed philosophy the section
sells for `[ScopedBy]`. It should be an allow-list: nothing renders or binds unless declared.
The write side is worse — nothing in the first draft prevented mass-assigning an `is_operator`
column, and create/edit permit different field sets (a passphrase on create, an immutable owner
on neither).

### `[ScopedBy]`: downgraded to proposed

Both reviewers called compile-time row-scope injection hand-waving as specified, on two
independent grounds, and both are right:

1. **The corpus is 190 hand-written `.sql` files, not auto-CRUD.** Injecting a predicate into
   arbitrary SQL with joins, CTEs and aliases needs a rewriter JauntyQ does not have. A
   generator can verify a query *declares* a scope parameter; it cannot verify the SQL actually
   uses it in a `WHERE`. Presence, not correctness.
2. **Writes are not a predicate.** Scoping an INSERT means *forcing* the owner column to the
   caller, a different mechanism entirely, unmentioned in the first draft.
3. `nameof(OwnerUserId)` is single-column and cannot express membership-via-join, which is the
   real shape in at least two of the three consumers.
4. The generator must read the policy attribute to know whether scope is mandatory, so the axes
   are **not fully orthogonal**.

**And PostgreSQL row-level security was never considered.** On a Postgres-only stack RLS is the
incumbent answer to exactly this problem and is *stronger*, because it holds regardless of query
path — including all 190 hand-written queries a generator cannot touch. The claim "nobody does
this at compile time" was true only with "at compile time" carrying all the weight.

**Q3 is now: RLS, compile-time injection, or RLS enforced with a compile-time check that the
session variable is set?** The third is probably the answer, and it is a genuinely novel
combination, but it is unproven and must not be recorded as decided.

**Also dropped and not noticed:** the handoff's mandatory audit/history-on-every-mutation
(§3.2 there) has no attribute, no axis and no tier here, while §9 claims reconciliation.

---

## 9. Reconciling `docs/admin-cms-handoff.md`

**The architecture is sound; the framing is stale.** Keep verbatim: §1 (no dynamic loading), §2
(the admin/CMS conceptual split), §3.3's `FrozenDictionary` dispatch with no
`Activator.CreateInstance`, §6's non-goals.

| Handoff says | Reality |
|---|---|
| Builds on `ARCHITECTURE.md` | No such file. `docs/native-web-architecture.md` is nearest, itself stale |
| JWT Bearer is the default scheme | We shipped cookie/session auth (`SessionAuthenticationHandler.cs`) |
| SQLite for dev, Postgres for prod | `AGENTS.md` binds Postgres only |
| `[AdminModel]` + `[AdminListDisplay]` … | Superseded by §8 |
| §4 process split (authoring/delivery) | A phase-6 concern; must not shape phase-1 design |
| §3.2 audit/history on every mutation | **Carried forward, currently unplanned here** (§8) |

Its four open items remain genuinely open. Item 1 — how a scaffolded model finds its JauntyQ
query — decides whether any of this is pleasant to use.

---

## 10. Open questions

**Q0 — Internal framework or public one?** Gates T2.1's publishable half and all of T2.2, and
the JauntyQ licence may make "public" impossible without renegotiation (§0). **Answer this
first.**

**Q1 — Consumption and compatibility.** Package mode does not currently restore anywhere (§5).
Publish `NativeWeb.*` to the private feed and fix the feed gap, or require every consumer to
hold a checkout and accept lockstep upgrades? Package mode additionally needs a semver and
deprecation policy that does not exist.

**Q2 — Minimal splits now, or defer?** §4 shows the reference-direction check needs
`nativeweb.security` split and the OAuth-client primitives extracted first. Both reviewers say
enforce now. That makes it roughly a day, not an afternoon.

**Q3 — Row scoping: RLS, codegen, or both?** See §8. Blocks T3.1's design.

**Q4 — Does the framework eventually leave this repo?** Not now. But if rings 0 and 2 are
different products, a shared repo keeps leaking the boundary — and epass.io's gravity is
constant because it is the only in-repo consumer.

**Q5 — Which app is the POC?** Both reviewers argue the POC is the critical path, since every
question above is blocked on information only a second consumer produces. A throwaway in-repo
sample produces almost none of that information. **Recommendation: make justsmtp.com the first
real consumer, immediately after T1.0 and T1.2.**

**Q6 — Migration orchestration across consumers.** Framework-level, currently unowned (§6).

**Q7 — Joint asks on JauntyQ**, as one batch: scope-predicate support (or a ruling that RLS is
the answer), the model-to-query naming convention, and whether eager loading is on the roadmap.

---

## 11. Gaps named by review and not yet placed

Neither reviewer's list is fully absorbed above. These have no tier and no owner:

- **Background jobs.** justsmtp.com will need one. Django/Rails/Laravel all ship or bless a
  queue; NativeAOT constrains the options and nothing here addresses it.
- **Testing DX.** Every consumer currently inherits `WebApplicationFactory` plus a real Postgres
  plus Playwright. That is a heavy default and a real adoption cost.
- **Diagnostics / developer error pages.** A named part of why the compared frameworks are liked.
- **Ecosystem.** Half of Django/Rails/Laravel's value is reusable packages, and NativeAOT forbids
  the runtime loading that ecosystems rely on. The handoff's process-boundary-as-extension-point
  (§4 there) is the only answer available and it is currently demoted to a phase-6 concern.
- **`libs/nativeweb.data/README.md` references a nonexistent `apps/api`.**
- Stale build output from removed projects: `libs/epass.*` and `libs/nativeweb.devmarks.data`.
