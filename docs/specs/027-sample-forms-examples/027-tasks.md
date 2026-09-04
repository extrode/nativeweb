# 027 — Tasks

Plan: ./027-plan.md

- [x] T1 The bound file type — files: `libs/nativeweb.forms/FormFile.cs` — covers: R1 — done when:
      a readonly type carries `Name`, `ContentType`, `Length` and `OpenReadStream()`, a handler can
      read a file through it without touching `HttpContext`, and nothing on it exposes `IFormFile`.
- [x] T2 The declaration — files: `libs/nativeweb.forms/FormAttributes.cs` — covers: R4 — done
      when: `[FormFile]` carries `MaxBytes` and `Accept`, both documented with their defaults and
      what a request violating each is answered.
- [x] T3 The binder helpers — files: `libs/nativeweb.forms/FormBinding.cs` — covers: R1, R2, R3,
      R4 — done when: `RequireMultipart` throws 415 for a urlencoded post, the per-endpoint
      `FormOptions.MultipartBodyLengthLimit` is installed before the read, an oversize read becomes
      a 413 rather than an `InvalidDataException`, a content type outside `Accept` is a client
      error naming what is accepted, and a missing required file matches a missing required text
      field.
- [x] T4 The binder helpers' facts — files: `tests/nativeweb.forms.tests.unit/FormFileBindingTests.cs` —
      covers: R1, R2, R3, R4 — done when: each of T3's five outcomes is asserted against a real
      `HttpContext`, including a `FormFile?` binding null when no file is posted.
- [x] T5 The generator accepts a file field — files: `libs/nativeweb.forms.generator/FormModel.cs`,
      `libs/nativeweb.forms.generator/FormEmitter.cs` — covers: R1, R5 — done when: `FormFile` and
      `FormFile?` are bindable member types alongside `string?`, the emitted binder calls the file
      helpers with the declared limits, `RequireMultipart` is emitted only for a model that has a
      file field, and NWF003's message still names `string?` for a member that is neither.
- [x] T6 NWF006 — files: `libs/nativeweb.forms.generator/FormModel.cs` — covers: R5 — done when: a
      `[FormFile]` on a non-file member, a `MaxBytes` of zero or less, and an empty or whitespace
      `Accept` entry each fail the build with NWF006, and the message names the declaration and the
      fix.
- [x] T7 The generator's facts — files: `tests/nativeweb.forms.tests.unit/FormFileGenerationTests.cs`
      — covers: R1, R5 — done when: a model with a file field compiles and its emitted binder is
      asserted, and each NWF006 case is compiled and asserted the way the existing NWF tests are.
- [x] T8 Carried answers — files: `libs/nativeweb.forms/CarriedAnswers.cs` — covers: R6, R7 — done
      when: a set of answers round-trips through one opaque string over
      `IDataProtectionProvider.ToTimeLimitedDataProtector()`, a tampered value and an expired value
      are distinguishable failures, and the payload uses no reflection-based serializer.
- [x] T9 Carried answers' facts — files: `tests/nativeweb.forms.tests.unit/CarriedAnswersTests.cs` —
      covers: R6, R7 — done when: round-trip, one-byte tamper, expiry, an empty set, and a value
      containing the payload's own delimiters are each asserted.
- [x] T10 Registration — files: `libs/nativeweb.forms/FormsServiceCollectionExtensions.cs` —
      covers: R6 — done when: `CarriedAnswers` resolves from the container of a host that called
      the framework's existing registration, and a host that did not gets a stated failure rather
      than a null.
- [x] T11 The mail seam — files: `libs/nativeweb.mail/IEmailTransport.cs`,
      `libs/nativeweb.mail/SmtpTransport.cs` — covers: R8, R9 — done when: `SmtpTransport`
      implements the interface with no behaviour change, and a host can substitute a transport.
- [x] T12 The upload directory is configuration — files: `libs/nativeweb.web/NativeWebOptions.cs` —
      covers: R12 — done when: `Uploads:Directory` is read through the same `Key(...)` path as the
      existing keys, has a stated default, is created when absent, and fails startup when
      unwritable, with the failure mode in the source comment as every other key has.
- [x] T13 The configuration fact — files: `tests/nativeweb.web.tests.unit/` — covers: R12 — done
      when: default, configured value, and the unwritable case are asserted.
- [x] T14 The forms integration tier — files: `tests/nativeweb.forms.tests.integration/` — covers:
      R1, R2, R3, R4, R17 — done when: the project exists, builds, and drives a real
      `WebApplicationFactory` host: a multipart post binds, a urlencoded post is 415, an oversize
      post is 413, a rejected content type names what is accepted, and a missing required file is
      refused.
- [x] T15 The contact form — files: `apps/sample/PublicForms.cs`,
      `apps/sample/templates/forms/contact.*` — covers: R8, R9, R10, R16 — done when: `GET
      /contact` renders behind bot traps and antiforgery, a valid post sends through
      `IEmailTransport` and confirms, and an unconfigured or unreachable transport logs at warning
      and answers a page rather than a 500.
- [x] T16 The upload form — files: `apps/sample/PublicForms.cs`, `apps/sample/UploadStore.cs`,
      `apps/sample/templates/forms/upload.*` — covers: R11, R13, R16 — done when: one file is
      accepted, stored under a server-generated name in the configured directory, shown back to the
      visitor, and served by a route whose id is matched against a strict pattern before any path
      is built.
- [x] T17 The two-step form — files: `apps/sample/PublicForms.cs`,
      `apps/sample/templates/forms/survey.*` — covers: R14, R15, R16 — done when: step one's
      answers ride in one carried hidden field, step two submits both, a tampered or expired value
      answers 400 and says to start again, and re-submitting step two leaves the stated condition.
- [x] T18 The sample's facts — files: `tests/nativeweb.sample.tests.integration/` — covers: R9,
      R10, R12, R13, R15 — done when: the traversal name writes nothing outside the directory, a
      refusing transport is not a 500, a tripped bot trap is refused, a tampered carried value is
      refused, and the double-submit condition is asserted.
- [x] T19 The forms e2e tier — files: `tests/nativeweb.forms.tests.e2e/` — covers: R17 — done when:
      the project exists on the model of `tests/nativeweb.permissions.tests.e2e`, and a browser
      completes all three forms against the published NativeAOT sample.
- [x] T20 Documentation and record — files: `libs/nativeweb.forms/README.md`, `CHANGELOG.md`,
      `work/status/current.md` — covers: R18 — done when: the README documents the file field, its
      limits, NWF006 and `CarriedAnswers`; the changelog names all of it under Unreleased; and the
      spec's acceptance criteria are re-read against what was built before `Status: done`.
