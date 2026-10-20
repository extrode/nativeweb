# 003 — CSP inline-script hash scanning: HTML context, and a lockstep test that exists

Standard tier. Branch `fix/csp-hash-scan-context`. No spec: this is a defect in shipped code
(spec 006 R3) plus the missing verification that would have caught it.

## Problem

`libs/nativeweb.render.generator/InlineScriptHashes.cs` claims, in its own class comment:

> Nothing here can over-authorize — a hash is only ever computed over bytes taken verbatim from
> a StaticNode, so a body carrying any dynamic output is unhashable by construction.

That reasoning covers dynamic content and nothing else. It is false for **static** bytes that a
browser never treats as a script element, because `ScanStatic` looks for the literal `<script`
with no idea what HTML context it is in. Confirmed by probe, not by reading:

| Template | Emitted |
|---|---|
| `<!-- <script>alert('commented');</script> -->` | 1 hash |
| `<textarea><script>…</script></textarea>` + the same in `<title>` | 2 hashes |

Each is a `script-src` allowlist entry for a script the page does not ship. The commented-out
case is the realistic one: someone comments a script out during debugging, the page keeps
authorizing those exact bytes, and any injection point that can reproduce them gets execution
under a policy that was supposed to be the thing stopping it. `<div data-x="<script>…">` is the
same shape through an attribute value.

**Second, the backstop the file names does not exist.** The class comment says a partial-borne
script is not hashed and:

> The backstop is the consuming app's lockstep test, which recomputes hashes from the actually-
> served response rather than from this scan.

Nothing in either repo recomputes anything. `skeleton/tests/…/SmokeTests.cs:40` asserts the
generated constants appear in the CSP header — constant against header, both from the same
scan. If the scan frames a body wrongly, misses a script, or invents one, the constant and the
header agree, every test is green, and the browser blocks the page's script. That is exactly
how these two defects survived.

## Approach

**Scanner: recognise the contexts, drop rather than model them.** The file's stated doctrine is
that any shape it is not certain about is dropped, because a dropped hash is a blocked script
that the suite sees, while a wrong hash is a blocked script in production. Extend `Phase.Outside`
from a bare `IndexOf("<script")` to a minimal tag walk with three carried sub-states:

- `InComment` — `<!--` … `-->`. Also covers `<!DOCTYPE` and bogus comments, which end at `>`.
- `InRawText` — `script`, `style`, `textarea`, `title`, `xmp`, `iframe`, `noembed`, `noframes`,
  `noscript`, `plaintext`. Skip to the matching close tag.
- `InTag` — inside any other element's open tag, tracking quoted attribute values, so a `<script`
  inside an attribute value is not a tag.

Rejected: parsing with AngleSharp. It is a test-project package, not a generator one, and a
source generator cannot usefully take it; and the input is not parseable HTML anyway — it is a
node list with dynamic holes in the middle of tags and attribute values.

A conditional or loop met while the outside state is anything but `Normal` is skipped rather
than rescanned with a fresh state: `<!-- {{#if x}}<script>…{{/if}} -->` must not be hashed.
That can suppress a later legitimate hash, which is the drop-not-hash trade the file already
makes everywhere else.

**Verification: recompute, never compare constants to constants.** Two layers.

1. `tests/nativeweb.render.tests.unit` — an AngleSharp lockstep over every `Csp/*` template:
   render it, parse the DOM, hash every *executable* inline script the parser actually found,
   and assert set equality with `InlineScriptCspHashes`. Set equality, so it fails on a missing
   hash and on a spare one. AngleSharp is already referenced there and is an independent
   implementation of the framing this scanner approximates — which is the whole point.
2. `skeleton/tests/__product__.tests.integration` — the app-level one the class comment
   promises: fetch `/`, parse the served body, recompute, and assert the CSP header's
   `'sha256-…'` sources are exactly that set. This is the layer that catches a partial-borne
   script, which the generator-tier test cannot see.

## Changes by area

- `libs/nativeweb.render.generator/InlineScriptHashes.cs` — the outside-state walk; the class
  comment's over-authorize claim corrected to name what it does and does not cover.
- `tests/nativeweb.render.tests.unit/templates/Csp/{commented,rawtext,attribute}.html` — the
  three shapes, kept as fixtures rather than deleted with the probe.
- `tests/nativeweb.render.tests.unit/InlineScriptHashTests.cs` — the three regressions plus the
  AngleSharp lockstep.
- `skeleton/tests/__product__.tests.integration/SmokeTests.cs` — recompute from the response.

## Risks

- **Suppressing a real hash.** The new states can only ever remove hashes, never add them, so
  the failure mode is a blocked script — loud, and caught by both lockstep layers.
- **The skeleton is not built in this repo.** Its tests compile only once stamped, so the change
  there is verified the way spec 006 T10 verified its own: stamp a throwaway product and run it.

## Coverage check

| Defect | Covered by |
|---|---|
| Commented-out script hashed | `commented.html` regression + render lockstep |
| Raw-text-element script hashed | `rawtext.html` regression + render lockstep |
| Attribute-value script hashed | `attribute.html` regression + render lockstep |
| Scan disagrees with the served bytes | render lockstep (all templates) + skeleton app lockstep |
| Partial-borne script not hashed | skeleton app lockstep only — by design, the generator tier cannot see it |
