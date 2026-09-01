# 004 — Consumer readiness: what the three products need, and what they cannot get

Measured 2026-09-01 on `dev` at `b21023e`, by reading `epass.io`, `justsmtp.com` and
`ghosthive.com` directly. Companion to
[`003-framework-readiness.md`](003-framework-readiness.md), which asked the same question from
inside this repository. This one asks it from outside: **a product wants to build on NativeWeb —
can it?**

The framing comes from a standing instruction: NativeWeb is the framework every Extrode web
product will use, whether it uses it today or not. A repository that has not adopted it yet is
still a consumer whose gaps count.

## The answer in one table

| repo | web tier | references NativeWeb? | uses NativeWeb? | builds today? |
|---|---|---|---|---|
| `epass.io` | `apps/web-native`, 19 `.cs`, Minimal API + AOT | yes, 2 project refs | **no — zero type uses** | yes |
| `justsmtp.com` | `apps/web`, Minimal API + AOT | yes, 4 project refs | yes, 8 files | **no** |
| `ghosthive.com` | `apps/web`, 61 files, SvelteKit 5 on Deno | no | no | yes |

Three products, three different relationships to the framework, and **not one of them is the
relationship the framework was designed for.**

## 1. `epass.io` declares the dependency and hand-copies the code instead

`apps/web-native/epass.web.csproj:62-63` references `nativeweb.render` and `nativeweb.security`.
Across its 19 source files, the string `NativeWeb` appears **twice, both in comments**
(`Program.cs:156`, `StartupConfigGuards.cs:8`). No `using NativeWeb.*`, no type constructed, no
method called.

What it has instead, each carrying a comment naming the NativeWeb file it was copied from:

| file | ports |
|---|---|
| `Render/Layout.cs:47-81` | the page shell, as one interpolated raw string |
| `Middleware/SecurityHeaders.cs:11-75` | CSP with a per-request nonce, HSTS, permissions policy |
| `Middleware/StaticPages.cs:24-112` | the clean-URL rewrite and its `FrozenDictionary` route map |
| `Render/Banner.cs:11-24` | flash banners |
| `Render/AssetUrls.cs:13-50` | content-hashed asset URLs |

**Every one of those five now ships in `libs/nativeweb.web`**, which spec 015 completed on
2026-08-31 — the day after the port was already in place. This is the clearest evidence the
copy-per-product diagnosis in `003` §2 was right, and it arrived from a repository that has never
read that document. `Layout.cs:9` says it plainly: *"Ported from nativeweb apps/web
Render/Layout.cs"*.

`Program.cs:450,807,1127,1416,1447` also repeats one rate-limit message string five times.

**The gap:** nothing tells a consumer that `libs/nativeweb.web` exists or what it replaces. It
has no README, it postdates every handover document, and the one consumer that would have used it
had already finished doing the work by hand.

## 2. `justsmtp.com` has been broken since the repository moved, and is now broken twice

Known and recorded — [decision 004](../decisions/2026-08-30-004-package-mode-for-consumers.md)
names the first half. The probe confirms both halves are live:

1. **The path is dead.** `Directory.Build.props:51-52` probes
   `..\..\extrode-legacy-org.com\NativeWeb` and `..\..\..\..\extrode-legacy-org.com\NativeWeb`. Neither exists — this
   repository moved to `extrode.com/nativeweb`. `NativeWebRoot` resolves to empty, so its four
   `ProjectReference`s at `apps/web/justsmtp.web.csproj:75,79-83` point at
   `\libs\nativeweb.render\...` from the drive root. `EnsureNativeWebRoot`
   (`justsmtp.web.csproj:112-115`) turns that into a hard build error rather than MSB9008
   silence, which is the one thing that went right.

2. **One of the four libraries has left.** `justsmtp.web.csproj:79` references
   `nativeweb.signin.epass`, and **seven of its files carry `using NativeWeb.SignIn.Epass`** —
   `Program.cs:18`, `Dashboard/DashboardExtensions.cs:11`, `Auth/SignInResolution.cs:5`,
   `Auth/SignInFailureCopy.cs:1`, `Auth/LoginPage.cs:7`, `Auth/LoginFlowStore.cs:3`,
   `Auth/AddressPlausibility.cs:5`. That library left on 2026-08-31 with the identity provider
   ([decision 007](../decisions/2026-08-31-007-idp-leaves-nativeweb.md)). Decision 007 recorded
   what went and why; it did not record that a downstream product was compiling against it.

Only one NativeWeb type survives the extraction in that tree: `context.GetLocale()` from
`NativeWeb.Render` (`Render/Chrome.cs:31,57`).

**Both fixes belong in `justsmtp.com`, not here.** What belongs here is the second one's cause: a
library was removed from the framework with no note to the product that referenced it, and
nothing in this repository would have caught that.

## 3. `ghosthive.com` is the stack NativeWeb exists to replace

`README.md` still says "concept stage, no application code yet". That is stale by a wide margin:

- **`apps/web`** — 61 files, SvelteKit 2 / Svelte 5 runes on Deno via `@deno/svelte-adapter`,
  with `@xyflow/svelte` and `@dagrejs/dagre` for the schema-designer canvas. Not a .NET project;
  not in `ghosthive.slnx`.
- **`apps/control-plane`** — 48 `.cs` files, Minimal API, `PublishAot=true`, Npgsql plus JauntyQ
  over `db/queries/**/*.sql`. **JSON only** — no endpoint in it returns HTML.

`ghosthive.com/work/todo.md:579-587` and `work/status/current.md:151-153` record the intended
migration of `apps/web` "from Deno + SvelteKit onto C# .NET 10 NativeAOT via NativeWeb" — and give
the path as `C:\home\code\extrode-legacy-org.com\nativeweb`, with `libs/nativeweb.{render,auth,data,security,assets}`.
Two of those five libraries no longer exist here, and the path is the same dead one justsmtp
carries. **The plan to adopt NativeWeb was written against a NativeWeb that is gone.**

Its reference implementation is named as `justsmtp.com`'s `apps/web-next/` on branch
`feat/004-nativeweb-web-tier` — which is the tree §2 shows cannot build.

## 4. The gaps, ordered by what blocks the most

### G1 — There is no way to consume NativeWeb except a checkout of `HEAD`

Eight libraries set `IsPackable` and a `PackageId` (`nativeweb.{web,render,render.generator,forms,forms.generator,scaffold,scaffold.generator,security,mail}`). Nothing packs them: no
`dotnet pack` step in either workflow, no version property, and **one tag in the repository's
history** — `pre-idp-extraction`, which is not a release. `justsmtp.com/Directory.Packages.props:49-52`
pins `NativeWeb.*` at `0.1.0`, and its own comment says those versions "resolve to nothing".

Decision 004 approved package mode on 2026-08-30 and listed what it obliges. **None of the four
items has been done.** Until one is, every consumer builds against whatever `dev` happens to be,
which is how §2 and §3 both broke.

### G2 — Removing a library has no consumer-facing consequence

`nativeweb.signin.epass` and `nativeweb.auth` left on 2026-08-31. Seven files in `justsmtp.com`
and a planning document in `ghosthive.com` still name them. Nothing in this repository knows any
consumer exists, so nothing could have flagged it. G1 is the structural fix (a pinned version
lets a consumer stay put); a `docs/` breaking-change log is the cheap one.

### G3 — A relying party is framework, and the framework has none

This is the gap that decides whether `ghosthive.com` can ever migrate, and it is the one that
needs a decision rather than work.

All three products authenticate, and all three authenticate **against epass.io**:

| repo | how |
|---|---|
| `ghosthive.com/apps/web` | OAuth authorization-code + PKCE to epass `/authorize`, code exchange at `/token`, `/oauth/userinfo`, then an opaque HttpOnly `SameSite=Lax` session cookie over a server-side token map |
| `ghosthive.com/apps/control-plane` | bearer tokens validated by epass introspection, 30s TTL cache, as an endpoint filter |
| `justsmtp.com/apps/web` | `AddEpassSignIn()` — the library that left |

AGENTS.md draws the line at Django's: `contrib.auth` is framework, the OAuth2/OIDC **server** is a
third-party package. An OAuth **client** — the relying-party half — is on the framework side of
that line, the same way `django-allauth` sits beside `contrib.auth` rather than beside an issuer.
`003` §3b already found that a stamped product cannot put anything behind auth because the
skeleton registers no authentication scheme; this is the same hole seen from three products that
have each filled it differently.

**This is a `Needs decision`, not a task.** Restating it plainly: does a generic
authorization-code-with-PKCE client, a session cookie primitive, and an introspection filter come
back as framework — with no epass-specific flow, realm or client registry — or does every product
keep writing its own?

### G4 — No JSON API tier

`ghosthive.com/apps/control-plane` is 48 files of Minimal API returning `TypedResults` over
source-generated STJ contexts, with endpoint filters for auth, six `AddHostedService` sweepers,
raw-body upload with a lifted Kestrel limit, and mTLS with pinned fingerprints. NativeWeb renders
HTML and has nothing to say about any of it.

That may be the right boundary — an HTML framework is not obliged to be an API framework. But it
means a ghosthive migration adopts NativeWeb for **one of its two backends**, and the framework
should say so rather than leave it implied.

### G5 — No list primitives, so every consumer builds keyset pagination twice

`ghosthive.com/libs/ghosthive.runtime` hand-rolls `ListQueryParser`, `FilterComposer`, keyset
`Cursor` and `ListExecutor` — for its *generated tenant* APIs. Its own control-plane list
endpoints then go without: `Program.cs:498` is `SELECT id, name FROM projects WHERE owner_sub = @sub`
with no limit and no cursor, and `/fleet` fetches the whole roster and paginates in the browser.

On this side, `IScaffoldStore<T>.ListAsync` took no ordering parameter when this was written, so
sort order was whatever each store silently picked (`003` §3b). **Closed on 2026-09-01 by spec
017**, which added `[ScaffoldSort]`, a sortable-field allow-list, and keyset cursor paging in
place of offset — a breaking change to the seam, recorded in
`docs/02-reference/breaking-changes.md`. Free-text `[ScaffoldSearch]` filtering is unchanged and
stays that way; structured filters were scoped out and are not coming.

### G6 — Seven of nine libraries have no README

Only `nativeweb.scaffold` and `nativeweb.assets` document themselves, and `nativeweb.assets` is
not a library. **`nativeweb.web` — the newest, the largest seam, and the one §1 shows a consumer
reimplemented by hand — has none.** `docs/README.md` already admits this; it is listed here
because §1 measured what it costs.

### G7 — Every guide assumes a greenfield product

`getting-started.md` goes clone → build → green suite → `tools/new-product.ps1`. That is the right
path for a new product and **not one of the three consumers can take it**: each has an existing
application, its own `Directory.Build.props`, its own solution and its own deployment. What is
missing is the other document — how an existing ASP.NET Core app adds NativeWeb: which packages,
which `Add`/`Use` pair, what it can delete afterwards, and what it must keep.

`epass.io`'s port in §1 is what happens without it.

## 5. What this document is not

Not a plan, and not a licence to change any of the three repositories. Fixes for §2 and §3 belong
in `justsmtp.com` and `ghosthive.com`. G3 is a scope question for the user, not a task. G1, G2,
G5, G6 and G7 are work in this repository and are recorded in `work/todo.md`.
