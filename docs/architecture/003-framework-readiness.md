# 003 — Framework readiness: where NativeWeb is, and what it is missing

Measured 2026-08-31 on `dev` at `6c164ee`, the day after the identity provider left
(`../decisions/2026-08-31-007-idp-leaves-nativeweb.md`). Written because the extraction changed
what this repo *is*, and nothing had re-answered "is it a working framework yet" against the tree
that is left.

The question this answers: **starting from the philosophy — a framework, like Django, for building
server-rendered Extrode applications — what exists today, what is missing, and what is the
shortest road to a version someone can look at and work in.**

## 1. What is here, measured

Six libraries carry the whole framework. Line counts are `*.cs` outside `bin/obj`:

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
| **total** | **~7,600** | plus `nativeweb.assets` (vendored htmx, `boot.js`) |

Verified this session: solution builds **0 warnings / 0 errors**; **444 tests pass** across seven
suites, none skipped; `tools/bench` 87 Deno facts pass; structure-audit reports 0 errors.

`apps/sample` is 10 files. Its routes answer, measured against a running instance on port 5399:

| route | status | what comes back |
|---|---|---|
| `/healthz` | 200 | empty |
| `/` | 302 | to `/admin` |
| `/admin` | 200 | the generated admin index, 3,345 bytes, styled |
| `/admin/articles`, `/admin/tags` | 200 | generated CRUD |
| `/articles` | 200 | a rendered list page with an antiforgery-minted filter form |

**So there is a working UI today, and it is the scaffold's.** `/admin` ships its own inline
stylesheet — dark theme, CSS custom properties, table, form, pager, empty state — and is the one
screen in this repo that looks designed. `dotnet run --project apps/sample` reaches it in about
twelve seconds from a warm build.

## 2. The gap, in one sentence

**The framework's libraries do not add up to an application; `skeleton/` makes up the difference
by copying source, and nothing compiles `skeleton/`.**

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

That is roughly twenty source files and the entire design system. Three consequences follow, and
they are the whole of what is wrong:

**(a) The sample cannot use any of it, so what it renders is bare.** `apps/sample` has no
`wwwroot/` and no `templates/`. Its `/articles` page is valid HTML with no stylesheet link, no
htmx, no layout, no fonts — measured above. The only styled surface in the repo is the scaffold's
inline CSS, which is a different design language from `skeleton/`'s tokens. **A framework whose
reference application cannot show its own design system has no answer to "show me the UI".**

**(b) `skeleton/` is in no solution, nothing compiles it, and as of this week nothing checks it
either.** Already milestone 1 in `work/milestones.md`: ten drifts from `apps/web` accumulated
undetected and were found by an audit rather than by CI. The mitigation was
`SkeletonParityTests` (2026-08-30), 384 lines and fifteen facts pinning the skeleton's text
against its twin — **and it is gone.** It lived at `tests/nativeweb.web.tests.unit/`, because the
twin it compared against was `apps/web`, so the extraction deleted it along with the tier
(confirmed: it is present at tag `pre-idp-extraction`, absent at `HEAD`). Text parity was always
the weaker guarantee — it catches divergence, not breakage — but it was the only guarantee, and
the skeleton now has none. Restoring cover is not a matter of rehoming the file: its comparand no
longer exists, so the replacement has to be a build, which is the destination anyway.

**(c) Copy-per-product is not a framework seam.** Django does not hand you `SecurityMiddleware` to
paste; you add a string to `MIDDLEWARE` and upgrades reach you. Every file in the table above is a
fix that cannot be shipped to an existing product — including security fixes. This is the
philosophy failing at the point where it matters most, and it is the same class of mistake the
extraction just corrected: something that should have been a boundary was a copy.

## 3. What else is unfinished

Carried from `work/todo.md` and `work/milestones.md`, not re-derived:

- **`scripts/deploy/` still names `apps/web`** in six places, and has never run against a real
  host (spec 011 T15). Milestone 3 is unmet and every elapsed time in the deployment reference
  reads `NOT YET MEASURED`.
- **`db/migrate.sh` and `db/test-run-lock.sh` are unreachable** — `MIGRATIONS_DIR` resolves to the
  deleted `db/migrations/`. Retarget or retire.
- **The OTel-under-AOT gate stopped gating.** `check-publish-budgets.ps1` was the only place the
  OpenTelemetry registration path ran inside a real NativeAOT image, and it ran because the target
  was `apps/web`.
- **The generated admin is unauthenticated unless a model opts in.** `[Auth]`
  (`libs/nativeweb.scaffold/ScaffoldAttributes.cs:57`) emits `RequireAuthorization`
  (`libs/nativeweb.scaffold.generator/ScaffoldEmitter.cs:174-180`) and is per-model, so a model
  without it is world-readable and world-writable. Measured: `apps/sample`'s models carry no
  `[Auth]`, and `GET /admin` and `/admin/articles` both answer **200 with no credentials**.
  Defensible as a framework default only if it is a loud one, and Django's is the opposite —
  `django.contrib.admin` is behind `is_staff` from the first request. Whether the default flips,
  and whether the reference app should model the safe shape rather than the bare one, is a
  decision this repo has not taken. Flagged for the reviews rather than assumed.
- **The framework has no password-policy primitives.** `LockoutPolicy`, `CommonPasswordChecker`,
  `HibpBreachChecker` left with `libs/nativeweb.auth`. Django ships password validators; these are
  on the `contrib.auth` side of the line and are a candidate to come back as framework.
- **Consumption requires two source checkouts** (this repo and JauntyQ), and JauntyQ may not be
  redistributed. Milestone 6.
- **CI is red on every push** for an org-side JauntyQ package-read grant. Out of scope for this
  repo by standing decision; noted so a reader is not surprised.

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
