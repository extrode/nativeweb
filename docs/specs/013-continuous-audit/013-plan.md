# 013 — Plan
Spec: ./013-spec.md

## Decisions taken by the user, 2026-08-29

Four were taken before drafting, one during. They are recorded here rather than in the spec
because each is a HOW.

| Decision | Chosen | Rejected, and why it was worth rejecting |
| --- | --- | --- |
| Host | Provision audit-host | A Windows scheduled task needs no provisioning, but `work/todo.md` already records thirteen `nativeweb.scaffold.tests.e2e` facts failing at `00:15:00` because a review worker saturated the CPU. A nightly audit on the same box is that failure on a timer. |
| Scope | `nativeweb` only | 19 extrode repos, or all 66, before the report format is tuned means wading through noise to discover the format is wrong. |
| Cost pool | Free tier, mechanical first | Below. |
| Triage authority | User decides, session recommends | R14. |
| Transport | Push mirror, not GitHub | Below. |

## Approach

**A throwaway clone is the whole of R1.** The audit does not need discipline about not writing to
the repository; it needs to be somewhere writing is meaningless. Each run clones a bare mirror
into a temp directory on audit-host, reads it, and deletes it. If a step went berserk and rewrote every
file, the consequence would be a slightly slower `rm -rf`. Nothing in the audit path holds a
credential that can reach `origin`, and audit-host does not have one — which also means R1 does not
depend on the audit behaving.

**The repository reaches audit-host by push, not by pull.** The obvious design has audit-host clone from
GitHub, and it is wrong twice over. The end state of finished work in this repo is
merged-into-`dev`-and-unpushed, so GitHub is behind by design — `dev` is ten commits ahead of
`origin/dev` as this is written, and an audit of `origin/dev` would be auditing code the
maintainer finished with days ago. Second, audit-host's `known_hosts` entry for `github.com` is stale
(`REMOTE HOST IDENTIFICATION HAS CHANGED`, measured 2026-08-29), so a pull design starts by
fixing an unrelated thing. Instead the workstation pushes to a bare mirror at
`audit-host:~/audit/mirror/nativeweb.git`. The direction matters: the workstation can reach audit-host, audit-host
cannot reach the workstation, so the flow is one-way by topology.

**Mechanical checks first; a model only where a model is required.** Most of what a daily audit
finds needs no reasoning — a `TODO` older than N months, a suppressed warning, a test file with
no assertion, a public method with no caller, a documentation claim naming a file that no longer
exists, a `docs/specs/*` requirement marked met with no test referencing it. These are cheap,
deterministic, and produce zero false positives when written carefully. They run every night and
cost nothing. The reading pass — "here are 400 lines nobody has reopened in five months, what is
wrong with them" — is the part that needs a model, and it is bounded to one slice per run.

**The reading pass calls GLM directly over HTTPS, and audit-host gets no Claude CLI.** Global AGENTS.md
puts free pools first, and `glm-lite` draws no credits at all. More to the point, the worker
tooling lives under `~/.claude` on the workstation and is not this repo's to install elsewhere,
and `claude login` on a headless box is a browser flow that would have to be redone. A single
`fetch` to the GLM endpoint with a key from `audit-host:~/.zai` needs no CLI, no login, and no
subscription capacity — leaving the Claude subscription for the triage session, where the
judgement happens. The key on audit-host is the only secret it holds, satisfying R15.

**Identity by content hash, not by line number (R7).** A finding's identity is a SHA-256 of
`{path, rule-id, normalised code excerpt}`, where normalising strips leading whitespace and
collapses runs of spaces. Adding an import at the top of a file shifts every line below it and
must not resurrect fifty findings. The line number is stored for navigation and is not part of
the identity; when a re-read finds the same identity at a new line, the ledger updates the line
and reports nothing.

**The ledger is append-only JSONL.** Every state transition is a new line, never an edit. The
current state of a finding is the last line bearing its id. This makes the history of a decision
readable — a finding rejected in September and re-raised by a different rule in November is
visible as such — and makes a corrupt write cost one line rather than the file. `rejected` is
absorbing: no rule may transition out of it (R8).

**Coverage is a slice table, and slices are directories.** Every directory containing source is
a slice, with a `lastAuditedAt` and a `findingsAtLastAudit`. Selection is "least recently
audited, `never` sorting first" (R5). This gives R4 for free — the table *is* the coverage
answer — and makes "the audit has never looked at `libs/nativeweb.auth`" a fact rather than an
assumption.

**Verification is a second, adversarial pass (R10).** The same shape `/code-review` uses and for
the same measured reason: on this project a `glm-5.3` worker once reported a `ReferenceError`
that one command disproved. Each candidate finding is re-put to a fresh model call prompted to
*refute* it, defaulting to refuted when it cannot demonstrate the failure. Survivors are
`verified`. A candidate the refuter could not evaluate is `unverified` and ranks last. The
refuted count is carried into the digest, because "clean after refuting six" reads differently
from "clean".

## Tech / dependencies

Verified on the hosts, not assumed:

| Component | Where | State on 2026-08-29 |
| --- | --- | --- |
| macOS 14.5 (Darwin 23.5.0), arm64, 8 cores, 136 GiB free | audit-host.local | present |
| `git` 2.39.5 (Apple Git-154) | audit-host.local | present |
| `python3` 3.9.6 | audit-host.local | present, unused by this design |
| Deno | audit-host.local | **absent — provisioned by this spec** |
| Homebrew | audit-host.local | not on the non-interactive `PATH`; not required |
| `dotnet`, `claude`, `rg`, `node`, `pwsh` | audit-host.local | absent, and none required |
| PostgreSQL 18 (`homebrew.mxcl.postgresql@18.plist`) | audit-host.local | running, untouched by this spec |
| `launchd` user agent | audit-host.local | the scheduler; `crontab` is empty and stays so |

Deno is installed to `~/.deno` by its own shell installer, which needs no administrator rights.
Nothing else is installed. Per the project's Deno-never-Node rule this introduces no
`package.json`, no lockfile, no `node_modules` and no `npm` invocation; dependencies are Deno
standard-library imports by URL.

## Changes by area

| Path | What, and which requirement | |
| --- | --- | --- |
| `scripts/audit/provision-audit-host.sh` | Idempotent, run by the user on audit-host. Installs Deno, creates `~/audit/{mirror,ledger,runs,reports}`, writes and loads the launchd plist, and verifies the GLM key is readable. Prints what it would do and exits unless given `--execute`. | R2 |
| `scripts/audit/sync-to-audit-host.sh` | Run on the workstation. `git push --mirror` to `audit-host:~/audit/mirror/nativeweb.git`, creating the bare repository on first use. Never touches `origin`. | R1 |
| `tools/audit/run.ts` | The run entry point, executed on audit-host by launchd. Clones the mirror to a temp dir, picks the slice, runs the checks, verifies, writes the ledger and run record, deletes the clone. | R1, R3, R5, R11, R12 |
| `tools/audit/slices.ts` | Slice enumeration and least-recently-audited selection over the coverage table. | R4, R5 |
| `tools/audit/checks/*.ts` | One module per mechanical check, each exporting a rule id and a pure function over file contents. | R6 |
| `tools/audit/reading-pass.ts` | The bounded model call over the selected slice, and the adversarial refutation pass. | R3, R10 |
| `tools/audit/ledger.ts` | Append-only JSONL: identity hashing, dedup, state transitions, the `rejected` absorbing rule. | R7, R8 |
| `tools/audit/redact.ts` | The secret patterns, applied to every finding before it is written, and the assertion that runs over the whole corpus. | R13 |
| `tools/audit/digest.ts` | Run on the workstation. Pulls new ledger lines over ssh, renders the digest with coverage, health and cost. Read-only over ssh; writes nothing on audit-host. | R9, R11, R12 |
| `tools/audit/triage.ts` | Applies a session's triage decisions — and refuses to write `accepted` without an explicit user token on the command line. | R14 |
| `docs/02-reference/continuous-audit.md` | The operator document: what runs, when, how to read a digest, how to triage, how to stop it. | all |
| `work/todo.md` | Unchanged by this spec except for anything the build itself finds. | — |

## Data / interfaces

**Finding** (one JSONL line in `~/audit/ledger/nativeweb.jsonl`):

```
{ "id": "<sha256 of path + ruleId + normalised excerpt>",
  "ruleId": "stale-todo" | "doc-names-missing-file" | "reading-pass" | ...,
  "path": "libs/nativeweb.auth/Foo.cs", "line": 214,
  "what": "...", "why": "...", "confirm": "...",
  "verified": "verified" | "unverified" | "refuted",
  "state": "new" | "accepted" | "rejected" | "fixed",
  "at": "2026-08-30T03:00:11Z", "reason": "<required on every transition>",
  "runId": "2026-08-30" }
```

**Coverage row** (`~/audit/ledger/coverage.json`): `{ slice, lastAuditedAt | null, findingsAtLastAudit }`.

**Run record** (`~/audit/runs/<runId>.json`): `{ runId, startedAt, endedAt, slice, outcome:
"ok" | "bounded" | "failed", error?, candidates, refuted, admitted, tokensIn, tokensOut, costUsd }`.

The digest is Markdown on stdout. There is no service, no port, and no HTTP listener anywhere in
this design.

## Risks & mitigations

- **The audit becomes noise and the maintainer stops reading it.** The single largest risk, and
  the reason R7, R8 and R10 exist. Mitigated by dedup, by `rejected` being absorbing, and by
  refuting before reporting. If the first week's digests are mostly rejected, the answer is to
  delete rules, not to add filtering.
- **A model finding that is confidently wrong.** Measured on this project twice. R10's
  refutation pass, and the fact that nothing is acted on without the user, are the mitigation.
- **The mirror push leaks work to a second host.** audit-host gets a full copy of the repository,
  including unpushed branches. It is the maintainer's own machine on the local network; this is
  stated rather than mitigated, and it is why scope stopped at one repository.
- **A secret reaching a report.** R13's redaction runs before the write, and the assertion runs
  over the whole corpus so a pattern added later catches what was written earlier. The audit never
  quotes a matched value.
- **JauntyQ's license.** The audit reads a repository whose data layer is generated from a
  local-checkout-only dependency. Nothing in this design distributes source or binaries; the
  mirror is on the maintainer's own hardware and no report leaves it. No change to the
  redistribution position.
- **Silent death.** A launchd agent that fails to load looks identical to a quiet week. R12
  handles it by treating missing run records for a date as a reported gap, so the digest states
  absence rather than implying health.
- **Cost drift.** `glm-lite` draws no credits today; that could change. R11 records per-run cost
  and the digest shows the trend, so drift is visible before it is a bill.

## Coverage check

| R | Covered by |
| --- | --- |
| R1 | Throwaway clone + push-direction topology; `sync-to-audit-host.sh`, `run.ts` |
| R2 | `provision-audit-host.sh` launchd agent, `RunAtLoad` plus daily interval |
| R3 | `run.ts` slice bound, recorded as outcome `bounded` |
| R4 | `slices.ts` coverage table, surfaced by `digest.ts` |
| R5 | `slices.ts` least-recently-audited selection, `never` first |
| R6 | Check module contract; findings lacking a field are counted and dropped in `ledger.ts` |
| R7 | `ledger.ts` content-hash identity, line number excluded |
| R8 | `ledger.ts` append-only transitions, `rejected` absorbing |
| R9 | `digest.ts` |
| R10 | `reading-pass.ts` refutation pass; ranking in `digest.ts` |
| R11 | Run record fields; trend in `digest.ts` |
| R12 | `digest.ts` gap detection over `~/audit/runs/` |
| R13 | `redact.ts` at write time plus a corpus assertion |
| R14 | `triage.ts` refuses `accepted` without an explicit user token |
| R15 | `provision-audit-host.sh` installs one key and asserts no other credential is present |

No requirement is uncovered.
