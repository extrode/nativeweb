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

**Q0 is decided: internal.** D1's trailing "and others" means *other products of ours*. The
JauntyQ licence question is **noted and deliberately deferred** — it does not bind an internal,
self-hosted framework, and it is revisited only if the public case is ever reopened.

Consequences, taken now:
- T2.1's *publishable* half is out of scope; the internal performance baseline stays (§7).
- T2.2 targets a **private** template feed, not public nuget.
- Q1 narrows to the private feed and its reachability gap.

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
| Apps proving the libraries | **Two.** *Corrected 2026-08-19:* justsmtp.com is already a consumer — see §13 |
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
| Who may | authorization | `is_staff` | not addressed | `[Auth]`, `[Auth(Role = "admin")]` |
| Which rows | row-level scope | not addressed | not addressed | `[ScopedBy]` — **proposed, see below** |
| **Which fields, per operation** | mass assignment | `fields`/`exclude` | `permit` | **unresolved** |

Three corrections forced by review:

**a. `[Auth]`, not `[Authorize]` and not `[ScaffoldPolicy]`.** Reusing
`Microsoft.AspNetCore.Authorization.AuthorizeAttribute` on a model class is a silent no-op — it
is endpoint metadata — so a generator bug degrades to *unauthenticated scaffolded routes*, and
defining our own attribute of that name creates ambiguous references in every consumer file.
`[ScaffoldPolicy]` avoided the collision by being ugly. **`[Auth]` is the right answer:** it
does not collide, and it reads as intended in the common case.

```csharp
[Auth]                        // any authenticated user
[Auth(Role = "admin")]        // the 90% case, stated the short way
[Auth(Policy = "...")]        // escape hatch to a full ASP.NET policy
[Auth(Delete = "admin")]      // per-operation, per §8b
```

Simple thing simple, hard thing possible. Confirm at implementation time that no
`AuthAttribute` exists in the referenced ASP.NET namespaces — a one-line compile check, not an
assumption.

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

### Q3 resolved: the scope contract is the abstraction, RLS is an optional backstop

Both reviewers proposed PostgreSQL row-level security as the incumbent, stronger answer. It is
stronger — it holds regardless of query path, including the 190 hand-written queries a generator
cannot touch. **But it cannot be the mechanism, because it is not portable, and the layer below
us already is.**

JauntyQ is a four-dialect data layer (`ContractDialect`: `SqlServer`, `Postgres`, `MySql`,
`Sqlite`, with a plan provider for each). Row-security support does not match that set:

| Dialect | Row-level security | Notes |
|---|---|---|
| PostgreSQL | Yes, 9.5+ | Mature. Needs `FORCE ROW LEVEL SECURITY` or the table owner bypasses it |
| SQL Server | Yes, 2016+ | Security policies over inline table-valued predicate functions |
| MySQL / MariaDB | **No** | Only `DEFINER` views, a poor substitute |
| SQLite | **No** | Nothing |

**Two of four.** Making NativeWeb's security model depend on RLS would remove portability that
JauntyQ already provides, which is the opposite of the intent.

**So the framework abstraction is the scope contract, not its enforcement:**

- **Layer 1 — portable and mandatory.** A model declares its scope; the generator emits the
  predicate into reads and *forces* the owner column on writes; omitting it where the policy
  requires it is a compile error. Works on all four dialects. This is what the framework
  guarantees.
- **Layer 2 — optional, deployment-time, Postgres and SQL Server only.** RLS as defence in
  depth, catching what layer 1 cannot: hand-written queries, an operator at a `psql` prompt, a
  future leak. Configured by the consumer, never required by the framework.

Layer 1's known weakness stands: over hand-written SQL a generator can verify a scope parameter
is *declared*, not that it is used in a `WHERE`. That is likely narrower than the reviewers
assumed, because JauntyQ carries a first-class `JauntyQ.SqlParser` project and already scans
parameters (`SqlParameterScanner.cs`) to emit `JNT1006`/`JNT1009`/`JNT8012`. **"Diagnose a
declared scope parameter that never reaches a predicate" is plausibly within reach of machinery
that exists** — but it is JauntyQ's to confirm, not ours to assume (Q7).

RLS caveats to record before anyone enables layer 2: the application role must not own the
tables or it bypasses policy; the session variable must be set with `SET LOCAL` inside a
transaction, because a bare `SET` on a pooled Npgsql connection leaks into the next request; and
policies become predicates that can change query plans, so it is a measurable cost, not a free
one.

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

**Q0 — Internal or public? RESOLVED: internal** (§0). Licence noted and deferred.

**Q1 — Consumption and compatibility.** Package mode does not currently restore anywhere (§5).
Publish `NativeWeb.*` to the private feed and fix the feed gap, or require every consumer to
hold a checkout and accept lockstep upgrades? Package mode additionally needs a semver and
deprecation policy that does not exist.

**Q1b — Does the framework inherit `AGENTS.md`'s PostgreSQL-only binding?** *New.* JauntyQ
supports four dialects (§8); `AGENTS.md` binds this repo to PostgreSQL. That is coherent while
NativeWeb is one product, and incoherent once it is a framework other products consume. Proposed
split: **epass.io stays PostgreSQL-only; ring 0 and ring 1 must not require a feature absent from
any JauntyQ dialect.** Needs confirming, because it changes what "PostgreSQL only" governs.

**Q2 — Minimal splits now, or defer?** §4 shows the reference-direction check needs
`nativeweb.security` split and the OAuth-client primitives extracted first. Both reviewers say
enforce now. That makes it roughly a day, not an afternoon.

**Q3 — Row scoping. RESOLVED: both, layered.** Portable generator enforcement is the framework
guarantee; RLS is an optional deployment-time backstop on the two dialects that have it (§8).
What remains open is the JauntyQ half, folded into Q7.

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

---

## 12. Measured: the T1.2 spike (2026-08-19)

Run on branch `chore/endpoint-ergonomics-spike` (`eedad9e`), **not for merge**. The question was
whether declarative form binding plus endpoint filters removes the hand-written preamble under
NativeAOT. Four variants were built and tested against the real integration suite.

| Variant | Builds AOT-clean | Binds form values | 415 on wrong content type | 400 on missing token |
|---|---|---|---|---|
| `[FromForm] ComplexType` | **No** — RDG003 + IL2026 + IL3050, 6 errors | — | — | — |
| `[AsParameters]` + `[FromForm]` members | Yes, 0 warnings | **No — silently all null** | **No — 200 OK** | **No — 200 OK** |
| `[AsParameters]` + explicit `RequireForm()` filter | Yes | **No — silently all null** | Yes | Yes |
| `[FromForm] string` scalars + `RequireForm()` filter | Yes, 0 warnings | Yes | Yes | **No — 500, not 400** |
| `BindAsync` / `IBindableFromHttpContext<T>` | Yes, 0 warnings | **Yes** | **No — 500** | **No — 200 OK** |
| `BindAsync` + reattached guards (variant 5b) | Yes, 0 warnings | Yes | Yes | Yes |

Variant 5b is variant 5 with the two dropped guards put back by hand, both inside the record's
own declaration: a `HasFormContentType` check in `BindAsync` that throws
`BadHttpRequestException(…, 415)` (which `ErrorPageMiddleware` already surfaces at its own
status), and `.WithMetadata(…)` carrying an `IAntiforgeryMetadata` implementation. **289/290
integration tests pass**; the one difference is that a JSON body with no token now answers 400
rather than 415, because the antiforgery check fires before the content-type check. Both are
correct rejections, so this is an ordering preference, not a hole.

**Findings, in order of importance:**

1. **The declared-model shape is available — but only via `BindAsync`, never via an attribute.**
   Two of the three attribute routes are closed. Complex-type `[FromForm]` is not
   NativeAOT-compatible at all: the Request Delegate Generator cannot statically resolve it
   (`RDG003`), falls back to reflection, and trips `IL2026`/`IL3050`, so with repo-wide
   `TreatWarningsAsErrors` the build fails. This is upstream-confirmed and deliberate — complex
   form binding is shared with Blazor and needs dynamic codegen, so RDG emits a diagnostic rather
   than generate wrong code ([dotnet/aspnetcore#61382](https://github.com/dotnet/aspnetcore/issues/61382)).
   RDG accepts a form parameter only if it is `StringValues`, `string`, something with `TryParse`,
   or an array of those. **The third route works:** a type implementing
   `IBindableFromHttpContext<TSelf>` (or carrying a conventional static `BindAsync`) binds through
   a compile-time-resolved static call, with no reflection and no RDG diagnostic — 0 warnings,
   correct values. **So the FastAPI-style "bind the request to a declared model" API is reachable
   under NativeAOT**, which the first draft of §7 assumed without evidence and the first draft of
   this section wrongly declared impossible.
2. **`[AsParameters]` ignores form sources silently.** It compiles clean and binds every field
   to `null`, so validation reports "all fields are required" for a perfectly good submission.
   Nothing warns. This is a worse failure mode than the boilerplate it would replace.
3. **Form binding does not attach antiforgery metadata**, so `UseAntiforgery()` — already wired
   at `Program.cs:679` — never validated the endpoint. A form POST with **no token returned 200
   OK**. Two of the three `ContentTypeGuardTests` for `/auth/register` caught this, which is
   exactly why they exist.
4. **Anything that binds a form runs upstream of every endpoint filter.** This is the structural
   fact behind findings 2, 3 and the 400/500 bug, and it applies to `[FromForm]`, `[AsParameters]`
   and `BindAsync` alike: argument binding happens before the filter pipeline, so a guard written
   as an `AddEndpointFilter` cannot protect the bind. **Request guards belong in middleware or in
   the binder itself; only post-bind concerns belong in a filter.**

   Concretely: `UseAntiforgery()` does not reject a bad token — it records the outcome on
   `IAntiforgeryValidationFeature` and continues, expecting whatever reads the form to check that
   feature. The RDG binder does not, so `FormFeature` throws a bare `InvalidOperationException`
   and the request escaped as a 500. **Fixed 2026-08-19** (`fix/antiforgery-status-code`) by
   `AntiforgeryValidationMiddleware`, placed immediately after `UseAntiforgery()`; 290/290
   integration and 489/489 unit tests pass. Catching `InvalidOperationException` in
   `ErrorPageMiddleware` was the other option and was rejected — genuine server bugs throw it too,
   and mapping it to a client error would hide them.

   Still open: `RequireServices<T>()`'s 503-degrade has the same ordering flaw and has not been
   moved (recorded in `work/todo.md`).
5. **The line-count win is small.** `RegisterEndpoints.cs` 393 → 378, plus a 59-line reusable
   filter file. The first draft's "393 → roughly 120" was wrong by a wide margin: reading the
   whole file shows roughly 250 of its 393 lines are enumeration defence, bot-trap timing
   equalisation, stale-registration reclaim and code-attempt handling. **That is domain logic no
   generator removes.** GLM's scepticism about the estimate was correct.

**What the spike changes:**

- **T1.2's generator now has a concrete target: emit variant 5b.** From a declared form record it
  emits the `BindAsync` body, the content-type guard inside it, and the antiforgery metadata on
  the route. Every one of those is a line a human currently has to remember, and the two the
  spike measured being forgotten — the 415 and the token check — fail *silently* when omitted.
  **This is the framework's whole argument in one endpoint:** the declaration is the contract, an
  undeclared field is unreachable (Rails' strong parameters, enforced by the compiler), and the
  guards cannot be left off because they are not written by hand at all.
- **A caveat the generator must carry:** the guards are only free because the generator emits
  them. Hand-written `BindAsync` is a foot-gun — variant 5 compiles clean, binds correctly, and
  answers **200 OK to a POST with no antiforgery token**. Anything the framework lets a consumer
  hand-write here needs an analyzer that fails the build when the metadata is missing.
- **`RequireServices<T1,T2,T3>()` and `RequireForm()` are worth keeping** as the interim shape,
  with the ordering caveat in finding 4: `RequireForm()` is a genuine 415 guard on a route whose
  binder does not check, and `RequireServices<T>()` needs moving upstream before it can be
  trusted for the same reason antiforgery was.
- **This is the first hard evidence for the whole thesis.** Every ergonomic gain here is a
  source generator not because we prefer generators, but because the platform's reflective path
  is closed by AOT. The spike is what turned that from an argument into a measurement.

---

## 13. Assessed: justsmtp.com as the first consumer

**It is already a NativeWeb consumer.** §2's "one app proving the libraries" was wrong.

| | justsmtp.com, measured 2026-08-19 |
|---|---|
| Size | 248 tracked `.cs` files, **52,664 lines**; 4 apps, 5 libs, 7 test projects |
| Branch | `dev`, head `eb8bf68` |
| NativeWeb wiring | `NativeWebRoot` dual-mode resolution **already in `Directory.Build.props:51-52`**, with an `EnsureNativeWebRoot` sentinel guard and `NativeWebUsePackages` branches already written |
| References | `nativeweb.security`, `nativeweb.auth`, `nativeweb.render`, `nativeweb.render.generator` |
| Symbols actually used from `nativeweb.auth` | `Pkce` (3), `Masking` (2), `Passphrases` (1), `OpaqueTokens` (1) |
| Hand-written OIDC relying-party code | **1,169 lines** in `apps/web/Auth/`: `EpassClient.cs` 316, `AuthEndpoints.cs` 396, `SessionCookie.cs` 149, `SessionAuth.cs` 113, `LoginFlowStore.cs` 110, `SessionAuthenticationHandler.cs` 85 |

**Three things this settles:**

1. **The ring model is confirmed by real usage, not argument.** justsmtp pulls exactly four
   symbols out of the 28-file `nativeweb.auth` IdP library, and three of them (`Pkce`,
   `OpaqueTokens`, `Masking`) are OAuth-client primitives. The reviewers predicted this
   precisely; §4's proposed extraction now has measured justification.
2. **T1.0 is not a green-field build, it is an extraction with a working reference
   implementation.** 1,169 lines of relying-party code already exist, already work, and already
   have tests. The library is that code generalised, not invented.
3. **D4's "two-day POC" was the wrong frame.** The first consumer is not a toy to be written; it
   is a 52k-line product already in the position the POC was meant to create. **Recommendation:
   drop the throwaway sample entirely (Q5 resolved), and treat justsmtp.com's existing
   integration as the source of requirements.**

Also noted: justsmtp's own `Directory.Build.props:43` records "NativeWeb is NOT on the JauntyQ
path yet — it publishes no packages", so its package-mode branches are written but dead. That is
Q1, stated from the consumer's side.

## 14. Measured: emitted endpoints and the RDG (2026-08-19)

The T1.3 scaffolding generator turned up a limitation §12's hand-written spike could not: **the
Request Delegate Generator does not run on another generator's output.**

Roslyn does not feed one incremental generator's `AddSource` into another's input, so the RDG only
rewrites `Map*` calls in the compilation's own syntax trees. Every `MapGet`/`MapPost` emitted by
`ScaffoldGenerator` therefore bound `Map*(IEndpointRouteBuilder, string, Delegate)` — the
reflection overload, annotated `RequiresUnreferencedCode` and `RequiresDynamicCode`. The first
NativeAOT publish of `apps/sample` failed with 22 IL2026/IL3050 errors, all of them on generated
lines, none of them fixable in the model or in the consumer's code.

**The fix, and the rule it implies.** Emit `RequestDelegate`s. The
`Map*(IEndpointRouteBuilder, string, RequestDelegate)` overloads carry neither attribute, because
there is nothing to bind — the delegate takes `HttpContext` and reads what it needs, which a
generator doing its own binding was already doing. Route values come from
`HttpContext.Request.RouteValues`; the form comes from the generated record's own `ReadAsync`.
Verified clean: `dotnet publish -c Release -r win-x64`, 17 MB binary, zero warnings.

**This constrains T1.2.** §12 concluded that T1.2's generator should emit variant 5b. It still
should — but it must emit it *as a `RequestDelegate`*, not as a route handler, or it reintroduces
the whole class of trim warning the framework exists to avoid. A hand-written variant 5b endpoint
is AOT-clean because the RDG sees it; the same code emitted is not. That asymmetry is invisible in
a `dotnet build` and only appears on publish, which is why it survived §12's spike.
