# 003 — Tasks

Execution breakdown for `003-plan.md`. One task per branch, sequential, each merged to `dev`
with `--no-ff` after review before the next starts. This file is the tasklist of record for
this body of work; `work/tasklist.md` points here.

- [x] V1 `chore/style-enforcement` — R8–R9. Absorb the user's hand conversion of
  `apps/devmarks/Program.cs` (explicit Program/Main) and their VS-generated `.editorconfig`
  (var rules); restore the lost `[*]` global section (utf-8, lf, final newline); raise the
  var rules to build severity; `EnforceCodeStyleInBuild=true` in `Directory.Build.props`;
  convert `apps/web/Program.cs` the same way; fix repo-wide fallout (`dotnet format` assisted);
  all suites green.
- [x] V2 `feat/render-engine` — R10–R13, R41, R43–R44. `libs/nativeweb.render` (runtime:
  `TemplateWriter`, `Html` type, `IResult` adapters) + `libs/nativeweb.render.generator`
  (templates as AdditionalFiles → static classes, UTF-8 literals, encoded-by-default,
  partials/layouts/required slots, diagnostics). AngleSharp well-formedness test harness.
- [ ] V3 `feat/i18n` — R14–R17, R45. Catalog generator with dialect fallthrough (en root,
  build-time-resolved FrozenDictionary tables), `CultureNegotiationMiddleware`, lang/dir,
  en+ms catalogs, migration 0053 `users.locale`; e-mail content templates out of
  `nativeweb.mail` into app templates, rendered per recipient locale.
- [ ] V4 `feat/devmarks-templates` — R18–R20, R42. Scripted conversion of the 16 DevMarks
  render files to `templates/`; accessible form-field partial + aria-live announcements;
  axe-core gate in the DevMarks e2e fixture; conversion script kept for the IdP's later spec.
- [ ] V5 `feat/bot-traps` — R21–R24. `BotTraps` in `nativeweb.security` (4 honeypots + signed
  time canary), template partial + string overload, wired into every form in both apps,
  fake-success discipline, trap-event logging, e2e visibility assertion.
- [ ] V6 `feat/security-baseline` — R25–R31. HSTS, Permissions-Policy, `__Host-` session
  cookie, body-size limits, request timeouts, `POST /csp-report`, migration 0052 `audit_log`
  + admin writes, `security.txt`, `docs/security-baseline.md`, CI CVE gate.
- [ ] V7 `feat/perf-ops` — R32–R37. Brotli compression for HTML, OTel off-by-default,
  `/healthz` `/readyz`, `ValidateOnStart` everywhere, `StripSymbols`/`OptimizationPreference`,
  `Max Auto Prepare=20`, FrozenDictionary `AssetUrls`, CI binary-size + startup budgets.
- [ ] V8 `feat/static-pages` — R38–R40. `wwwroot/pages/` pipeline with clean URLs, no-cache +
  ETag, nonce-free CSP branch, static 404/500 via `ErrorPageMiddleware`, robots/sitemap.
- [ ] V9 `feat/skeleton` — R1–R3, R29. `skeleton/` tokenized shell, `tools/new-product.ps1`,
  systemd + Windows service files, `$(NativeWebRoot)` dual-mode props, pack metadata on the
  framework libs, conventions/ASVS docs.
- [ ] V10 `feat/manzil-extraction` + `chore/remove-devmarks` — R4–R7, R42.
  `scripts/extract-manzil.ps1` creates `C:\home\code\extrode-legacy-org.com\manzil.my` (full manzil.*
  rename, fresh history, skeleton operating files); manzil.my suites green in checkout mode;
  DevMarks projects removed from NativeWeb; NativeWeb suites still green.

Close-out: both repos green across all three suites; zero-warning Release AOT publishes;
screenshots (manzil.my en+ms, static landing, trap invisibility); size/startup baseline
recorded; CHANGELOG entry; archive pointer; `scripts/cleanup/architecture-v2.ps1` written.
