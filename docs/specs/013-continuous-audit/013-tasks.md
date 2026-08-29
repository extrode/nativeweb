# 013 — Tasks
Plan: ./013-plan.md

Build order is dependency order. T1–T3 are the substrate every later task writes against; T12 is
the only task a person other than an agent must run.

- [ ] T1 Ledger: identity, append-only transitions, `rejected` absorbing — files: `tools/audit/ledger.ts`, `tools/audit/ledger.test.ts` — covers: R7, R8, R6 — done when: a finding whose file gains a line above it hashes identically; a `rejected` finding cannot transition; a finding missing `what`/`why`/`confirm`/`line` is dropped and counted.
- [ ] T2 Redaction: secret patterns applied before write, plus a corpus assertion — files: `tools/audit/redact.ts`, `tools/audit/redact.test.ts` — covers: R13 — done when: a finding carrying a key, token, or connection string is written with the value replaced and the location kept; the corpus assertion fails on a planted secret.
- [ ] T3 Slices: enumeration and least-recently-audited selection — files: `tools/audit/slices.ts`, `tools/audit/slices.test.ts` — covers: R4, R5 — done when: `never` sorts before any date; the coverage table lists every source directory; selection over a table with one `never` returns that one.
- [ ] T4 Mechanical checks, first three rules — files: `tools/audit/checks/{stale-todo,doc-names-missing-file,assertionless-test}.ts` and their tests — covers: R6 — done when: each rule finds its planted defect in a fixture and finds nothing in a clean fixture.
- [ ] T5 Mechanical checks, spec-traceability rule — files: `tools/audit/checks/unevidenced-requirement.ts` + test — covers: R6 — done when: a requirement marked met in a `docs/specs/*-spec.md` with no test file referencing it is reported, and one with a reference is not.
- [ ] T6 Reading pass: bounded GLM call over a slice — files: `tools/audit/reading-pass.ts`, `tools/audit/model.ts` + tests against a stubbed transport — covers: R3, R11 — done when: the slice bound ends the run with outcome `bounded`; token and cost counts are recorded; no network call happens in the test.
- [ ] T7 Refutation pass and verification ranking — files: `tools/audit/reading-pass.ts` (extend) + test — covers: R10 — done when: a refuted candidate never reaches the ledger, the refuted count is recorded, and an unevaluable candidate is admitted as `unverified`.
- [ ] T8 Run entry point: clone, select, check, verify, write, delete — files: `tools/audit/run.ts` + test — covers: R1, R3, R11, R12 — done when: the temp clone is gone whether the run succeeds or throws; a thrown run still writes a run record with outcome `failed` and its error.
- [ ] T9 Digest: new-since-last-triage, coverage, health, cost trend — files: `tools/audit/digest.ts` + test — covers: R9, R11, R12 — done when: two runs with no triage between them produce the same digest twice; a date with no run record is reported as a gap; the trend spans the recent runs.
- [ ] T10 Triage: apply decisions, refuse `accepted` without the user token — files: `tools/audit/triage.ts` + test — covers: R8, R14 — done when: `accepted` without the token exits non-zero and writes nothing; every transition records a date and a non-empty reason.
- [ ] T11 Sync script: mirror push to audit-host — files: `scripts/audit/sync-to-audit-host.sh` — covers: R1 — done when: bare invocation reports what it would push and touches nothing; `--execute` pushes to the audit-host mirror and never to `origin`; running it twice is a no-op the second time.
- [ ] T12 Provisioning script for audit-host — files: `scripts/audit/provision-audit-host.sh` — covers: R2, R15 — done when: bare invocation prints every step and changes nothing; `--execute` installs Deno, creates the directory layout, loads the launchd agent, and fails loudly if any credential beyond the GLM key is present. **Run by the user on audit-host, not by an agent.**
- [ ] T13 Operator documentation — files: `docs/02-reference/continuous-audit.md`, `README.md` (one pointer) — covers: all — done when: a reader who has never seen this can provision, read a digest, triage, and stop the schedule, without opening a `.ts` file.
- [ ] T14 Acceptance review and records — files: `013-spec.md`, `work/status/*`, `work/tasklist.md`, `CHANGELOG.md`, `scripts/cleanup/013-continuous-audit.ps1` — covers: gate — done when: every R is walked against what was built, R2 and R15 are marked pending the operator run of T12, and `Status` reflects the truth rather than the intent.

## Analyze — requirement to task

| R | Tasks | R | Tasks |
| --- | --- | --- | --- |
| R1 | T8, T11 | R9 | T9 |
| R2 | T12 | R10 | T7 |
| R3 | T6, T8 | R11 | T6, T8, T9 |
| R4 | T3 | R12 | T8, T9 |
| R5 | T3 | R13 | T2 |
| R6 | T1, T4, T5 | R14 | T10 |
| R7 | T1 | R15 | T12 |
| R8 | T1, T10 | | |

No orphan requirements and no task without a plan section. **R2 and R15 cannot be met by an
agent** — they are properties of the audit host after the user runs T12, and the gate check in
T14 must record them as pending rather than met, the same way spec 011's T15 is outstanding.
