# 020 — Tasks
Plan: ./020-plan.md

- [x] T1 Passphrase and routes — files: `apps/sample/AdminSignIn.cs`, `apps/sample/templates/signin/form.html`, `apps/sample/Program.cs`, `tests/nativeweb.sample.tests.unit/AdminPassphraseTests.cs` — covers: R1 R2 R3 R4 R5 R6 R7 — done when: the unit tests pass and fail when the comparison is broken, and the existing integration suite passes unchanged
- [x] T2 Integration proof — files: `tests/nativeweb.scaffold.tests.integration/AdminSignInTests.cs` — covers: R1 R2 R3 R4 R5 R7 — done when: every acceptance line but R6 and R8 has an assertion, and each fails against a broken route
- [x] T3 E2e proof — files: `tests/nativeweb.scaffold.tests.e2e/SampleProcessFixture.cs`, `tests/nativeweb.scaffold.tests.e2e/AdminSignInTests.cs` — covers: R1 R2 R3 — done when: a Production-environment sample signs in through the browser and lists Articles
- [x] T4 Deploy contract — files: `scripts/deploy/provision.sh`, `scripts/deploy/deploy.sh` — covers: R8 — done when: the seeded stanza and the preflight refusal both exist and `bash -n` passes on both scripts
- [x] T5 Records — files: `docs/02-reference/configuration.md`, `tests/nativeweb.repo.tests.unit/DocsClaimsTests.cs` if parity needs it, `work/todo.md`, `work/status/current.md`, `CHANGELOG.md`, `020-spec.md` status — covers: R9 — done when: repo-facts tests pass and `Status: done`
