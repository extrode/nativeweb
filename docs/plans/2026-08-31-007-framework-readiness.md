# 007 — From here to a framework you can see and work in

Plan for the destinations in `../architecture/003-framework-readiness.md`, sequenced after three
independent reviews (fable, glm-5.3, qwen3.8-max) both verified that document and argued against
its ordering. Written 2026-08-31 on `dev` at `6c164ee`.

**The goal, restated as an acceptance test.** One command starts `apps/sample`; a browser shows a
styled page in the framework's own design language; a filter interaction happens over htmx without
a full page load, and still works with JavaScript off; editing a template or a stylesheet shows up
on refresh without restarting anything.

## The ordering argument, and why the assessment's was wrong

The assessment proposed: build the library seam first, then make the sample show it. Both reviews
rejected that, independently and for the same reason — it front-loads weeks of invisible
refactoring of security-relevant code, and it does the refactor with no live consumer to prove the
seam is the right shape. Fable put the risk plainly: the files being moved carry invariants that
exist only as prose, and nothing red turns if the move breaks one.

The order below inverts it. **Phase 1 uses what the repo already has and produces the visible
result. Phase 2 does the structural work with `apps/sample` as its proving consumer.** The one
thing Phase 1 must not do is copy the design system into `apps/sample` — that would make a third
copy and leave Phase 2 harder than it is now. Hence T1, which is small and comes first.

## Phase 1 — a UI you can see and edit

Strictly sequential except where noted. No new libraries; nothing security-relevant moves.

**T1. Make the design system canonical.** Move `tokens.css`, `base.css`, `components.css` from
`skeleton/apps/__product__/wwwroot/css/` into `libs/nativeweb.assets/css/`, and give
`libs/nativeweb.assets` a real csproj — today it is a holding directory with `boot.js` and one
rollback file. The skeleton keeps its vendored copies, which is the pattern its README already
documents. *Done when:* the canonical files exist, a stamp still builds, and a test asserts the
skeleton's copies are byte-identical to canonical.

**T2. Vendor the assets into `apps/sample` and link them.** Create `apps/sample/wwwroot/` with the
CSS from canonical and htmx/Alpine/fonts from the skeleton's vendor tree, plus the same
byte-identity test. Wire the `<link>` and `<script>` into `ArticlesPage.Render`, and add
`UseStaticFiles` if `CreateSlimBuilder` does not supply it — to be checked at runtime, not assumed.
*Done when:* `/articles` renders styled, with fonts and htmx loaded. **This is the first visible
result and the smallest step that produces one.**

**T3. Put the sample on the real render pipeline.** Reference `nativeweb.render` and its generator
from `apps/sample`, with `templates/**/*.html` and `locales/*.json` as `AdditionalFiles`, the way
`skeleton/apps/__product__/__product__.csproj:71-90` does. Add `templates/layout/page.html` and
`shared/antiforgery.html`; render `/articles` through the layout instead of a hand-built `<head>`.
Fix `ArticlesPage.cs:82` to read `tokens.FormFieldName` rather than hardcoding
`__RequestVerificationToken`. *Done when:* the page renders through a compiled template. This is
also the first time `nativeweb.render` is exercised outside the skeleton, which is what a reference
app is for.

**T4. One htmx interaction — the first anywhere in the repo.** Convert the `/articles` filter to
`hx-post` swapping the list fragment, keeping the existing full-page POST as the no-JS path. The
antiforgery form already exists. *Done when:* the swap is visible in the network tab, and the page
still filters with JavaScript disabled. **Note what this is:** the search found zero `hx-`
attributes in the entire repository, so this is the first live exercise of htmx and of `boot.js`,
both of which have been vendored, upgraded and benchmarked without ever running.

**T5. The one command and the edit loop.** `dotnet watch run --project apps/sample`, documented in
the README and quick-start. Dev runs are JIT — `PublishAot` affects publish only — so the loop is
fast and `IsAotCompatible` still guards the contract. *Runs in parallel with T3/T4 once T2 lands.*
*Done when:* editing a template or a stylesheet is visible on refresh with no manual restart.

**T6. Correct the stale records.** `work/milestones.md:21` says nothing compiles the skeleton;
`.github/workflows/ci.yml:309` has stamped, built and tested it for some time. Rewrite milestone 1
around what is actually missing: behavioural assertions, not compilation. *Parallel, cheap.*

At the end of Phase 1 the framework has not changed shape. What changes is that it can be seen,
and that every later claim about the seam has a running consumer to be tested against.

## Phase 2 — the seam

This is the structural work, and it needs a spec of its own before it starts: it moves
security-relevant code, and section 3d of the assessment says nothing would turn red if the move
broke an invariant.

**T7. Assert the invariants before moving anything.** The spec-006 triangle first —
`PublicPageCache` / `Chrome.Anonymous` / antiforgery: anonymous variant byte-stable, no token
minted, no cookie set, no identity in a shared cache entry, cache after the rate limiter, locale in
the key. Today all of that is commentary and no test in `tests/` so much as names
`SecurityHeadersMiddleware`, `PublicPageCache` or `ErrorPageMiddleware`. *This gates T8.*

**T8. Extract `libs/nativeweb.web`.** One library, not five, following the house style the scaffold
already sets — *"this method hides nothing; it is exactly four calls, every one of them public and
individually callable"*. It carries the eleven middleware files, `Chrome`, `AssetUrls`, `Flash`,
`StartupConfigGuards`, `ReadinessCache`, and the CSP-report and `security.txt` endpoints. Surface:
`AddNativeWebWeb(NativeWebOptions)` — a typed options record bound by hand, not reflection-bound
`Configure<T>`, which AOT will not take — and `UseNativeWebPipeline()`, whose ordered body **is**
the Django `MIDDLEWARE` equivalent. A config-driven string list is the wrong translation: it needs
reflection, and the ordering constraints here are the documentation. Two constraints the design
must answer, both from the fable review: CSP hashes are compiled per product from
`Templates.Home.Index.InlineScriptCspHashes`, so moving the layout's `<head>` scripts changes where
they come from; and asset delivery must materialise physical files through an MSBuild `.targets`,
because `AssetUrls` depends on fingerprinting and precompression that embedded resources bypass.

**T9. Shrink the skeleton to what a product owns.** Its `Program.cs` should fall from 489 lines to
roughly 40. What stays product-owned: the layout's body, locales, the product stylesheet, logo,
static pages, appsettings, route mappings, per-page CSP, and — deliberately — who may reach the
admin.

**T10. Scaffold admin on the shared tokens.** Only after T8, and with a decision recorded: the
generator's self-contained inline stylesheet (`ScaffoldEmitter.cs:530`) is a deliberate property —
`/admin` works with no host wwwroot. Linking the design system trades that away. Both reviews
flagged it as off the critical path.

## Phase 3 — the things Phase 1 and 2 do not produce

**T11. The scaffold defects.** *Done 2026-08-31 on `fix/017-scaffold-defects`.* All five, plus the
two the 015 review added (`?page` overflow, the discarded created key) and the update race. Two
things the audit did not name turned up with them: nullable `double` and nullable `Guid` failed to
compile the same way `short` and `float` did, and the generated files relied on `ImplicitUsings`, so
a consumer with it disabled got source that would not build. Found by compiling the generator's
output rather than reading it. The two-lens review of the batch added five more, all fixed in the
same branch: a nullable `bool` emitted `bool? && bool`; a rejected form was redisplayed from the
converted item and under the Edit heading; an empty key redirected to the list; `SearchFields` used
a C# 12 collection expression compiled with the consumer's `LangVersion`; and the reference store's
update was check-then-act. Four observations went to `work/todo.md` instead of the diff — the `new`
route-segment key collision, `JauntyArticleStore` ignoring `searchFields`, unencoded labels in
emitted markup, and a repeat delete answering 404. D2 below is untouched and still open. From the qwen audit, in
severity order: the admin index emitted with
no authorization metadata (`ScaffoldEmitter.cs:468-479`); `short`/`float` generating code that does
not compile (`ScaffoldEmitter.cs:402-427`); `[ScaffoldSearch]` never reaching the store
(`IScaffoldStore.cs:26`); silent coercion of malformed numeric, `Guid` and `DateTime` input
(`ScaffoldValues.cs:23-65`); `DeleteAsync` reporting success on a `false` (`ScaffoldEmitter.cs:311`).
Each is small and independent; they can run in parallel with Phase 2.

**T12. Deploy and database hygiene.** Retarget `scripts/deploy/` off `apps/web` (7 occurrences
across 5 files); decide whether `db/migrate.sh` and `db/test-run-lock.sh` are retargeted or retired
— retirement is a cleanup script the user runs; restore an OTel registration so
`check-publish-budgets.ps1` gates OTel-under-AOT again.

## Needs decision before Phase 2 or 3 can be scoped

**D1. Does a `contrib.auth` equivalent come back?** This is the largest open question and no task
above produces it. The skeleton registers no authentication at all, yet the framework advertises
`[Auth]` and the sample demonstrates it — so a stamped product that follows framework guidance gets
a runtime failure for want of a scheme to challenge with. The identity provider was supplying that
answer invisibly. On this repo's own Django line, `contrib.auth` — a user primitive, a session
primitive, a login round-trip, password validators — is framework-side. Milestone 2 cannot complete
without it. The alternative is to state plainly that NativeWeb ships no auth and every product
brings its own, and to stop advertising `[Auth]` as a framework feature.

**D2. Should the generated admin be closed by default?** Qwen argues yes, with the framework's own
reasoning turned around: `AddScaffolding` refuses to register an authentication scheme because *"a
default supplied from a library is one nobody reviews"* — and a posture supplied by omission is
equally unreviewed. Django's admin is closed from the first request. Against: it is a breaking
change for any consumer, and openness is currently demonstrated as a legitimate choice by
`apps/sample`'s `Tag` model. Independent of the answer, the index defect (T11) is a bug either way.

## Flow

Phase 1 is Standard tier and this document is its plan. Phase 2 earns a spec of its own — it
touches security-relevant code and has two unanswered design constraints. Phase 3 is ordinary
backlog. Nothing here starts without approval.
