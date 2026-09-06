# 003 — Framework readiness: where NativeWeb is, and what it is missing

Measured 2026-08-31 on `dev` at `6c164ee`, the day after the identity provider left
(`../decisions/2026-08-31-007-idp-leaves-nativeweb.md`). Written because the extraction changed
what this repo *is*, and nothing had re-answered "is it a working framework yet" against the tree
that is left.

The question this answers: **starting from the philosophy — a framework, like Django, for building
server-rendered Extrode applications — what exists today, what is missing, and what is the
shortest road to a version someone can look at and work in.**

## 1. What is here, measured

Eight libraries carry the whole framework -- 7,606 lines across 41 files, independently
recounted by the glm review. Line counts are `*.cs` outside `bin/obj`:

| library | lines | what it is |
|---|---|---|
| `nativeweb.render.generator` | 3,633 | compiles `.html` templates into C# writers at build time |
| `nativeweb.scaffold.generator` | 1,007 | generates CRUD endpoints from a `[Scaffold]` model |
| `nativeweb.forms.generator` | 702 | generates a binder from a `[Form]` model |
| `nativeweb.security` | 687 | hashing, opaque tokens, bot traps, masking, a data protector |
| `nativeweb.scaffold` | 432 | the admin runtime: routing, store interface, values |
| `nativeweb.render` | 415 | the template runtime, locale catalog and negotiation |
| `nativeweb.forms` | 405 | `MapFormPost<T>` and the antiforgery/415 metadata |
| `nativeweb.mail` | 325 | SMTP transport |
| **total** | **7,606** | across 41 files |

`libs/nativeweb.assets` is not in that table and is not a library: it holds `boot.js`, a README
and `rollback/htmx-2.0.10.min.js`, with no csproj. Its README says apps vendor their own htmx and
Alpine, and only the rollback build lives there -- so the current htmx 4.0.0 exists solely at
`skeleton/apps/__product__/wwwroot/vendor/htmx/`. An earlier draft called it a vendored-asset
library; it is a holding directory.

Verified this session: solution builds **0 warnings / 0 errors**; **444 tests pass** across seven
suites, none skipped; `tools/bench` 87 Deno facts pass; structure-audit reports 0 errors.

`apps/sample` is 10 files. Its routes answer, measured against a running instance on port 5399:

| route | status | what comes back |
|---|---|---|
| `/healthz` | 200 | empty |
| `/` | 302 | to `/admin` |
| `/admin` | 200 | the generated admin index, 3,345 bytes, styled |
| `/admin/tags` | 200 | generated CRUD, open: `Tag` declares no `[Auth]` |
| `/admin/articles` | 302 | to `/signin`: `Article` declares `[Auth(Role = "admin")]` |
| `/articles` | 200 | a rendered list page with an antiforgery-minted filter form |

**So there is a working UI today, and it is the scaffold's.** `/admin` ships its own inline
stylesheet — dark theme, CSS custom properties, table, form, pager, empty state — and is the one
screen in this repo that looks designed. `dotnet run --project apps/sample` reaches it in about
twelve seconds from a warm build.

## 2. The gap, in one sentence

**The framework's libraries do not add up to an application, so `skeleton/` makes up the
difference by copying source into every product.**

Everything a real page needs beyond the render call lives in `skeleton/apps/__product__/` as files
a stamped product owns outright, not as a library it references:

| in `skeleton/`, per product | what it does |
|---|---|
| `Middleware/SecurityHeadersMiddleware.cs` | CSP with a per-request nonce, HSTS, frame and referrer policy |
| `Middleware/ErrorPageMiddleware.cs`, `StaticErrorShell.cs` | error rendering, and the shell used when render itself is what failed |
| `Middleware/AntiforgeryValidationMiddleware.cs` | the global antiforgery gate |
| `Middleware/PublicPageCache*.cs` (3 files) | output caching and eviction for anonymous pages |
| `Middleware/StaticPagesMiddleware.cs` | serving `wwwroot/pages/*.html` through the shell |
| `Middleware/RequestLoggingMiddleware.cs`, `QueryRedactingProcessor.cs` | request logs with query redaction |
| `Render/Chrome.cs`, `Render/AssetUrls.cs` | the page shell, and content-hashed asset URLs |
| `StartupConfigGuards.cs`, `ReadinessCache.cs` | fail-fast config validation, `/readyz` |
| `Flash.cs`, `CspReportEndpoint.cs`, `SecurityTxtEndpoint.cs` | flash messages, CSP reports, `security.txt` |
| `templates/layout/page.html`, `shared/*.html` | the layout, antiforgery partial, banners, bot traps |
| `wwwroot/css/{tokens,base,components}.css` | the design system |
| `wwwroot/vendor/{htmx,alpinejs,fonts}` | the vendored client runtime |

That is roughly twenty source files and the entire design system. Measured: the eleven middleware
and render-support files total **980 lines**, and `skeleton/apps/__product__/Program.cs` is another
**489** — so about **1,470 lines are copied into every stamped product**, against a framework of
7,600. Every product carries a fifth of the framework's mass as source it owns and cannot receive
fixes for.

Three consequences follow, and they are the whole of what is wrong:

**(a) The sample cannot use any of it, so what it renders is bare.** `apps/sample` has no
`wwwroot/` and no `templates/`. Its `/articles` page is valid HTML with no stylesheet link, no
htmx, no layout, no fonts — measured above. The only styled surface in the repo is the scaffold's
inline CSS, which is a different design language from `skeleton/`'s tokens. **A framework whose
reference application cannot show its own design system has no answer to "show me the UI".**

**(b) `skeleton/` is absent from `NativeWeb.slnx`, and its text-parity guard is gone.**
*Resolved 2026-09-05 — see the second correction at the end of this subsection; the guard is not
missing, its subject is.* `SkeletonParityTests` (2026-08-30) was 384 lines and fifteen facts pinning the skeleton's text
against its twin, and it left with the extraction. It lived at `tests/nativeweb.web.tests.unit/`,
because the twin it compared against was `apps/web` (confirmed: present at tag
`pre-idp-extraction`, absent at `HEAD`). Its comparand no longer exists, so it cannot be rehomed;
the replacement has to be a build.

**Correction, from the glm review.** An earlier draft of this section said nothing compiles the
skeleton, repeating milestone 1's wording. That is out of date. `.github/workflows/ci.yml:309`
defines a `skeleton-stamp` job that runs `tools/new-product.ps1 -Execute` into a sibling
directory, then restores, builds and runs the stamped product's unit, integration and e2e tiers
including the NativeAOT publish (ci.yml:375-433). The accurate statement is narrower: the skeleton
is not in `NativeWeb.slnx`, so a local `dotnet build` never touches it, and the CI job that does
runs in package mode (`JauntyQUsePackages: "true"`, ci.yml:340). `work/milestones.md:21` carried
the same stale wording and was corrected with it.

**Second correction, 2026-09-05: the replacement exists and this section asked for the wrong
thing.** "`SkeletonParityTests` still does not exist" was read as work outstanding for two weeks
after it stopped being work at all. That suite asserted that twenty C# files copied into
`skeleton/` still matched their twin in `apps/web`. Spec 015 moved those twenty files into
`libs/nativeweb.web`, so there is one copy and nothing to compare; `apps/web` left with the
identity provider, so there is no comparand either. The invariant is vacuous by construction, and
a suite reasserting it would pass without executing anything. What replaced it is eleven test
classes in `tests/nativeweb.web.tests.unit` asserting those files' behaviour, which is the gap
the paragraph below actually names, plus `tools/stamp-check.ts` proving a stamped product builds
and passes its own tiers here rather than only in CI. Spec 029 closed the last unguarded copy —
four HTML partials under `templates/shared/`, which are delivered by the framework now and
guarded by `FrameworkTemplateTests` and a build error, not by a parity assertion.

What is uncovered is behaviour, not compilation. **No test in `tests/` references
`SecurityHeadersMiddleware`, `PublicPageCache` or `ErrorPageMiddleware`** -- the fable review
checked, and I confirmed it. Those twenty security-relevant files compile and are smoke-tested
through a stamped product; none of their invariants is asserted anywhere. That is the gap that
makes moving them risky, and it is a different gap from the one milestone 1 names.

**(c) Copy-per-product is not a framework seam.** Django does not hand you `SecurityMiddleware` to
paste; you add a string to `MIDDLEWARE` and upgrades reach you. Every file in the table above is a
fix that cannot be shipped to an existing product — including security fixes. This is the
philosophy failing at the point where it matters most, and it is the same class of mistake the
extraction just corrected: something that should have been a boundary was a copy.

## 3. What else is unfinished

Carried from `work/todo.md` and `work/milestones.md`, not re-derived:

- ~~**`scripts/deploy/` still names `apps/web`, and has never run against a real host**~~ --
  both closed. The 7 occurrences across 5 files counted by the glm review are down to one, a
  past-tense note in `build.sh`'s header explaining what the default used to be. Spec 011 T15
  ran on real hosts on 2026-09-05 and every elapsed time in the deployment reference is a
  measured number. Milestone 3 shipped on 2026-09-05; what it does not claim is R14, R17b and
  R15, all unmet on the host and all in the runbook's gap table.
- ~~**`db/migrate.sh` and `db/test-run-lock.sh` are unreachable**~~ — retargeted at
  `db/sample/migrations/` on 2026-08-31 (plan 007 T12), with the extensions step made optional.
- ~~**The OTel-under-AOT gate stopped gating.**~~ — restored 2026-08-31. The registration was never
  removed: `apps/sample` gets it through `AddNativeWebWeb`. `check-publish-budgets.ps1` was setting
  the switch under the `NativeWeb` section, which the sample does not bind, so the value was unread.
- **The generated admin index is never gated, whatever the models say.** `[Auth]`
  (`libs/nativeweb.scaffold/ScaffoldAttributes.cs:57`) emits `RequireAuthorization`
  (`libs/nativeweb.scaffold.generator/ScaffoldEmitter.cs:174-180`) per model — but the emitted
  index route carries no authorization metadata at all (`ScaffoldEmitter.cs:468-479`), so `/admin`
  answers and enumerates every model including the gated ones. Measured against a running
  instance: `/admin` **200**, `/admin/tags` **200** (`Tag` declares no `[Auth]`, by design),
  `/admin/articles` **302 to `/signin`** (`Article` declares `[Auth(Role = "admin")]`,
  `apps/sample/Models.cs:11`). An earlier draft of this document said `/admin/articles` was open;
  that was wrong, and the qwen review caught it. The defect that survives is the index: a consumer
  who gates every model still cannot close `/admin`.
- **The framework has no password-policy primitives.** `LockoutPolicy`, `CommonPasswordChecker`,
  `HibpBreachChecker` left with `libs/nativeweb.auth`. Django ships password validators; these are
  on the `contrib.auth` side of the line and are a candidate to come back as framework.
- **Consumption requires two source checkouts** (this repo and JauntyQ), and JauntyQ may not be
  redistributed. Milestone 6.
- **CI is red on every push** for an org-side JauntyQ package-read grant. Out of scope for this
  repo by standing decision; noted so a reader is not surprised.

## 3b. What the three reviews found that this document did not

Commissioned 2026-08-31 against the draft above: **fable** (`think`, 40 turns, $3.03) on
architecture, **glm-5.3** (`glm-think`) on verification, **qwen3.8-max** (`qwen-review`) on the
scaffold. A first, broader qwen dispatch hit its 20-turn cap and returned nothing ($0.94); the
re-dispatch was narrowed to one subsystem with the file list supplied, which worked.

Corrections to this document are folded in above. What follows is new.

**Nothing in this repository uses htmx.** Fable found it; I confirmed with a whole-tree search:
**zero `hx-get`, `hx-post`, `hx-target` or `hx-swap` attributes** in any `.cs` or `.html` under
`apps/`, `libs/`, `skeleton/` or `tests/`. htmx is vendored, wired into the layout's CSRF-header
script, upgraded to 4.0.0 by spec 012, benchmarked, and exercised by no live surface anywhere. For
a framework whose architecture is "server-rendered HTML enhanced with HTMX", that is the single
most surprising measurement in this document. It also means the `boot.js` progressive-enhancement
path has never run against a real interaction.

**There is no composition root, and the twenty middleware files are the visible tenth of it.**
Fable's reframing, and it is better than section 2's. `skeleton/apps/__product__/Program.cs` is
489 lines of framework knowledge held as copied text: the Kestrel body-size limit, request
timeouts, forwarded headers, the IPv6-/64 rate-limit partitioner, data-protection key persistence,
the `__Host-` antiforgery cookie hardening, OTel wiring, `ReadinessCache` construction. The defect
narrates itself in a comment at `Program.cs:217-220` -- *"This was bare AddAntiforgery() until
2026-08-30... The framework app fixed this in its 2026-08-19 audit and the template never took the
fix, which meant every product stamped from it inherited the defaults."* That failure mode applies
to all 489 lines, not only to the middleware. The same is true of `templates/layout/page.html:16-46`,
where the CSRF-header script and htmx-config meta are framework behaviour held as template text.

**A stamped product cannot put anything behind auth, and the framework's own feature is the trap.**
The skeleton's `Program.cs` registers no authentication: no `AddAuthentication`, no
`UseAuthentication`, no sign-in route, and it never maps the scaffold. Meanwhile the framework
advertises `[Auth(Role = "admin")]` and `apps/sample` demonstrates it. A stamped product that
follows that guidance gets a runtime failure, because there is no scheme to challenge with. The
identity provider was silently supplying the whole "how does anyone sign in" answer, and section 3
above named only the smallest piece of what left. On this repo's own Django line, `contrib.auth` --
a user primitive, a session primitive, a login round-trip -- is framework-side, and **none of it
exists.** Nothing in section 4's destinations produces it, and milestone 2 cannot complete without
it.

**The scaffold has defects, not only absences.** From the qwen audit, each confirmed against the
generator source:

| finding | file | kind |
|---|---|---|
| The admin index is emitted with no authorization metadata, so `/admin` enumerates gated models | `ScaffoldEmitter.cs:468-479` | defect |
| `short` and `float` get a number input but fall through `Convert`'s `_` case to a string assignment -- the generated code does not compile | `ScaffoldEmitter.cs:402-403, 411-427` | defect |
| `[ScaffoldSearch]` decides only whether the search box renders; which fields are searched never reaches the store, which takes one opaque string | `ScaffoldEmitter.cs:197`, `IScaffoldStore.cs:26` | defect |
| Malformed numeric input coerces to 0, a bad `Guid` to `Guid.Empty`, a blank `DateTime` to `UtcNow`; the re-rendered form shows the coerced value, not what was typed | `ScaffoldValues.cs:23-65`, `ScaffoldEmitter.cs:424` | defect |
| `DeleteAsync` ignores a `false` from the store and redirects as success | `ScaffoldEmitter.cs:311` | defect |
| Update is GET-then-POST-overwrite with no concurrency token; last writer wins | `ScaffoldEmitter.cs:288-300` | defect |
| ~~No sorting: `IScaffoldStore<T>.ListAsync` has no ordering parameter, so order is whatever each store silently picks~~ — closed 2026-09-01 by spec 017 (`[ScaffoldSort]` + keyset cursor paging) | `IScaffoldStore.cs:26` | closed |
| No faceted filtering, no bulk actions, no foreign-key or relation display, no per-field widgets beyond `[ScaffoldMultiline]`, no readonly-on-form, no change history | | absence |

Both sample models are flat, so the relation gap is never surfaced by the reference app. Qwen also
found `apps/sample/Render/ArticlesPage.cs:82` hardcodes `__RequestVerificationToken` instead of
reading `tokens.FormFieldName` as the scaffold library does -- the reference app hand-duplicating a
framework constant.

**The reviews disagreed with this document's ordering, and with each other on one fact.** Both
fable and glm said the seam-first sequence in section 4 inverts the goal and buys weeks of
invisible work; both independently warned that the first move must be to make the design system
canonical, or `apps/sample` becomes a third copy of it and the seam gets harder. Fable additionally
argued for compiling the skeleton before moving any file, on the grounds that it has no coverage --
an argument that rests on the claim glm disproved, since the `skeleton-stamp` job already compiles
and tests it. Fable's underlying point survives the correction in weaker form: compilation exists,
behavioural assertions do not.

## 3c. Two constraints the seam design has to answer

Both from fable, neither previously recorded:

1. **CSP hashes are compiled per product.** `Program.cs:374` builds the public-page CSP from
   `Templates.Home.Index.InlineScriptCspHashes`, generated by the template compiler in the
   product's own build. Moving the layout's `<head>` scripts into a shared library partial changes
   where those hashes come from, and the design has to say where.
2. **Asset delivery cannot use embedded resources.** `AssetUrls` depends on fingerprinting and
   precompression of physical files, so a library shipping the design system has to materialise it
   into the consumer's `wwwroot` via an MSBuild `.targets`, not embed it.

## 3d. The highest risk, named by fable

**The plan moves roughly twenty security-relevant files whose subtlest invariant lives only in
prose.** The spec-006 triangle -- `PublicPageCache` / `Chrome.Anonymous` / antiforgery -- requires
that the anonymous variant be byte-stable, mint no token, set no cookie, and never render an
identity into a shared cache entry (`Chrome.cs:41-63`), with the cache after the rate limiter and
the key including locale. All of that is commentary. An extraction that gets one of them subtly
wrong ships cache poisoning or CSRF-token leakage into every stamped product, **and nothing red
would turn**, because no test asserts any of it. The mitigation is to write those assertions before
moving the files, not after.

## 4. Where this needs to go

Stated as destinations, in the order that unblocks the most:

1. **A library seam for the app shell.** The middleware, chrome and asset-URL machinery in the
   table above become one or more referenced libraries with an `AddNativeWeb…()` /
   `UseNativeWeb…()` pair, configured rather than copied. `skeleton/` keeps only what a product
   genuinely owns: its templates, its CSS, its routes.
2. **The design system ships with the framework**, so a consumer gets tokens, base and components
   without hand-copying, and the scaffold's admin uses the same tokens instead of its own inline
   sheet.
3. **`apps/sample` becomes a real reference app** — layout, stylesheet, htmx, at least one
   htmx-driven interaction and one form round-trip — so `dotnet run` shows the framework rather
   than raw markup. This is the deliverable that answers "I want to see the UI".
4. **`skeleton/` is compiled and tested on every run.** Milestone 1, and now unguarded entirely:
   the parity test that stood in for a build left with `apps/web`, so this is a regression against
   last week, not only an outstanding milestone.
5. **A stamped product builds, tests and deploys unedited.** Milestone 2, gated on 1 and 4.
6. **Deploy retargeted and executed once on a real host.** Milestone 3.

Ordering claim to be checked by review: 1 and 2 are the same change and should not be split; 3 is
what makes 1 and 2 verifiable by eye rather than only by test; 4 gets much cheaper once 1 has
moved code out of `skeleton/` and left less to keep in parity.

## 5. What this document is not

Not a plan. It names destinations and the evidence for them; the sequencing, the seam's actual
shape, and what ships first are the plan's job. Three independent reviews were commissioned
against it — fable, glm-5.3 and qwen3.8-max — precisely because the ordering claim above is the
part most likely to be wrong.
