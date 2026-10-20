# 010 — The Roslyn pin is a compatibility floor, and it stays at 4.12.0

Date: 2026-09-05
Status: superseded by `2026-09-05-011-roslyn-pin-raised-to-5-3.md`
Supersedes: nothing. It writes down reasoning that lived only in a comment in
`../../Directory.Packages.props`, and adds the measurements that comment asserted.

## Decision

**`Microsoft.CodeAnalysis.CSharp` stays pinned at 4.12.0 for the four generator projects under
`libs/`, and `Microsoft.CodeAnalysis.Analyzers` moves freely.** They are not one decision.

The version the generators compile against is a **floor on the Roslyn every consumer of the four
published analyzer packages must be running**. It is not a statement about what this repository can
build.

## The rule, once

An analyzer or source generator is loaded into the compiler that is compiling the project
referencing it. If the generator references a **newer** `Microsoft.CodeAnalysis` than that
compiler, the compiler refuses to load it and reports `CS9057`. Older is always fine.

That single rule produces both bounds:

- a **ceiling** — what this repository can pin at all, set by our own SDK's compiler;
- a **floor** — the oldest SDK a consumer of the packages may use.

They are the same rule evaluated at two different compilers, which is why raising the pin to gain
headroom at one end costs range at the other.

## What was measured

Two throwaway projects: a `netstandard2.0` `IsRoslynComponent` generator emitting one type, and an
app consuming that type through `ProjectReference OutputItemType="Analyzer"`. Measured 2026-09-05.

SDK compilers on this machine:

| SDK | Roslyn |
|---|---|
| 10.0.203 | `5.3.0-2.26219.105` |
| 9.0.313, 9.0.311 | `4.14.0-3.26064.1` |

Generator pin against consumer SDK:

| generator built against | SDK 9.0.313 (Roslyn 4.14.0) | SDK 10.0.203 (Roslyn 5.3.0) |
|---|---|---|
| **4.12.0** | builds, generator runs | builds, generator runs |
| **5.3.0** | `CS9057` warning + `CS0103` in the consumer's own source | builds, generator runs |
| **5.9.0** | not measured | `CS9057` |

Restore-time bounds, quoted from the `NU1605` text rather than inferred:

| CSharp pin | drags in Analyzers |
|---|---|
| 4.12.0 | `>= 3.3.4` |
| 5.3.0 | `>= 5.3.0-2.25625.1` |

So the two lines can only ever move **up** together, never down.

## Why 4.12.0 and not 5.3.0, which also loads here

Because "internal framework, so everyone is on our SDK" is not true of the tree as it stands.

`justsmtp.com` pins `NativeWeb.Render.Generator` in its own `Directory.Packages.props`, and its
`global.json` carries **no `sdk` section at all** — only the test-runner opt-in. It therefore builds
on whatever `dotnet` resolves, and .NET 9 SDKs shipping Roslyn 4.14.0 are installed on this
machine. Against a 5.3.0 pin that consumer gets the worst of the three `CS9057` surfaces: a warning
it will not read, plus a page of `CS0103: The name '...' does not exist` pointing at its own code.

Two honest caveats, so this is not read as more settled than it is. **The first of them was
false**, and 011 records what `justsmtp.com` measured instead:

- **That consumer does not build today for an unrelated reason.** Its `NativeWeb.*` pins read
  `0.1.0`, which its own comment says "resolve to nothing" — the gap `PackageVersionTests` exists
  to catch. So the breakage described above is prospective, not current.
- **Nothing in this repository calls a 5.x compiler API.** That is true by construction, since
  these four compile against 4.12.0 at all. It means the floor is currently free, not that it is
  currently needed.

## What would change this

Raise the pin when **either** of these happens, and not otherwise:

1. A generator here needs an API that only 5.x offers. Then the floor is being bought, not paid.
2. Every consumer of the four packages pins an SDK whose Roslyn is at or above the new pin. For
   `justsmtp.com` that means adding an `sdk` section to its `global.json`, which is a change in
   that repository and not this one.

**An SDK upgrade here is not a reason.** Moving to a newer SDK only lifts the ceiling; it makes
4.12.0 more conservative, not more obsolete.

## Why `Microsoft.CodeAnalysis.Analyzers` is exempt

It is `PrivateAssets="all"`, build-time only, and never enters a consumer's dependency closure. It
is the RS* rule set an analyzer author is checked against, not a compiler API surface, so neither
the ceiling nor the floor applies. It sits at 5.9.0. Its only constraint is the restore-time floor
in the table above.

That distinction was got wrong first time round: both pins were held back together, and the second
one had no reason behind it.

## Where `CS9057` shows up, and why the third row is the point

`CS9057` is a **warning**, emitted by the *consuming* project's `csc` — never by the four generator
projects, which build clean at any pin. What it always means is that the generator does not run.
How that surfaces is the consumer's configuration:

| consumer | what they see |
|---|---|
| `TreatWarningsAsErrors` on (this repository) | `error CS9057`, naming the analyzer assembly |
| off, generated output unused | warning; build succeeds; generator inert |
| off, generated output consumed | warning, plus `CS0103`/`CS0246` for every generated symbol |

This repository only ever sees the first, because `Directory.Build.props` sets
`TreatWarningsAsErrors` repo-wide. A consumer of the packages gets whichever of the other two their
own settings produce, and the third is a debugging session pointed at the wrong file.
