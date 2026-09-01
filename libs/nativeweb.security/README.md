# nativeweb.security

Security primitives for NativeWeb: passphrase hashing, opaque bearer tokens, at-rest
encryption, blind indexes, display masking and form bot traps. No generator, no product:
every type here is a mechanism more than one product would otherwise re-implement badly.

## The whole API

| Type | Member | Effect |
| --- | --- | --- |
| `Passphrases` | `Hash` | Argon2id; PHC string starting `$argon2id$`, fits `hashed_passphrase VARCHAR(255)` |
| | `Verify` | Constant-time check against a stored PHC string; a malformed hash returns false, not a throw |
| | `NeedsRehash` | True when the stored hash carries weaker parameters than today's |
| `OpaqueTokens` | `Generate` | 32 bytes of CSPRNG output as unpadded base64url, 43 characters |
| | `Hash` | SHA-256 of the token, 64-char lowercase hex for the `token_hash` column (`CHAR(64)`) |
| `Sha256Hex` | `OfUtf8` | The codebase's single at-rest SHA-256 routine, 64-char lowercase hex |
| `StoreDataProtector` | ctor | AES-256-GCM over a 32-byte key; `allowLegacyPlaintext` opts in to passthrough |
| | `DeriveKey` | 32-byte key from a configured secret: base64/hex of 32 bytes used verbatim, anything else SHA-256'd |
| | `Protect` | `enc:v1:` + base64(nonce, ciphertext, tag) |
| | `Unprotect` | Reverses `Protect`; a value without `Prefix` throws unless `allowLegacyPlaintext` |
| `ContactProtector` | ctor | Derives independent `enc` and `index` subkeys from a 32-byte master key |
| | `DeriveKey` | Same decoding rule as `StoreDataProtector.DeriveKey` |
| | `Protect` / `Unprotect` | Encryption for a contact value that must round-trip in plaintext |
| | `EmailIndex` | Blind index, lowercased and trimmed first, so equality is case-insensitive |
| | `PhoneIndex` | Blind index, trimmed only, for values expected normalised on the way in |
| `Masking` | `Email` | `alice@example.test` -> `a•••@e•••.test`; the TLD stays intact |
| | `Phone` | `+15550000002` -> `+1 (•••) •••-0002`; last 4 digits stay visible |
| | `IpAddress` | `203.0.113.42` -> `203.0.113.x`, `2001:db8::1` -> `2001:db8::x` |
| `BotTraps` | `IssueCanary` | The signed render-timestamp canary for a form rendered now |
| | `Fields` | The full trap block as markup |
| | `Inspect` | Judges a submitted `IFormCollection`: honeypots first, then the canary |
| | `LogTripped` | One structured event line per tripped submission |

### Which hash, when

| Input | Routine | Reason |
| --- | --- | --- |
| A human-chosen passphrase | `Passphrases.Hash` | Low entropy, so a memory-hard KDF: Argon2id at the OWASP Password Storage Cheat Sheet (2026) settings, m = 19456 KiB, t = 2, p = 1 |
| A machine-generated 256-bit token | `OpaqueTokens.Hash` | Uniform CSPRNG entropy leaves nothing to brute-force, so a slow KDF would add cost without benefit |
| Anything else hashed at rest | `Sha256Hex.OfUtf8` | One shared `CHAR(64)` hex convention, defined in one place |

`Passphrases.NeedsRehash` is the migration path: verify against the stored string, and if
it asks for a re-hash, run `Hash` again on next successful login. `Verify` treats a
truncated or malformed stored PHC string as a mismatch rather than an exception; Geralt
rejects those in two different ways and both are caught.

### Encryption and the blind index

A contact value must round-trip in plaintext (a code or link is sent to it, and it is
displayed masked), so it is encrypted, not hashed. Encryption is non-deterministic: a
fresh nonce per write means the ciphertext cannot back an equality lookup or a UNIQUE
constraint. Those move to a blind index, a keyed HMAC of the normalised value:
deterministic, non-reversible, stored beside the ciphertext and queried instead of the
plaintext.

`ContactProtector` derives two independent subkeys from one configured secret so the
encryption key and the index key are never the same value. Both `EmailIndex` and
`PhoneIndex` bind a scope id into the HMAC, so the same address under two scopes produces
different indexes and a keyless database dump cannot link one person across tenants. A
product with no tenancy passes a constant.

**This library reads no configuration.** `DeriveKey` takes the secret as a string and the
product decides where it comes from and what to call it, under its own configuration section.
Comments in `ContactProtector.cs` still name a `NativeWeb:ContactKey` key: that prefix belonged
to an application that left on 2026-08-31, nothing binds it, and it is a stale comment rather
than a setting to configure.

`StoreDataProtector.Unprotect` refuses a value without the versioned prefix: in a store
this type has always protected, the only ways to reach that branch are a column that was
never encrypted and a value that has been replaced, and returning either verbatim reports
it as success. The `allowLegacyPlaintext` passthrough exists for a product carrying rows
that predate its own encryption rollout; it is that product's decision, not this
library's default.

### Bot traps

Four honeypot fields (`contact_website`, `fax_number`, `company_website`,
`phone_extension`), named to tempt autofill bots rather than to describe themselves, plus
`form_session`, a signed render-timestamp canary. `MinAge` (default 3 s) and `MaxAge`
(default 24 h) are tuned through `BotTrapOptions`, resolved from the request services
when registered and defaulted when not. Template-based apps mirror the `Fields` markup in
their shared/bot-traps partial. The offscreen pair is hidden by the `bt-extra`
stylesheet class rather than inline style or type=hidden, because the classic regression
is a sanitizer or CSS change quietly un-hiding them.

When `Inspect` returns a tripped `BotTrapVerdict`, the caller takes the endpoint's
normal-outcome path with no side effects, after logging through `LogTripped`. `Reason` is
recorded for tuning and never shown. The verdict type offers `BotTrapVerdict.Clean` for
the pass and `BotTrapVerdict.Trip` with a reason for the fail.

Known bypass, accepted by design: the canary is bound to the app, not to a session, path
or single use, so one canary harvested from any form GET satisfies the timing check on
every form until `MaxAge` runs out. The mechanism targets bots that fetch and post
atomically, the overwhelming spam pattern; a bot tooled up to harvest and age canaries
has graduated past what honeypot fields catch either.

## A worked example

```csharp
using NativeWeb.Security;

// A passphrase: hash it once, verify it forever, re-hash when the parameters age
string phc = Passphrases.Hash("correct horse battery staple");
bool ok = Passphrases.Verify(phc, "correct horse battery staple");
if (ok && Passphrases.NeedsRehash(phc))
{
    phc = Passphrases.Hash("correct horse battery staple");
}

// An opaque token: plaintext goes to the client once, only the hash is persisted
string token = OpaqueTokens.Generate();
string tokenHash = OpaqueTokens.Hash(token);

// Contact PII: ciphertext for round-trip, blind index for lookup and uniqueness
byte[] master = ContactProtector.DeriveKey("the secret your product configured");
var contact = new ContactProtector(master);
string ciphertext = contact.Protect("alice@example.test");
string index = contact.EmailIndex(0, "Alice@Example.Test");   // same address, same index

// Display, without the value
string shown = Masking.Email("alice@example.test");

// At-rest encryption for material that must round-trip but is not contact PII
var store = new StoreDataProtector(StoreDataProtector.DeriveKey("a configured secret"));
string stored = store.Protect("a live token or in-flight value");
string roundTripped = store.Unprotect(stored);
```

Bot traps live in the request pipeline, not in a standalone block: call `BotTraps.Fields`
when rendering a form, then `BotTraps.Inspect` and `BotTraps.LogTripped` on the POST
before any side effects.

## What this library does not do

The line this repository holds, on the Django model: Django ships contrib.auth and
leaves the OAuth2/OIDC server to a third-party package. Hashing, opaque tokens, bot traps
and a data protector are framework primitives and belong here. A token issuer, consent
screen, realm resolver, SSO mode, client registry or identity schema does not belong here
and never will. An identity provider was removed from this repository on 2026-08-31 for
that reason, and this library is small on purpose: a candidate type that serves one
product's sign-in flow rather than any consumer of the primitive has crossed the line,
and the answer is removal, not accommodation.

Two further boundaries. There is no generator, unlike `libs/nativeweb.scaffold`: nothing
here is build-time. And `BotTraps` is not a proof of humanity; it is a spam filter whose
known bypass is stated above, and rate limiting remains the host's job.

## Proved by

`tests/nativeweb.security.tests.unit`.
