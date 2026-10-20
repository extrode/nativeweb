# 009 — Response formats and negotiation live in the framework

Date: 2026-09-02
Status: accepted
Supersedes: the "Any content type (R12)" stance in `../01-architecture/progressive-enhancement.md`
as it read before this date
Spec: `../specs/021-response-formats-and-negotiation/021-spec.md`

## Decision

**A NativeWeb application answers text, Markdown, JSON, XML, Atom, RSS, CSV and a sitemap through
one typed helper each, and offers several representations of one route through one mapping call.**
The helpers are `FormatResults` in `libs/nativeweb.render`; the mapping call is `MapNegotiated` in
`libs/nativeweb.web`, whose chooser the public-page output cache shares.

The architecture document used to say the opposite: any media type is a `Results.Text` with a
content type, "no framework machinery, no content-negotiation layer". That line is withdrawn.

## Why

- **The raw primitives were the wrong default for four of these formats.** JSON through
  `Results.Json` reflects and fails to trim under NativeAOT; CSV needs RFC 4180 quoting and a
  formula guard nobody adds by hand twice the same way; Atom, RSS and a sitemap each have a
  schema and a date format that a string builder gets wrong silently. Each product would have
  rebuilt the same five things, and the first one that rebuilt them was this repo's own sample.
- **Negotiation touches the cache, and the cache is the framework's.** A route that answers HTML
  or JSON depending on `Accept` is either keyed on the chosen representation or it serves one
  client's JSON to another's browser. That key cannot be attached by an application after the
  fact; the policy computes it, so the policy has to make the same choice the endpoint makes,
  from the same pure chooser. A negotiation layer outside the framework would have had to
  reimplement the cache policy to be safe.
- **The Django line holds.** Nothing here composes a product flow. Serialising a value, quoting
  a field and picking a representation are the same kind of primitive as HTML encoding, which
  has been the framework's since the first template.

## What stays outside

- **What a sitemap lists and what a robots file disallows** are the application's: the framework
  owns no route inventory. The sample's `PublicDocuments.cs` is the model, not a library call.
- **Streaming bodies and binary.** Every helper buffers, declares `Content-Length` and writes
  once, for the measured reason in the render README. A body that cannot know its length, or is
  bytes, stays on `Results.Stream` and `Results.Bytes`.
- **Escaping opt-outs.** `FormatResults.Xml` hands the caller an `XmlWriter`, and `WriteRaw` on it
  is the one way to emit unescaped XML, the same way `Html.Raw` is for markup.

## Consequences

- Extension and `Accept` are the two negotiation inputs, in that order; there is no `?format=`
  query parameter, because a query key is a cache-key allowlist entry and a link-rot vector.
- An extensionless negotiated route says `Vary: Accept` on every response, wildcard and absent
  header included, so a shared cache never serves one representation to a client that asked for
  another.
- `AsPublicPage` now accepts any `IEndpointConventionBuilder`, so a negotiated pair, a group and
  a single route are declared public the same way.
