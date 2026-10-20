# NativeWeb — Lean Native Web architecture + epass.io redesign

## Context

`extrode.com` runs several apps (epass.io, justsmtp.com, ghosthive.com) on a **Svelte frontend + Deno BFF** presentation layer sitting on top of a **.NET 10 NativeAOT** backend and a **JauntyQ** (compile-time SQL source-generator) data layer. The Deno/Svelte layer is the bottleneck: measured ~13.8x slower and ~3.8x more memory than an in-process ASP.NET Core render (per `docs/native-web-architecture.md`).

**Goal:** build the "Lean Native Web" (LNW) architecture — ASP.NET Core Minimal APIs + HTMX + Alpine, server-rendered HTML, NativeAOT, JauntyQ — and use it to **completely redesign epass.io** in this repo, styled with the **v2 green password-manager design language** (`docs/design-prototypes/epass.zip`, incl. its logo). NativeWeb then **replaces** epass.io and becomes the **reference template** for migrating justsmtp.com / ghosthive.com.

**Key finding from exploration:** epass.io already owns the durable, framework-agnostic core — `libs/epass.{auth,security,mail}` (TokenIssuer, OpaqueTokens, Argon2id Passphrases, mail), `libs/epass.data` (JauntyQ host), and `db/` (46 Postgres migrations + 178 inline `.sql` queries + committed `jaunty.schema.json` + seed). Only `apps/web` (Deno/SvelteKit BFF) and the `apps/api` endpoint layer are being replaced. So this is a **presentation + auth-hosting rewrite that reuses the domain core**, exactly the arch doc's "reuse C# domain, rewrite frontend+BFF" principle.

## Decisions (validated)

1. **One app.** Collapse epass's `apps/api` + `apps/web` into a single ASP.NET Core app (`apps/web`). One AOT binary, one `NpgsqlDataSource`, one session store, one JsonSerializerContext chain — "server owns the app." The OAuth **authorization-server** surface stays an isolable `Oauth/` route group (`/oauth/token,/introspect,/authorize,/device`) with its own partial `JsonSerializerContext`, so siblings that aren't IdPs can omit it. Human (cookie) vs machine (client-auth) traffic handled by **separate auth schemes/policies in one process**.

2. **Vendor the core now.** In Phase 0, **copy** `db/` + `libs/epass.{auth,security,mail,data}` into NativeWeb (it replaces epass.io, so it should own them). Keep `epass.*` names initially (zero churn); neutral-naming is a later refactor slice. Rejected: cross-repo ProjectReference (JauntyQ's `$(JauntyQRoot)` probe + `AdditionalFiles` globs are **relative** — `../../db/queries`, `../../db/schema` — and won't resolve cross-repo; also couples to a repo slated for deletion). **Feature-freeze epass.io's libs/db on copy** to avoid two sources of truth for 178 queries.

3. **Postgres-only** (running locally, pid confirmed). JauntyQ snapshot is single-dialect; the existing Postgres `jaunty.schema.json` copies unchanged. Connection: `ConnectionStrings:Epass` (env `ConnectionStrings__Epass`), pooled singleton `NpgsqlDataSource`; other config `Epass:*`.

4. **Auth: custom `AuthenticationHandler`** — opaque session token in an HttpOnly cookie, validated against a `sessions` table (mirrors epass `bff_store`, 14d TTL) via JauntyQ; reuses `TokenIssuer`/`OpaqueTokens`/`Passphrases` as-is. Authorization **policies** replace `X-Admin-Key`. AOT-clean: **no ClaimsPrincipal serialization** (materialize claims per-request from the session row), **no IDataProtection on the session cookie** (value is a random opaque token, state in Postgres → free revocation + horizontal scale). ASP.NET Core owns presentation **and** session (no Deno BFF).

5. **First slice = the `auth/` route group** (SignIn/CreateAccount/Unlock/2FA/Recovery) — pure re-skins of epass's existing users/sessions/totp/webauthn/passphrase/magic-link domain, **zero new schema**. Settings, Admin, and OAuth authorize/consent also map to existing tables.

### Open product decision (GATED — does not block Phase 0 / Slices 1–2)
v2's *design* is a Bitwarden-style **password manager** (Vault, Share/Send), but epass.io's *domain* is an **OAuth identity provider** with **no vault/sharing schema**. Before any Vault/Share slice: decide **"is epass becoming a password manager, or just adopting v2's look for the IdP?"** Building vault UI without backing schema = rotting mock screens. IdP-parity slices proceed regardless; Vault/Share are gated behind a separate spec + migrations.

## Prerequisites — clear before Phase 0 (Fable blockers)

- **B1 — AOT toolchain.** NativeAOT linking on Windows needs `cl.exe`/`link.exe` from a VS2022 Developer environment; they're **not** on PATH in the default shell, so `dotnet publish -c Release` (the Phase 0 gate) will fail at link. **Run Phase 0 AOT publishes from a VS2022 Developer Command Prompt (or import `vcvars64`).** Ordinary `dotnet build` still runs the AOT *analyzers* for warning-level verification without linking.
- **B2 — JauntyQ license sign-off (needs your decision).** JauntyQ is ISL-R licensed: local-checkout compile-time use only, **no redistribution of source/binaries**, and it's an *open legal question* whether generator OUTPUT baked into a shipped binary is a derivative work. NativeWeb ships a product binary. The likely safe path: these are **first-party self-hosted SaaS** (binaries never distributed to third parties) → not "redistribution." **Confirm in writing that self-hosted-SaaS use of generator output is permitted, and adopt "no binary distribution" as a hard product constraint** (if any consumer ever ships a binary, JauntyQ must be excised). I can't clear this — it's your call/relationship.

## Repo setup & git workflow

- `dev` = integration branch, `main` = release. **Never commit directly to dev/main.** Every unit of work: `git switch -c <type>/<name>` off `dev`, commit often, `--no-ff` merge to `dev`. Fable review before each merge.
- Scaffold operating docs (constitution/conventions/status/specs) via the `scaffold` skill; spec-driven `spec.md`→`plan.md`→`tasks.md` per major slice.
- Add `.gitignore` (bin/obj/TestResults/.vs/publish), `.gitattributes`, `.editorconfig`, repo `CLAUDE.md` (project rules + git workflow + pre-task protocol).

## Phase 0 — foundation (branch `chore/phase-0-foundation`)

Ordered; gate at each build:
1. Repo scaffold: flat `.slnx` (`apps/ libs/ tests/ tools/`), `Directory.Build.props` (LangVersion 14, Nullable, `TreatWarningsAsErrors`, `IsAotCompatible`, `InvariantGlobalization`, `EnableConfigurationBindingGenerator`), `Directory.Packages.props` (Npgsql 10.0.3, xUnit 2.9.3, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Playwright`), `.editorconfig`, `.gitignore`, `.gitattributes`. **Secrets (S3):** `ConnectionStrings:Epass` + later SMTP creds come from **dotnet user-secrets in dev / env vars in prod** — never real creds in committed `Epass:*` config.
2. Vendor core: copy `db/` + `libs/epass.{auth,security,mail,data}`; add `$(JauntyQRoot)` probe → `../../extrode-legacy-org.com/jauntyq`; wire `libs/epass.data.csproj` (`JauntyQ.Generator` as `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` + `JauntyQ.Runtime`; `AdditionalFiles` globs `db/queries/**/*.sql` + `db/schema/*.schema.json`). Confirm generator emits `JauntyQ.Generated`. **Migration runner (S4):** port epass's runner as a `tools/` deliverable. **Fresh DB (S5):** provision a **new `nativeweb_dev`** (never share epass's live DB), migrate from vendored `db/` to head, then `jaunty schema pull` to regen the snapshot.
3. The one app `apps/web`: `CreateSlimBuilder`, `PublishAot=true`, root `JsonSerializerContext` chain, pooled `NpgsqlDataSource` from `ConnectionStrings:Epass`, `Map*Endpoints` pattern, `/healthz`. **Gate: `dotnet publish -c Release` (from VS Dev env, B1) AOT-clean, zero trim/AOT warnings.** **(S6) Treat libsodium/Argon2id/Npgsql AOT-clean publish under `TreatWarningsAsErrors` as a real spike** — budget for per-assembly trim descriptors / targeted warning suppression, not a checkbox.
3a. **Baseline cross-cutting middleware (S1/S2/S7), added once here:** ASP.NET Core **rate limiting** (per-IP + per-account, applied to auth + `/oauth/token` groups); **security-headers** middleware (CSP, HSTS, `X-Content-Type-Options`, frame options) with a **nonce strategy decided now** for the inline theme script + HTMX/Alpine inline usage (avoid blanket `unsafe-inline`); **structured logging + minimal auth audit** baseline (AOT-compatible), request logging beyond `/healthz`.
4. Static/design: vendored htmx + Alpine under `wwwroot/vendor`; v2 design tokens (CSS custom props, green `#4aab69`, light+dark), fonts (Sora / Instrument Sans / JetBrains Mono), `logo.png`; `MapStaticAssets` fingerprinting (use asset-URL helper, not hardcoded `src`).
5. Base layout + `Render/` layer: `<head>` with antiforgery `<meta>` + **inline theme script** (read localStorage/`prefers-color-scheme`, set class before paint — avoids FOUC; not Alpine); a helper distinguishing full-page vs `HX-Request` fragment.
6. Error-fragment middleware: `HX-Request` → HTML error fragment, else full error page. **Contract: 200 + error fragment for expected validation errors**; non-2xx only for true failures (global `htmx:response-error` handler). Don't rely on htmx-4 default swap.
7. Antiforgery: `AddAntiforgery` + `UseAntiforgery` + **persisted key ring** (the one place DataProtection is needed); emit token to `<meta>`, attach as `RequestVerificationToken` header via global `htmx:configRequest` handler; session cookie `SameSite=Lax`.
8. AuthenticationHandler skeleton: opaque-cookie scheme + `sessions` table + `Admin` authorization policy; protected `/healthz/authed` probe. Ensure `/oauth/*` does **not** default-challenge a browser redirect. **(N3) Session hardening decided as the table is created:** absolute-vs-sliding TTL, rotation on privilege elevation, revocation-on-password-change.
9. 3-tier test harness: `tests/unit` (xUnit, `IsAotCompatible=false`); `tests/integration` (`WebApplicationFactory<Program>` — needs `public partial class Program {}` + `InternalsVisibleTo`; real Postgres, schema-per-run or transaction rollback isolation; asserts `/healthz`); `tests/e2e` (Playwright launches app, asserts layout + theme toggle).
10. Verify task (build + AOT publish + all 3 tiers). **(N1) CI workflow** running the 3 test tiers on branch push. Merge `--no-ff` → `dev`; Fable review.

## Slice roadmap (each: branch → unit+integration+e2e → Fable review → `--no-ff` to dev)

- **Slice 1 `feat/auth-signin`:** render `auth/SignIn` full page + HTMX POST → validate via JauntyQ + `Passphrases` (Argon2id) + `LockoutPolicy` → create session row → set HttpOnly opaque cookie → `HX-Redirect` to authed landing; error fragment on bad creds; sign-out. Exercises the full stack. Tests: unit (cred/lockout), integration (cookie set / 401 fragment / lockout-after-N on real PG), e2e (fill form → redirect + cookie + theme persists).
- **Slice 2 `feat/auth-register`:** `CreateAccount` → `Passphrases` hash + breach check + `ContactProtector` email → one-time-code/magic-link via `epass.mail` `SmtpTransport` → verify activates + starts session. Tests: unit (validation/breach), integration (faked mail, real PG), e2e (happy path w/ captured code).
- **3** 2FA/TOTP (enroll + step-up) · **4** WebAuthn/passkeys · **5** Recovery/Unlock (reset, recovery codes, re-auth) · **6** Settings (profile, sessions/devices, MFA mgmt) · **7** Admin (applications/realms/clients under `Admin` policy; retires `X-Admin-Key`) · **8** OAuth authorize + consent (auth-code+PKCE, rendered HTML) · **9** OAuth machine endpoints (`/token`,`/introspect`, device-code, magic-link grant; JSON) · **10** Polish/cutover (a11y, deploy to replace epass.io) · **(N4)** dedicated `refactor/neutral-naming` slice to rename `epass.*` → product-neutral before siblings adopt the template · **11+ GATED** Vault/Share (needs product decision + new spec + migrations first).

## Risks / foot-guns

- **JauntyQ schema-pull needs a live migrated DB** (all 46 migrations at head); stale snapshot → silent generated/runtime mismatch. CI needs a migrated PG.
- **htmx 4.0 error swap** — decide contract now (200+fragment for validation, see step 6).
- **MapStaticAssets fingerprinting** vendored htmx/alpine — content-hashed URLs break hardcoded `<script src>`; use asset-URL helper / import-map.
- **WebApplicationFactory** needs `public partial class Program {}` + `InternalsVisibleTo`; test projects `IsAotCompatible=false`.
- **Antiforgery persisted key ring** — else tokens die on restart / across instances.
- **Theme FOUC** — inline `<head>` classList script, not Alpine.
- **Scheme bleed** — `/oauth/*` machine endpoints must not sit behind cookie handler / browser-redirect challenge.
- **Two-sources-of-truth** — feature-freeze epass.io libs/db after copy.
- **InvariantGlobalization** — `ContactProtector`/email casing must use ordinal/invariant normalization explicitly.
- **JauntyQ security** — stay on inline `.sql` + auto-CRUD; **avoid `@proc`/`@call`** (un-remediated build-time injection path); treat `.sql`/snapshot as trusted first-party.

## Verification

- `dotnet build` and `dotnet publish -c Release` (AOT) both zero-warning.
- All 3 test tiers green: `dotnet test` (unit+integration against local PG) + Playwright e2e.
- Manual: run app, sign in via the v2 SignIn screen, confirm cookie set, theme toggle persists, protected route gated, error fragment renders on bad creds. Screenshot the rendered v2 UI for visual confirmation.
- Fable review clean before each `--no-ff` merge to `dev`.

## Scope this planning round covers

Phase 0 foundation + Slices 1–2 (auth sign-in / register) in detail; Slices 3–10 as sequenced headings; Vault/Share (11+) gated behind the product decision above.
