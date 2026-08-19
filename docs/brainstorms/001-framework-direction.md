# 001 — NativeWeb as a framework: direction

Created: 2026-08-19
Status: brainstorm — decisions D1-D5 taken in discussion, open questions in §10 unresolved

Supersedes nothing. Reconciles `docs/admin-cms-handoff.md` (see §9). Inherits the binding
stack rules in `AGENTS.md`; where this doc and `AGENTS.md` disagree, `AGENTS.md` wins.

---

## 1. Premise: what makes a framework fun, and what AOT forbids

The frameworks NativeWeb is measured against all sell the same thing — you write intent, the
framework writes the boilerplate. In every case the mechanism is **runtime reflection**, and
NativeAOT forbids all of it.

| Framework | The loved feature | Mechanism | Available to us |
|---|---|---|---|
| Django | `Post.objects.filter(...)`, auto-admin | Metaclasses, runtime introspection | No |
| Flask | `@app.route` decorators | Runtime registration | Yes — Minimal APIs already |
| FastAPI | Pydantic binding, auto `/docs` | Runtime type-hint introspection | No |
| Rails | `params.require(:post).permit(...)`, scaffold | `method_missing`, runtime codegen | No |
| Laravel | `$user->posts` | `__get` magic | No |

This is not the constraint it looks like. **The compile-time equivalent is strictly stronger,
and it is already the house style.** Django reports a missing index in production; JauntyQ
reports it at build time (`JNT8012`, `JNT1006`, `JNT1009`). Pydantic raises a 422 on request
number one; a source-generated binder raises a compile error before deploy. FastAPI's own
headline claim is that type safety catches bugs — it catches them one request too late.

**The pitch, and it is a real one:** *the ergonomics of Rails with the failure mode of a
compiler.* Not "Django but faster".

The corollary is that every ergonomic win in NativeWeb is a source generator. There is no
second mechanism available, and that is a feature: it bounds the design space and it keeps
startup cost at zero.

---

## 2. Baseline, measured 2026-08-19

| | Today |
|---|---|
| `apps/web` | 10,458 lines of C#, 247 `.sql` files |
| Four routes (`/auth/register` + `/auth/register/verify`, GET+POST) | `RegisterEndpoints.cs`, **393 lines** |
| Admin | `AdminEndpoints.cs`, **940 lines**, hand-written |
| HTML pages | 13 C# files of raw string literals; `HtmlEncoder.Default.Encode` called **by hand**, per field |
| `nativeweb.render.generator` | Exists; drives **2 email templates**, **0** HTML pages |
| Apps proving the libraries | **One.** DevMarks left for manzil.my at `0760c7c` |
| Published performance numbers | **Zero** |

Every POST handler repeats the same preamble by hand: resolve services or 503, check
`HasFormContentType` or 415, validate antiforgery or 400, `ReadFormAsync`, then
`form["firstName"].ToString()` once per field, then hand-rolled validation. That is *more*
ceremony than Flask, and it is the clearest single thing to fix.

Stated plainly: **NativeWeb today is epass.io with some libraries extracted, not a framework.**
Nothing has proven the libraries work for a second app since DevMarks left.

---

## 3. Decisions taken

**D1 — It becomes a framework, consumed by epass.io, justsmtp.com, ghosthive.com and others.**
Pragmatically, without a big-bang extraction. See §4 and §5 for how that avoids being bound by
a rushed guess.

**D2 — Tier 1 first** (§7). Scaffolding built on top of today's boilerplate would bake the
boilerplate into the generator, which is the one ordering mistake that is expensive to undo.

**D3 — Shipped framework artifacts are .NET only.** The scaffolding CLI is `dotnet new`
templates plus, where templates cannot express it, a `dotnet tool` global tool. No Deno, no
Node, no second runtime in anything a consumer installs. Any exception is discussed first.

Two clarifications this does *not* overturn:
- **Repo tooling stays Deno**, per the global `AGENTS.md` rule. `tools/` and `scripts/` are
  ours and are never installed by a consumer. The distinction is *shipped artifact* versus
  *build-time convenience*, not language preference.
- **HTMX, Alpine and `boot.js`** are vendored browser assets and unaffected; Playwright's Node
  driver is a test-time dependency of Microsoft's package, not ours. Neither is new, neither is
  in scope here.

`dotnet new` is the better answer than the Deno CLI originally proposed: it is the idiom every
.NET developer already knows, it needs no install beyond the SDK, and template packs are a
supported distribution format on nuget.

**D4 — A proof-of-concept sample site, timeboxed to two days**, decided *after* tier 1 lands so
it exercises the new ergonomics rather than the old boilerplate. If scoping shows it exceeds
two days, it does not happen yet and we say so rather than letting it slip.

**D5 — `[AdminModel]` is rejected** in favour of an orthogonal three-attribute split. See §8.
The handoff doc's single attribute conflates "generate CRUD for this", "only admins may reach
it" and "show every row", and those are three different questions with three different answers
in every app we intend to build.

---

## 4. What NativeWeb *is*: the ring boundary

The most useful finding from surveying `libs/`: **most of what is named `nativeweb.*` today is
not framework material.**

| Ring | Contents | Reusable by justsmtp / ghosthive? |
|---|---|---|
| **0 — Framework core** | `nativeweb.render` + `.render.generator`, `nativeweb.assets`, the generic half of `nativeweb.security` (`BotTraps`, `OpaqueTokens`, `Sha256Hex`), **plus everything in tier 1-2 below** | Yes. This is the framework |
| **1 — Optional batteries** | `nativeweb.mail` (SMTP send + templating), scaffolding generator, session-cookie plumbing | Yes, opt-in |
| **2 — epass.io product** | `nativeweb.auth` (28 files: OAuth, PKCE, WebAuthn, TOTP, HIBP, SSO modes, realms, token issuance), `nativeweb.data` (247 epass `.sql` queries), `apps/web` | **No** |

`libs/nativeweb.auth` is misnamed. It is an identity provider, not an auth framework.
justsmtp.com does not want `SilentConsent.cs` or `Pkce.cs`; it wants to *be a client of*
epass.io. Shipping ring 2 as "the framework" would be the single worst binding decision
available, and it is the one the current naming quietly invites.

**Pragmatic consequence — move nothing yet.** Extraction today costs real time and buys zero
information, because we do not yet know what the second consumer actually reaches for. Instead:

1. Record the ring boundary here as the rule.
2. Enforce it mechanically with a reference-direction check: ring 0 must not reference ring 1
   or 2; ring 1 must not reference ring 2. That is a build-time assertion, cheap to write, and
   it stops the boundary eroding while we are not looking.
3. Let the first real downstream consumer force the extraction. Whatever it pulls is, by
   definition, ring 0 or 1. Whatever it never pulls was never framework.

Only one tracked source file under `libs/` still mentions epass at all: a comment in
`nativeweb.auth/LoginLinkSending.cs:22`. The neutral-naming work already did the hard part; what
remains is the boundary, not the names.

---

## 5. How downstream repos consume it

**Reuse the JauntyQ dual-mode pattern verbatim.** It already exists in this repo, it is already
exercised in CI, and it already solves exactly this problem:

- **Checkout mode** (default): `ProjectReference` to `$(NativeWebRoot)`, guarded by an
  `EnsureNativeWebRoot` sentinel-csproj `Exists` probe. For co-development, where a change to
  the framework and a change to the app land in one session.
- **Package mode** (`-p:NativeWebUsePackages=true`): pinned `NativeWeb.*` packages.

The sentinel-probe detail is not optional. MSBuild only *warns* (MSB9008) on a missing
`ProjectReference` path, so an unresolved root silently drops projects from a build that still
reports green. This is already how `EnsureJauntyQRoot` works and the same trap applies here.

**This collides with the tag-only release policy** decided earlier for NativeWeb (`dev` → `main`
`--no-ff` plus a semver tag, no package publishing). Tag-only works for checkout mode. Three
separate downstream repos, potentially on different machines and in CI, want package mode.
Unresolved — §10 Q1.

---

## 6. Will not do

| | Why |
|---|---|
| Runtime plugin loading, `Assembly.LoadFile`, `Reflection.Emit`, Roslyn scripting | Hard NativeAOT boundary, not a tradeoff. Handoff §1 is correct and stands |
| Fluent/LINQ query builder over JauntyQ (`User::where(...)->take(10)`) | That is rewriting the ORM. SQL-first is JauntyQ's thesis |
| Models → migrations (Django `makemigrations`) | Inverts JauntyQ's schema-as-source-of-truth. A JauntyQ decision if ever, not NativeWeb's |
| `select_related` / eager loading / relationship declarations | Genuinely wanted, genuinely **JauntyQ's job**. Goes on a joint asks list (§10 Q3), not this backlog |
| React, Tailwind, client-rendered SPA | Settled in `AGENTS.md` |
| SQLite for development | `AGENTS.md` binds PostgreSQL only. `admin-cms-handoff.md` §0 and `native-web-architecture.md` §3 both still say otherwise; both are stale and need correcting |
| A generic reflection-based "admin over anything" fallback | Handoff §6, still right |

---

## 7. Will do, in order

### Tier 1 — credibility. Nothing else counts without these.

**T1.1 Getting-started docs and a clone that builds.** A clean clone currently cannot build
(unresolved JauntyQ root). A framework you cannot start is not a framework. Already planned,
blocked on nothing.

**T1.2 Kill the endpoint preamble.** A source-generated binder from a record — FastAPI's
`BaseModel`, Rails' strong params — plus an endpoint filter carrying antiforgery, 415 and the
503-degrade. Highest leverage in the repo: it touches every route ever written afterwards, and
it is the difference between 393 lines and roughly 120 for the same four routes. Validation
attributes on the record emit checks; a field the record does not declare is not bound, which
is `permit` enforced by the compiler.

**T1.3 Point the render generator at HTML pages, not only email.** The generator already
exists. Contextual encoding by construction deletes an entire XSS class that is currently 13
files of hand-written `Encode()` calls.

### Tier 2 — the differentiator.

**T2.1 Benchmark it and publish the number.** Every framework in §1 leads with RPS. We have
none. This is roughly a day of work for the most persuasive artifact the project can own, and
it is the only claim in the pitch that is currently unevidenced.

**T2.2 `dotnet new nativeweb` template pack** plus a `dotnet tool` global tool for what
templates cannot express (adding a page, an endpoint, a scaffolded model to an existing
project). Rails' most-loved feature, at .NET-idiomatic cost.

### Tier 3 — the Django killer feature.

**T3.1 Scaffolding generator** (§8). `AdminEndpoints.cs`'s 940 hand-written lines are the proof
it is needed. Larger than tiers 1 and 2 combined; do not start it before T1.2 lands.

### Tier 4 — speculative.

**T4.1 CMS blocks**, handoff phases 4-7. The block-type-as-code / page-as-data split is the
right design and should be kept as design-of-record. Building it before a content site exists
to run it is inventing requirements.

---

## 8. The scaffolding attributes: replacing `[AdminModel]`

The handoff proposes one attribute. One attribute cannot answer the three questions every real
screen asks, and conflating them is precisely why Django applications outgrow `django.contrib.
admin` and start hand-writing views.

**Three orthogonal axes:**

```csharp
[Scaffold(Route = "/account/apps")]      // 1. generate WHAT
[Authorize(Policy = "authenticated")]    // 2. WHO may reach it
[ScopedBy(nameof(OwnerUserId))]          // 3. WHICH rows they see
public sealed partial class Application { … }
```

| Axis | Question | Django | Rails scaffold | Proposed |
|---|---|---|---|---|
| Generate what | which views exist | admin only | all seven RESTful | `[Scaffold]`, per-operation opt-out |
| Who may | authorization | `is_staff` | not addressed | `[Authorize]`, any policy |
| Which rows | row-level scope | not addressed | not addressed | `[ScopedBy]` |

**`[ScopedBy]` is the differentiator and the one nobody else does at compile time.** It emits a
mandatory scope predicate into the generated JauntyQ call, so *omitting* it on a
non-admin-reachable model is a compile error rather than a silent cross-tenant data leak. This
is the actual shape of every app named in D1: an epass.io user sees their own sessions and
applications, a justsmtp tenant sees their own domains, a ghosthive user sees their own hosts.
"Admin sees all rows" is the *rare* case, not the default the handoff assumed.

**Field-level attributes**, renamed off the `Admin` prefix for the same reason:
`[ListColumn]`, `[Searchable]`, `[Filter]`, plus `[NeverExpose]` so a scaffolded view cannot
render a passphrase hash or a recovery code even by accident.

**Dependency:** `[ScopedBy]` needs the generated JauntyQ query to accept a scope predicate.
That is a joint ask on JauntyQ, not something NativeWeb can do alone — §10 Q3.

---

## 9. Reconciling `docs/admin-cms-handoff.md`

**The architecture is sound; the framing is stale.** Keep verbatim: §1 (no dynamic loading), §2
(the admin/CMS conceptual split), §3.3's `FrozenDictionary` dispatch with no
`Activator.CreateInstance`, §6's non-goals.

Correct or drop:

| Handoff says | Reality |
|---|---|
| Builds on `ARCHITECTURE.md` | No such file. `docs/native-web-architecture.md` is the nearest, and it has its own stale sections |
| JWT Bearer is the default scheme | We shipped cookie/session auth (`SessionAuthenticationHandler.cs`) |
| SQLite for dev, Postgres for prod | `AGENTS.md` binds Postgres only |
| `[AdminModel]` + `[AdminListDisplay]` … | Superseded by §8 |
| §4 process split (authoring/delivery) | A phase-6 concern. Correct, but must not shape phase-1 design |

Its four open items (§7) remain genuinely open. Item 1 — how a scaffolded model finds its
JauntyQ query — is the one that decides whether any of this is fun to use.

---

## 10. Open questions

**Q1 — Package publishing.** Tag-only releases were decided for NativeWeb, but three downstream
repos in CI want package mode (§5). Publish `NativeWeb.*` to the private GitHub feed, or require
every consumer to hold a checkout? Note that CI's package-mode restore for JauntyQ already has
an unresolved feed-reachability gap, so this is not a solved problem being copied.

**Q2 — Does the ring boundary get enforced now or documented now?** The reference-direction
check in §4 is maybe an afternoon. Cheap insurance, or premature?

**Q3 — Joint asks on JauntyQ**, to be raised as one batch rather than piecemeal:
scope-predicate injection for `[ScopedBy]`, the model-to-query naming convention (handoff §7
item 1), and whether eager-loading / relationship declaration is on JauntyQ's roadmap at all.

**Q4 — Does the framework eventually leave this repo?** Not now. But if ring 0 and ring 2 are
genuinely different products, a shared repo will keep leaking the boundary. Revisit after the
first downstream consumer.

**Q5 — Which app is the POC (D4)?** A throwaway sample in-repo, or the smallest of
justsmtp.com / ghosthive.com used as the real first consumer? The second proves more and costs
more.
