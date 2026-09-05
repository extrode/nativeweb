# 011 — The Roslyn floor moves to 5.3.0

Date: 2026-09-05
Status: accepted
Supersedes: `2026-09-05-010-roslyn-pin-floor.md`, whose condition 2 fired the same day.

## Decision

**`Microsoft.CodeAnalysis.CSharp` moves from 4.12.0 to 5.3.0** for the four generator projects
under `libs/`. `Microsoft.CodeAnalysis.Analyzers` stays at 5.9.0 and remains exempt from both
bounds, for the reasons 010 gives.

5.3.0, not 5.9.0. The ceiling is unchanged: our SDK 10.0.203 runs Roslyn `5.3.0-2.26219.105`, and
a generator built against 5.9.0 is refused by our own compiler with `CS9057`. "Latest" here means
5.3.0 until the SDK moves.

## Why now

010 named two triggers and said raise the pin on either. The second one fired.

`justsmtp.com` was the one consumer of the four published analyzer packages whose `global.json`
carried no `sdk` section. On 2026-09-05 it merged `chore/pin-dotnet-10-sdk`
(`2bd3cc7` on its `dev`, `7d64c00` on its 013 branch), giving it:

```json
"sdk": { "version": "10.0.100", "rollForward": "latestFeature" }
```

`latestFeature` reaches the installed 10.0.203 feature band and excludes every 9.x. That repository
break-checked the file rather than assuming it: with the block temporarily set to 9.0.311 and
`rollForward: disable`, `dotnet --version` reported 9.0.311; reverted, 10.0.203.

The rest of the consumer set was re-checked here before the pin moved, since 010 rested on it:

| repository | consumes the generators? | `global.json` `sdk` |
|---|---|---|
| `justsmtp.com` | yes, package pins | **10.0.100 / latestFeature** |
| `epass.io` | no — `ProjectReference` to `nativeweb.render` and `nativeweb.security` only, neither of which references a generator project | none |
| `ghosthive.com` | no | no `global.json` |
| `jauntyq` | no | 8.0.100 / latestMajor |

So `justsmtp.com` was the whole floor, and it is pinned.

## Two things 010 got wrong about that consumer

Both came back from `justsmtp.com` and both are corrections to this repository's record, not to its.

1. **"That consumer does not build today."** False. Its `NativeWeb.*` `0.1.0` pins are inert on the
   default path: its `Directory.Build.props:54-55` probes `..\nativeweb` and resolves the framework
   by project reference, and the package pins apply only under `-p:NativeWebUsePackages=true`.
   Measured there on 2026-09-05: 0 warnings, 0 errors, **2,271 tests pass, 0 failed, 0 skipped**,
   3m34s across eight tiers. The middle row of 010's table was therefore breakage against a green
   repository, which made the floor **more** worth having while it stood, not less.
2. **The floor was already being cleared by luck.** With three SDKs installed (9.0.311, 9.0.313,
   10.0.203) and no `sdk` section, `dotnet` takes the highest, so 5.3.0 would have worked there
   today anyway. The pin converts that from luck to a guarantee, which is the only form a floor in
   another repository can be relied on in.

Neither correction changes 010's mechanism, and neither is a reason the pin should have moved
earlier: a floor cleared by SDK-resolution luck is not cleared.

## What this buys, honestly

**Nothing today.** No generator here calls a 5.x compiler API — true by construction, since all
four compiled against 4.12.0 until this change. What it buys is the option: a 5.x API can now be
used without a second round of consumer coordination.

## What was verified here

Pin bumped, `project.assets.json` for `libs/nativeweb.render.generator` confirming
`Microsoft.CodeAnalysis.CSharp/5.3.0` resolved rather than a cached 4.12.0, then every tier that
consumes a generator, at its recorded baseline:

| tier | tests |
|---|---|
| render unit | 219 |
| forms unit | 148 |
| scaffold unit | 268 |
| permissions unit | 151 |
| sample unit | 231 |
| repo unit | 183 |

All passed, 0 failed, 0 skipped. `apps/sample` builds with **0 warnings** under repo-wide
`TreatWarningsAsErrors`, which is the assertion that matters: a `CS9057` from any of the four would
have been an error naming the assembly.

## What would change this again

Unchanged in shape from 010, one version up:

1. A generator here needs an API only 5.9.0+ offers **and** this repository's SDK ships a compiler
   that will load it. Today it does not.
2. A **new** consumer of the four packages appears without an `sdk` section pinning .NET 10 or
   later. The floor is a property of the consumer set, so it has to be re-checked whenever that set
   grows — the table above is the check.

**An SDK upgrade here is still not a reason to raise the pin.** It lifts the ceiling only.
