# 018 — Tasks

Plan: [./018-plan.md](./018-plan.md)

- [ ] T1 Docs staleness test — files: `tests/nativeweb.repo.tests.unit/DocsClaimsTests.cs` — covers: R4, R10 — done when: every backticked repo path and every relative link in the documented set resolves, the set is discovered rather than listed by hand, and breaking a path in a document turns the suite red.
- [ ] T2 `nativeweb.web` README — files: `libs/nativeweb.web/README.md` — covers: R1, R2, R3 — done when: the five capabilities `epass.io` hand-ported are each named with the call that provides them, the `UseNativeWebPipeline`/`FromConfiguration` pair is shown in a runnable example, and T1 passes over it.
- [ ] T3 `nativeweb.render` README — files: `libs/nativeweb.render/README.md` — covers: R1, R2 — done when: `Html`, `TemplateWriter`, `TemplateResults` and locale negotiation are covered, the `.generator` gets its section, and T1 passes over it.
- [ ] T4 `nativeweb.forms` README — files: `libs/nativeweb.forms/README.md` — covers: R1, R2 — done when: `[Form]`, the generated `BindFormAsync`, `MapForm*` and `RequiresAntiforgery` are covered with the NWF010 build error explained, and T1 passes over it.
- [ ] T5 `nativeweb.security` README — files: `libs/nativeweb.security/README.md` — covers: R1, R2 — done when: passphrases, opaque tokens, bot traps, masking, `Sha256Hex`, `StoreDataProtector` and `ContactProtector` are covered, the Django-line boundary is stated, and T1 passes over it.
- [ ] T6 `nativeweb.mail` README — files: `libs/nativeweb.mail/README.md` — covers: R1, R2 — done when: `SmtpTransport`, `SmtpOptions`, `EmailTemplates` and `IEmailContentRenderer` are covered, and T1 passes over it.
- [ ] T7 `nativeweb.signin.testing` README — files: `libs/nativeweb.signin.testing/README.md` — covers: R1, R2 — done when: a consumer can run `LoginFlowStoreConformance` and `SessionStoreConformance` against its own store from the README alone, and T1 passes over it.
- [ ] T8 Configuration reference rewrite — files: `docs/02-reference/configuration.md` — covers: R7 — done when: every key documented is one `NativeWebOptions.TryFromConfiguration` builds or a named root-level key, each row carries a default and a failure mode, the `apps/web` sections and `CanonicalBaseUrl` are gone, and the deploy-time `app.env`/`secrets.env` contract is described.
- [ ] T9 Config-key parity test — files: `tests/nativeweb.repo.tests.unit/DocsClaimsTests.cs` — covers: R8 — done when: the `Key("...")` literals are extracted from `NativeWebOptions.cs` at run time, each appears in the reference, each row the reference declares is a real key, and adding a fake key to either side turns the suite red.
- [ ] T10 Adoption guide — files: `docs/00-quick-start/adopting-in-an-existing-app.md` — covers: R5, R6 — done when: an existing ASP.NET Core app can follow it end to end, it names the referencing model that works today and says why package mode does not, and it lists what the adopter can delete and must keep.
- [ ] T11 Adoption guide proved by running it — files: `tmp/adopt-probe/` (scratch, not committed) — covers: R5 — done when: every command in T10 has been run against a real minimal app, that app serves a NativeWeb-rendered page, and any step that did not work as written is corrected in the guide.
- [ ] T12 Navigation and docs site — files: `docs/README.md`, `docs-site/` — covers: R9 — done when: all eight documents are linked from `docs/README.md`, `scripts/build-docs.sh` has regenerated the site, and `tools/docs-site/check.ts` is green.
- [ ] T13 Record corrections — files: `work/todo.md`, `work/status/current.md`, `CHANGELOG.md` — covers: — — done when: the `SecurityContact` claim is corrected, G6 and G7 are struck as closed, the dead `ForwardLimit` line in `provision.sh` is noted, and the change is in `CHANGELOG.md` under Unreleased.
- [ ] T14 Gate check — files: `docs/specs/018-library-docs-and-adoption/018-spec.md` — covers: all — done when: each acceptance criterion is re-read against what was built and `Status:` is set to `done`, or the unmet criterion is named.

## Analyze — requirement to task

| R# | Tasks | |
| --- | --- | --- |
| R1 six READMEs | T2–T7 | covered |
| R2 required sections | T2–T7 | covered |
| R3 `nativeweb.web` first, five capabilities | T2 | covered |
| R4 named symbols and paths exist | T1, plus the pre-merge two-lens review | covered |
| R5 adoption document | T10, T11 | covered |
| R6 today's referencing model | T10 | covered |
| R7 configuration rewritten | T8 | covered |
| R8 two-way key parity | T9 | covered |
| R9 reachable and in the site | T12 | covered |
| R10 fails when stale | T1, T9 | covered |

No orphan tasks: T13 and T14 are the flow's own record and gate steps, which every Durable spec
carries and which advance no single requirement. No uncovered requirement.
