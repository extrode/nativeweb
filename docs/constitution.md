# Constitution — NativeWeb

Binding project rules. These are the WON'T-change decisions. Agents and humans obey them;
changing one is a deliberate governance act, not a casual edit. This is NOT lessons-learned
(see docs/lessons/) and NOT mechanical conventions (see docs/conventions.md).

Status: draft · Last reviewed: <date>

## Stack constraints
- (e.g. "No React." / "No Node." / "NativeAOT — source-gen JSON only, no reflection.")

## Architecture invariants
- (e.g. "Every mutating flow is an HTML form action; app works with JS disabled.")

## Security / data
- (e.g. "AWS surface is SES+S3+SNS only." / rules for auth, input handling, data exposure)

## Testing
- (e.g. "Core is zero-dependency; tests may use Testcontainers.")

## Governance
- Supersedes ad-hoc practices. To change a rule: record the change + date + why, right here.
