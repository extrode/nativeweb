# Continuous audit

A nightly, read-only pass over one slice of this repository, run on a machine that is not the
workstation, producing findings a session triages by hand. Spec `docs/specs/013-continuous-audit/`.

You should be able to provision it, read a digest, act on a finding and stop the schedule without
opening a `.ts` file. That is what this page is for.

## What it does, once a night

1. The workstation has pushed a mirror of the repository to the audit host.
2. At 03:07 (the hour is `--hour`, default 3) the audit host clones that mirror into a temporary directory.
3. It picks the **least recently audited slice** — a directory that directly contains source —
   and reads only that. Slices never audited come first.
4. It runs six mechanical checks over the slice, then, if the slice is under 120 KB and a GLM key
   is present, one bounded reading pass.
5. Every candidate finding is put to a second model prompted to **refute** it. Refuted findings
   are dropped and counted; findings that cannot be evaluated are admitted as `unverified`.
6. What survives is redacted, then appended to the ledger. The clone is deleted.

A run only marks the slice as read when it read it through. A slice over the byte bound, or a
reading pass the model cut short at its token limit, leaves the slice where it was in the
rotation, so it comes round again rather than being recorded as covered.

It writes nothing to this repository, ever. There is no flag that makes it.

## Why it cannot damage anything

| Guarantee | How, structurally |
| --- | --- |
| The audit cannot modify the repository | It works on a throwaway clone in a temp directory, deleted whether the run succeeds or throws |
| The audit cannot reach `origin` | The audit host holds no credential for it; the repository reaches the host by push, not pull |
| A secret in the source cannot leak into a report | 13 redaction rules run before anything is written, and every written artefact is re-scanned afterwards; a non-zero `corpusLeaks` is a defect in the audit itself |
| A finding you dismissed cannot come back | `rejected` is an absorbing state, on write and on read |
| A quiet week cannot look like a clean repository | A day with no run record is reported as a gap, above the findings — and the gap window runs to today, so a scheduler that died a month ago shows a month of gaps rather than an unbroken history |
| An unreachable audit host cannot look like a clean repository | The digest refuses to render (exit 4) rather than reporting an empty ledger it could not read |

## Provisioning, once

Run **on the audit host, by you**. It installs software and loads a scheduler, which is not an
agent's call.

```bash
scp scripts/audit/provision-audit-host.sh audit-host.local:~/
ssh audit-host.local
./provision-audit-host.sh                 # dry run: prints every step, changes nothing
./provision-audit-host.sh --execute       # installs Deno to ~/.deno, creates ~/audit, loads launchd
```

It refuses with exit 3 if the host holds a production credential — `~/.aws/credentials`,
`~/.pgpass`, a gh token and so on — before installing anything, so a refusal leaves the machine
as it found it. `--accept-credential-risk` proceeds anyway and says so.

The one secret the host needs is the GLM key in `~/.zai`. Without it the reading pass is skipped
and the mechanical checks still run.

Then, from the workstation:

```bash
scripts/audit/sync-to-audit-host.sh                # dry run: what it would mirror
scripts/audit/sync-to-audit-host.sh --execute      # mirror every branch to the audit host
ssh audit-host.local 'git clone -q ~/audit/mirror/nativeweb.git ~/audit/nativeweb'
```

Re-run `sync-to-audit-host.sh --execute` whenever you want the audit to see newer work. It pushes to the
audit host and never to `origin`; `origin` is yours.

## Reading a digest

From the workstation. This reads over ssh and writes nothing on the audit host.

```bash
deno run -A tools/audit/digest.ts --ssh audit-host.local
deno run -A tools/audit/digest.ts --ssh audit-host.local --json > tmp/digest.json
```

If the host cannot be reached it exits 4 and prints why, rather than rendering an empty digest —
"there are no findings" and "the store could not be read" are not the same statement.

The report opens with health, deliberately: if the scheduler is dead, nothing below that line
means what it appears to mean. Then coverage, then untriaged findings with verified ones first,
each carrying the file, the line, what, why, and a command to confirm it.

Running it twice without triaging shows the same list. That is the intended behaviour — the list
is "everything not yet decided", not "everything since yesterday".

## Triaging

A finding is worth acting on, or it is not, and either way that is a decision someone makes.

```bash
# Dismiss it. Absorbing: it will never appear in a digest again.
deno run -A tools/audit/triage.ts --ssh audit-host.local \
  --id a1b2c3d4e5f6 --state rejected --reason "cross-repo note, not this repository"

# Agree to act on it. Requires a recorded approver.
deno run -A tools/audit/triage.ts --ssh audit-host.local \
  --id a1b2c3d4e5f6 --state accepted --reason "broken cross-reference" --approved-by <name>

# Record that it is done.
deno run -A tools/audit/triage.ts --ssh audit-host.local \
  --id a1b2c3d4e5f6 --state fixed --reason "landed in <commit>"
```

`--id` takes any unambiguous prefix of at least six characters. An ambiguous one is an error, not
a guess.

**On `--approved-by`.** It is a record, not an enforcement boundary: anything with a shell can
type a flag. What holds is structural — nothing in the audit path calls `triage.ts`. The audit
writes findings and never decides about them. `--approved-by` exists so the ledger says who
agreed and when, rather than the tooling assuming you did.

Rejecting needs no approver, because declining to act is reversible with one more line while
accepting is what leads to a branch and a diff.

## Stopping it

```bash
ssh audit-host.local "launchctl bootout gui/\$(id -u)/com.extrode.nativeweb.audit"
```

The ledger, coverage and run records stay under `~/audit`. Re-run the provisioner to start again.

To pause for one night, do nothing: the digest will report the gap, which is the point.

## Where things live

| What | Where |
| --- | --- |
| The ledger of record | `audit-host:~/audit/ledger/nativeweb.jsonl` — append-only JSONL |
| Slice coverage | `audit-host:~/audit/ledger/nativeweb-coverage.json` |
| One record per run | `audit-host:~/audit/runs/<runId>.json` |
| launchd stdout/stderr | `audit-host:~/audit/runs/launchd.{out,err}.log` |
| The repository mirror | `audit-host:~/audit/mirror/nativeweb.git` |
| The tooling checkout | `audit-host:~/audit/nativeweb` |

A finding's identity is a hash of its path, rule and normalised excerpt — **not** its line number,
so editing the code above a defect does not resurrect it as a new finding.

## The checks

| Rule | Fires when |
| --- | --- |
| `stale-todo` | A TODO is both old (by blame) and unattributed — no owner, issue, spec, requirement, date or URL |
| `doc-names-missing-file` | A Markdown document names a repository path that no tracked file matches, under any reading |
| `assertionless-test` | An xUnit `[Fact]`/`[Theory]` or `Deno.test` body contains no assertion |
| `spec-requirement-without-criterion` | A spec requirement has no matching acceptance criterion |
| `spec-done-with-open-task` | A spec is marked done with unticked tasks |
| `spec-done-with-unmet-requirement` | A spec is marked done while its own text says a requirement is unmet |

Plus one reading pass per run, over the selected slice, whose findings go through the same
refutation and redaction as the mechanical ones.

## Running it by hand

```bash
cd tools/audit
deno test -A -q                                    # the suite
deno run -A run.ts --source ../.. --store ../../tmp/audit-smoke --no-model --slice docs/02-reference
```

`--no-model` runs the mechanical checks only, makes no network call, and costs nothing.

## Cost

The reading pass uses `glm-5.3` over the z.ai Anthropic-compatible endpoint, which draws z.ai plan
credits. Each run records its token counts, and the digest shows the trend, so drift is visible
before it is a bill. No dollar figure is claimed unless you supply a price with `--cost-in` and
`--cost-out` — a fabricated number would be believed.
