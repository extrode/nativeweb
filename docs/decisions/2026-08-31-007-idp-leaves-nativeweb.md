# 007 — The identity provider leaves NativeWeb

Date: 2026-08-31
Status: accepted

## Decision

NativeWeb is a framework. It stops containing an identity provider. `apps/web`,
`libs/nativeweb.auth`, `libs/nativeweb.data`, `libs/nativeweb.signin.epass*`, `db/` and the
seven test projects that cover them are removed from this repository. **epass.io is the real
epass**; the copy that lived here is a dead fork and is not ported anywhere.

## Why

`AGENTS.md` opened with "NativeWeb is ... both a reusable reference template **and** the full
ground-up redesign of epass.io." That dual charter is where the auth code came from, and it
stopped being true once epass.io became its own repository with its own IdP. Nobody updated the
sentence, so the code kept growing against it.

The 2026-08-19 N4 neutral-naming rename made it worse rather than better: `libs/epass.auth`
became `libs/nativeweb.auth` without one line of it becoming framework code. A library holding
`TokenIssuer`, `SilentConsent`, `RealmResolver`, `SsoModes`, `WebAuthnPolicy` and
`ClientAuthenticator` is an identity provider's domain model. Django ships `contrib.auth` —
users, passwords, sessions, permissions — and leaves the OAuth2/OIDC authorization server to
`django-oauth-toolkit`. This repository had crossed that line and was calling the far side
"framework".

`libs/nativeweb.data` is the same story one layer down: it has no `.cs` files of its own, it is
generated entirely from `db/queries/**/*.sql` and `db/schema/*.schema.json`, and its own csproj
describes it as "JauntyQ-generated data access for **the shared identity schema**". A framework
does not ship a database schema of users, realms and refresh tokens.

## What was here, and what epass.io has instead

Both repositories descend from one IdP and both moved on. This section records the divergence so
the removal is a decision with evidence under it rather than a deletion.

| | this repository | epass.io |
| --- | --- | --- |
| shape | one server-rendered app, `apps/web` | `apps/api` (59 `.cs`) + `apps/web-native` (19 `.cs`) |
| auth library | `libs/nativeweb.auth`, 24 files | `libs/epass.auth`, 26 files |
| schema | `db/`, 55 migrations | `db/`, 50 migrations |
| last change | 2026-08-31 | 2026-08-30 |

The two auth libraries are near-identical modulo namespace: `SilentConsent` and `LockoutPolicy`
differ by 4 diff lines, `OAuthScopes` by 8, `TokenIssuer` by 44, `RealmResolver` by 43.

**Nine migrations existed only here** and are not in epass.io:

```
audit_log                      first_party_application
bff_store_session_id_hashed    login_attempts_code_window
bff_store_user_id              users_last_breach_check
bff_store_user_id_backfill     users_locale
bff_store_user_id_created_at_idx
```

**Four existed only in epass.io:** `channel_bindings`, `reverse_auth_challenges`,
`sms_send_attempts`, `temp_codes_channel`.

**One source file existed only here:** `libs/nativeweb.auth/SessionToken.cs`. Three existed only
in epass.io: `Masking.cs`, `Pkce.cs`, `SecurityNoticeSending.cs` — the first two live in
`libs/nativeweb.security` here rather than being absent.

**Eighteen server-rendered pages existed only here**, with no counterpart in epass.io's
`apps/web-native`: the admin page, the OAuth consent screen, sign-in, register and its verify
step, passphrase change, passphrase reset and its verify step, settings, TOTP enrol / verify /
recovery-codes, and WebAuthn enrol / verify.

None of it is ported. It stays reachable in this repository's history at the commit before the
removal, and this table is the index for anyone who later wants to cherry-pick from it.

## What NativeWeb keeps

`libs/nativeweb.render`, `.render.generator`, `.forms`, `.forms.generator`, `.scaffold`,
`.scaffold.generator`, `.assets`, `.mail`, `apps/sample`, `skeleton/`, `tools/` and the docs.
No source file in any of them referenced epass.

`libs/nativeweb.security` splits. `Sha256Hex`, `OpaqueTokens`, `BotTraps`, `ContactProtector`,
`Masking`, `Passphrases` and `StoreDataProtector` are framework primitives and stay. `Pkce` and
`PairwiseSubject` are OAuth and OIDC specifics and go.

## Consequences

- **`apps/sample` becomes the reference application.** It is what the e2e suite drives and what
  `tools/bench/` measures; the R16 baseline in spec 012 was recorded against `apps/web` and has
  to be re-recorded, so its committed figures are superseded by this change, not by an upgrade.
- **The spec 011 deployment toolchain targeted `apps/web`.** It retargets or it stops being
  exercised; either way T15 was never run on a real host, so nothing in production moves.
- **Specs 002, 006, 007, 008, 009 describe work on code that is no longer here.** They are
  historical records and are not rewritten. This decision is the pointer that explains why their
  paths no longer resolve.
- `docs/architecture/001-relying-party-extraction.md` says "the identity provider is `apps/web`
  in this same repository". That is now false and the doc carries a note saying so.
