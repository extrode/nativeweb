# 027 — Plan
Spec: ./027-spec.md

Approved in plan mode 2026-09-04 and recorded here verbatim.

## Approach: sample forms examples (contact, upload, two-step)

## Context

`libs/nativeweb.forms` shipped in spec 014 and nothing exercises it beyond the scaffolded admin's
own edit screens. Two of the three form shapes a real application needs cannot be written at all
today: the generated binder accepts only `string?` members (`NWF003`, `FormModel.cs:284`), so a
file field is impossible, and nothing in the framework carries part-finished form state between
requests. Spec 027 (`docs/specs/027-sample-forms-examples/027-spec.md`, committed `734f032` on
`feat/027-sample-forms-examples`) closes both and proves them through three public forms in
`apps/sample`.

The user chose, 2026-09-04: framework capability **and** the sample examples; uploads on the
filesystem under a configured directory; two-step state in signed hidden fields, no server store;
contact mail through `libs/nativeweb.mail`.

## Grounding facts (verified this session)

- **Every bound member must be `string?` today.** `FormModel.IsNullableString` gates it and
  `NWF003` reports it. A file field widens exactly that check; nothing else in the model layer
  needs to change.
- **The emitter is one call per field**: `FormBinding.Field(form, "wire-name")` in a constructor
  argument list (`FormEmitter.cs:63-70`). A file field is a second helper alongside it.
- **`FormBinding.RequireFormContentType` is the binder's first statement** and already throws 415
  for a non-form body. It does not distinguish urlencoded from multipart, which R3 needs.
- **ASP.NET Core Data Protection is already wired**: `NativeWebPipeline.cs:352` calls
  `AddDataProtection().PersistKeysToFileSystem(options.DataProtectionKeyDirectory)`. So
  `IDataProtectionProvider` is in the container of every NativeWeb host, and
  `ToTimeLimitedDataProtector()` gives tamper detection and expiry with key rotation for free.
  **This is why the carried-state helper does not use `libs/nativeweb.security`'s
  `StoreDataProtector`**: that is AES-GCM for a database column, with no expiry and no rotation,
  and it would need a new key of its own to be configured per host.
- **`SmtpOptions.IsEnabled` is `Host` being set** (`SmtpOptions.cs:86`), which is the natural
  "SMTP not configured here" path for nw-dev. `SmtpTransport` is a concrete class with no
  interface, so the sample's contact endpoint cannot be tested without one.
- **Nothing registers SMTP in `apps/sample`** — grep finds no `SmtpTransport`/`SmtpOptions` there.
- **Bot traps are ready**: `BotTraps.HiddenFilledName`/`HiddenEmptyName`/`OffscreenFilledName`
  plus `BotTrapVerdict` in `libs/nativeweb.security/BotTraps.cs`, already rendered by `Chrome`.
- **The public site's route shape** is `MapGet(... ) => HandlerAsync(...)` with a declared return
  type, `.RequireRateLimiting(NativeWebPipeline.GeneralRateLimitPolicy)` and `.AsPublicPage(...)`
  (`PublicSite.cs:44-56`). The comment there explains why a lambda body will not compile.
- **The forms tier is incomplete**: only `tests/nativeweb.forms.tests.unit` exists, though
  `AGENTS.md` names `forms` as a unit/integration/e2e suite. Two new projects are needed.
  `tests/nativeweb.permissions.tests.e2e` is the model for an e2e tier that publishes the
  NativeAOT sample and drives a browser.

## Deliverables

### 1. A file-valued form field (R1–R5)

- **`libs/nativeweb.forms/FormFile.cs`** — a small readonly record carrying `Name`,
  `ContentType`, `Length` and a `OpenReadStream()`, so a bound model does not expose
  `IFormFile` and a handler never touches `HttpContext`. Optional fields are `FormFile?`.
- **`libs/nativeweb.forms/FormAttributes.cs`** — `[FormFile]` carrying `MaxBytes` and
  `Accept` (a content-type list). Declared on the constructor parameter, alongside `[FormField]`.
- **`libs/nativeweb.forms/FormBinding.cs`** — `RequireMultipart(HttpContext)` (415 when a model
  with a file field is posted urlencoded — R3), `File(form, name, limits)` and
  `RequiredFile(...)`, and a `FormLimits` install that sets a per-endpoint `FormFeature` with
  `FormOptions.MultipartBodyLengthLimit` before the read. That limit is enforced *during*
  `ReadFormAsync`, which is what makes R4's early refusal real rather than claimed.
- **`libs/nativeweb.forms.generator/`** — `FormModel` accepts `FormFile`/`FormFile?` as a member
  type; the emitter emits `RequireMultipart` and the file helper; `FormEmitter` installs the
  limits before `ReadFormAsync`. **`NWF006`** is the new diagnostic for a file field the binder
  cannot honour (a non-nullable `FormFile` with no required semantics available, a `[FormFile]`
  on a `string?` member, a `MaxBytes` of zero or negative, an empty `Accept` entry).
- Size overrun surfaces as `InvalidDataException` from `ReadFormAsync`; the binder catches it and
  throws `BadHttpRequestException(413)`, which `ErrorPageMiddleware` already surfaces at its own
  status — the same route `RequireFormContentType`'s 415 takes.

**One requirement needs amending, and I am not able to meet it as written.** R4 says a request
exceeding *either* the size limit or the content-type set is refused "before the body is read to
completion". Size is: `MultipartBodyLengthLimit` aborts the read mid-stream. **Content type is
not** — `IFormCollection` only exposes a part's content type once the whole form has been read,
so refusing earlier needs a hand-written `MultipartReader` loop in place of `ReadFormAsync`,
which is a different and much larger design. The plan checks content type after the read and
**before any byte is written to disk**, and I will amend R4 to say so rather than leave a criterion
the code does not meet. Flagging it here because it is a real narrowing of the spec.

### 2. Carried answers between requests (R6, R7)

**`libs/nativeweb.forms/CarriedAnswers.cs`** — over `IDataProtectionProvider`:

- `Protect(IReadOnlyDictionary<string,string>, TimeSpan lifetime)` → one opaque string for a
  hidden field, via `CreateProtector("NativeWeb.Forms.CarriedAnswers").ToTimeLimitedDataProtector()`.
- `TryUnprotect(string, out answers, out CarriedAnswersFailure)` distinguishing **tampered** from
  **expired**, because R7 wants a different message for each.
- Payload is length-prefixed key/value pairs, not JSON — no reflection, nothing for the AOT
  analyzer to complain about.
- Registered by `AddNativeWebForms()` (or the existing forms registration, whichever exists),
  singleton, resolved from the container by the sample's handlers.

### 3. A mail seam (R8, R9)

**`libs/nativeweb.mail/IEmailTransport.cs`** — the interface `SmtpTransport` already satisfies,
extracted so a host can substitute it and the sample's contact endpoint is testable without SMTP.
No behaviour change to `SmtpTransport`.

### 4. The three sample forms (R8–R16)

`apps/sample/PublicForms.cs` plus `templates/forms/` following the `PublicSite.cs` route shape
(declared return type, rate limiting, `AsPublicPage`), reachable from the site nav (R16):

| Form | Routes | Behaviour |
| --- | --- | --- |
| Contact | `GET /contact`, `MapFormPost /contact` | Bot traps + antiforgery, then `IEmailTransport`. An unconfigured or unreachable SMTP host logs at warning and answers the visitor a "could not send" page, never a 500 (R9) |
| Upload | `GET /upload`, `MapFormPost /upload` | One `FormFile`, 1 MB, an image content-type set. Stored under the configured directory under a server-generated name; the client's name is metadata only and never a path (R13). Served back by `GET /uploads/{id}` where `{id}` is the generated name, matched against a strict pattern |
| Two-step | `GET /survey`, `MapFormPost /survey/step-2`, `MapFormPost /survey` | Step one's answers ride in one `CarriedAnswers` hidden field with a 20-minute lifetime. Step two submits both. A tampered or expired value answers 400 with "start again". Re-submitting step two sends a second mail; the page says so rather than pretending it is idempotent (R15) |

**`libs/nativeweb.web/NativeWebOptions.cs`** gains `Uploads:Directory` (R12), through the same
`Key("...")` + `TryFromConfiguration` path as the eleven existing keys. Default: an `uploads`
directory beside the data-protection keys. Failure mode: **absent is created at startup;
unwritable fails startup**, stated in the source comment the way each existing key states its own.

### 5. Test tiers (R17)

- **`tests/nativeweb.forms.tests.unit`** (exists) — `CarriedAnswers` round-trip, tamper, expiry;
  the generator's `NWF006` cases compiled and asserted, the way `PermissionGatingTests` does.
- **`tests/nativeweb.forms.tests.integration`** (new) — the binder through a real
  `WebApplicationFactory`: a multipart post binds, a urlencoded post is 415, an oversize post is
  413, a wrong content type is a client error naming what is accepted, a missing required file
  matches a missing required text field.
- **`tests/nativeweb.forms.tests.e2e`** (new, modelled on `nativeweb.permissions.tests.e2e`) — a
  browser completes each of the three forms against the published NativeAOT sample.
- **`tests/nativeweb.sample.tests.integration`** — the path-traversal fact (R13) and the
  upload-directory failure modes (R12).

### 6. Documentation and record (R18)

`libs/nativeweb.forms/README.md` (the file field, its limits, `NWF006`, `CarriedAnswers`),
`CHANGELOG.md` under Unreleased, `work/status/current.md`, spec → `Status: done`.

## Risks

- **Path traversal (R13)** is the one place this spec touches attacker-controlled input that
  becomes a filesystem path. The mitigation is that the client's name is never used: the stored
  name is server-generated and the serve route matches a strict pattern before touching disk. A
  test posts `../../etc/passwd` and asserts nothing was written outside the directory.
- **AOT.** `FormFile`, `CarriedAnswers` and the new generator output all have to stay trim-clean;
  `TreatWarningsAsErrors` is on repo-wide, so a violation is a build failure rather than a
  surprise. The length-prefixed payload exists to avoid a reflection-based serializer.
- **The e2e tier costs a NativeAOT publish** (~31 minutes cold, seconds cached, measured on the
  permissions tier). Budget for one cold publish in the verification pass.
- **`GeneralRateLimitPolicy` on a public POST** could refuse a legitimate resubmission during
  e2e. The permissions e2e tier does not hit this; watch for it.

## Coverage check

R1–R5 → §1. R6, R7 → §2. R8, R9 → §3 and §4. R10, R11, R13–R16 → §4. R12 → §4 (`NativeWebOptions`).
R17 → §5. R18 → §6. **R4 is partly amended** — see the flagged narrowing in §1.

## Verification

- `export JauntyQUsePackages=true` for every command, per this repo.
- Each new tier green, then the falsification pass: break each new guard in turn, confirm red,
  `git checkout --` **after committing** (an uncommitted revert goes back to the parent branch).
- `dotnet test tests/nativeweb.forms.tests.e2e` for the browser runs, and the AOT tier for the
  publish warnings.
- `deno run -A ~/.claude/tools/structure-audit.js` — 0 errors, after the first task, not at the end.
- Full `/code-review` (two-lens) on the branch before merge, then `--no-ff` into `dev`, unpushed.

Branch: continue on `feat/027-sample-forms-examples` (already carries the spec). On approval this
is recorded as `027-plan.md`, derived into `027-tasks.md`, and built to merge.
