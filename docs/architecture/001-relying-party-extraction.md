# 001 — T1.0, the relying-party library, by example

What "extract the OIDC relying-party library" actually means, using the code that already exists.

The source is `justsmtp.com/apps/web/Auth/` — **1,329 lines across nine files**, working, tested,
and already shipping. T1.0 is not a design exercise. It is deciding, file by file, which of those
lines are *justsmtp's* and which are *every consumer's*, and moving the second set into
`libs/nativeweb.rp`.

Related: `docs/brainstorms/001-framework-direction.md` §7 (T1.0), §4 (the ring model).

---

## 1. What a consumer writes today

Every app that wants "sign in with epass.io" writes this, from scratch, today:

| File | Lines | What it is |
| --- | --- | --- |
| `AuthEndpoints.cs` | 396 | `/login` GET+POST, `/callback` GET, `/logout` POST |
| `EpassClient.cs` | 316 | PKCE, the three HTTP calls, outcome classification |
| `SessionCookie.cs` | 149 | Mint and verify a signed session ticket |
| `SessionAuth.cs` | 113 | Key loading, issue, clear, scheme constants |
| `LoginFlowStore.cs` | 110 | Persisted in-flight sign-ins, taken at most once |
| `SessionAuthenticationHandler.cs` | 85 | The ASP.NET `AuthenticationHandler` |
| `ReturnPath.cs` | 62 | Open-redirect sanitiser |
| `EpassDtos.cs` | 56 | Wire records + the `JsonSerializerContext` |
| `EpassOptions.cs` | 42 | Configuration binding |

Nine files of security-critical plumbing before the product has a single feature. Django ships
`contrib.auth`; Rails has devise; Laravel has Breeze. NativeWeb currently ships bot traps.

---

## 2. The three buckets

Read every one of those 1,329 lines and each falls into exactly one of three buckets. That
classification *is* T1.0; everything after it is mechanical.

### Bucket A — pure framework. Moves as-is.

Code with no justsmtp in it at all. `ReturnPath.Sanitise` is the clearest case:

```csharp
// justsmtp.com/apps/web/Auth/ReturnPath.cs
internal static class ReturnPath
{
    public const string Default = "/dashboard";
    public static string Sanitise(string? candidate) { ... }
}
```

Every relying party needs an open-redirect sanitiser, and every one that writes its own writes the
same twenty lines with a different bug in them. The only justsmtp-specific token is `"/dashboard"`,
which becomes a parameter:

```csharp
// libs/nativeweb.rp/ReturnPath.cs
public static string Sanitise(string? candidate, string fallback) { ... }
```

Same for `SessionCookie.Mint`/`TryRead`, the `SessionReadResult` enum, `SessionTicket`,
`EpassClient`'s PKCE helpers, and `SessionAuthenticationHandler`. **Roughly 600 lines** move with
edits no larger than that one.

### Bucket B — framework shape, consumer content. Becomes an interface.

`LoginFlowStore` is the model case. Its *contract* is universal — a flow is recorded when a link is
sent and taken **at most once** when the link is opened — and that single-use property is what
stops a replayed sign-in link from working. But its *implementation* is justsmtp's schema:

```csharp
// justsmtp: the guarantee lives in JauntyQ-generated SQL over justsmtp's own table
var taken = await scope.Queries.LoginFlows.ConsumeAsync(state, ct).ConfigureAwait(false);
```

The framework cannot own that table. It can own the contract, and the four-outcome result that
makes the contract usable — which justsmtp already got right, and which a consumer inventing it
fresh would flatten into a nullable:

```csharp
// libs/nativeweb.rp/ILoginFlowStore.cs
public enum FlowTake { Taken, Unknown, AlreadyUsed, Expired }

public readonly record struct LoginFlow(string Verifier, string ReturnTo);

public interface ILoginFlowStore
{
    Task StartAsync(string state, string verifier, string returnTo, CancellationToken ct);
    Task<(FlowTake Outcome, LoginFlow Flow)> TakeAsync(string state, CancellationToken ct);
    Task SweepAsync(CancellationToken ct);
}
```

Exactly the shape `IScaffoldStore<T>` takes in `libs/nativeweb.scaffold`, and for the same reason:
JauntyQ owns data access, so the framework owns the contract and never the query. justsmtp's
existing class becomes the reference implementation, ~30 lines shorter for having the types
supplied.

`SessionKey` is bucket B too — loading a key from configuration is universal; *which* configuration
section, and whether an ephemeral dev key is acceptable, is the consumer's call.

### Bucket C — genuinely justsmtp's. Stays put.

```csharp
// AuthEndpoints.cs:267 — "find or create the local account behind an epass subject"
private static async Task<long> ResolveUserAsync(...)
```

That is a `users` table, a `long` primary key, and a provisioning policy. The framework hands over
a verified `sub` and stops. Likewise `AssetUrls`, the login page's markup, and
`SessionAuthDefaults.CookieName = "__Host-justsmtp.session"`.

---

## 3. What the consumer writes afterwards

The target. Everything above collapses to configuration plus one interface implementation plus a
callback:

```csharp
builder.Services.AddNativeWebRelyingParty(builder.Configuration.GetSection("Epass"))
    .WithFlowStore<MyLoginFlowStore>()          // bucket B: the consumer's table
    .OnSignedIn(async (ctx, sub, email, ct) =>  // bucket C: the consumer's users
        await ctx.RequestServices
            .GetRequiredService<Accounts>()
            .ResolveAsync(sub, email, ct));

app.MapRelyingPartyEndpoints();                 // /login, /callback, /logout
```

**1,329 lines to roughly 8.** The nine files do not disappear; they move behind a seam, get one
set of tests instead of one per consumer, and get fixed once when they are wrong.

---

## 4. Four decisions the extraction forces

These are why T1.0 is a day of thinking and not an afternoon of `git mv`.

**The DTOs are epass's wire format, not a standard.** `LoginLinkStartRequest` is camelCase while
`TokenResponse` is RFC 6749 snake_case, and justsmtp's own comment says that asymmetry "is epass's
and is not ours to tidy". A library that hard-codes it is an *epass.io* client, not an OIDC one. It
is probably right to ship exactly that and name it honestly — `nativeweb.rp.epass` over a smaller
`nativeweb.rp` — rather than pretend to a generality nothing tests.

**`EpassResult<T>`'s three outcomes are the load-bearing design.** `Ok` / `Refused` /
`Unavailable` is what lets the login route degrade instead of throwing when the IdP is down, and
`IsConfigured` is what lets a box with no credentials still boot and serve the public site. Both
must survive the move, because both are the kind of thing a reimplementation drops.

**`error_description` must not reach a page.** justsmtp reads it into the log and never renders it,
because it is an upstream string and putting another system's prose in front of a customer is a
content-spoofing surface. The library has to make the safe thing the default; if the field is on a
public result type, someone will render it.

**PKCE and `Masking` come from ring 2 today.** justsmtp uses exactly four symbols out of the
28-file `nativeweb.auth` — `Pkce` (3 uses), `Masking` (2), `Passphrases` (1), `OpaqueTokens` (1).
Three of those four are OAuth-*client* primitives sitting in the *IdP* library. T1.0 is where that
gets split, which is why brainstorm §4's ring model is confirmed by measurement rather than by
argument.

---

## 5. Why this is the first ring-1 deliverable

Every named consumer needs it on day one, the reference implementation already exists and already
passes tests, and doing it forces the ring split that the rest of tier 1 depends on. It is also the
cheapest possible proof that the framework is a framework: a second consumer wiring up sign-in in
eight lines is a claim anyone can check.
