# 007 — Session tokens must not be recoverable from the database
Status: done
Created: 2026-08-16

## Problem

The IdP hands every signed-in user a `__Host-NativeWeb.Session` cookie whose value **is** the
primary key of the `bff_store` row backing that session, written to Postgres verbatim
(`apps/web/Auth/SessionAuth.cs:62-68`). The token is unguessable — it is a random v4 GUID, and
`bff_store` is a point lookup on that id — but it is not *secret at rest*. Anyone who can read
the table holds live bearer credentials for up to fourteen days: a nightly backup, a read
replica, a logical-replication slot, a support engineer running a `SELECT`, a slow-query log
that captured the parameter, or a read-only SQL-injection primitive anywhere in the app. None of
those is a compromise of the authentication system today, and all of them become a full session
takeover.

This is the standard failure the rest of the codebase already avoids. One-time codes are stored
as `OneTimeCodes.Hash(code)` and compared in fixed time; passphrases are Argon2id; the pending
registration payload is `storeProtector.Protect(...)`; and the sibling product (manzil.my, from
the same template) stores only the SHA-256 of its own session token. The IdP's session is the
one credential in the system held in the clear, and it is the highest-value one.

It has never mattered because the IdP carries no real traffic. It stops being free the moment
epass.io cuts over, because from then on the plaintext is in every backup that has ever been
taken, and fixing it afterwards means invalidating live sessions rather than empty ones.

## Goals

- No value ever transmitted to a client is recoverable by reading the database, its backups, or
  its replication stream.
- Session lookup stays a single indexed point lookup — no measurable added latency on the
  authentication path, which runs on every authenticated request.
- The migration is safe to run against a populated table.
- One obvious way to do it for the next credential: a future contributor storing a
  client-held token in `bff_store` should find hashing to be the path of least resistance,
  not an extra step to remember. *(Downgraded from "correct by construction" after review —
  see R4.)*

## Non-goals

- **Encrypting `bff_store.data`.** Session rows carry only a `UserId`, and the one sensitive
  payload (the pending-registration passphrase hash) is already `IDataProtection`-protected.
  Out of scope.
- **The registration, reset, TOTP and authorize flow rows.** Their ids are *not* bearer tokens:
  they are stored server-side in `temp_codes.unique_id` and reached via the username, while the
  credential the user actually holds is an emailed one-time code that is already hashed. They
  may be covered incidentally if the chosen design applies uniformly to the table — that is a
  `/plan` decision, not a requirement here.
- **Rotating session tokens on privilege change**, session pinning, or any change to session
  lifetime. Separate concerns.
- **The skeleton.** It ships no auth or session store; nothing to mirror.
- **manzil.my.** Already stores only a hash of its token.

## User stories

- As an operator, I want a database backup to be worthless as a set of credentials, so that
  handling a dump — restoring it to staging, shipping it to support, losing it — is a data
  incident and not an account-takeover incident.
- As a security reviewer, I want every client-held credential in the system hashed at rest, so
  that "is anything stored in the clear?" has one answer rather than one exception.
- As a signed-in user, I want the change to be invisible: my session keeps working and requests
  stay as fast as they were.

## Requirements

- R1 The value stored in the database for a session MUST NOT be sufficient to authenticate.
  Presenting the stored value as a cookie MUST NOT sign anyone in.
- R2 The token minted into the cookie MUST retain at least 120 bits of entropy from a
  cryptographically secure source. *(Corrected from 128 after review: a v4 GUID fixes six bits
  for version and variant, so `Guid.NewGuid` yields 122, not 128. The bound is what matters and
  122 clears it by a wide margin; the original figure was simply wrong.)*
- R3 Session lookup MUST remain a single indexed point lookup on the stored value. No table
  scan, no per-row computation, no second round trip.
- R4 The derivation from cookie value to stored value MUST be a single choke point that every
  read, write and delete path goes through. **Partially met, and the gap is recorded rather than
  papered over:** the type does not hand out the plaintext as a `Guid`, but
  `Guid.Parse(CookieValue)` recovers it in one hop, so a new write path could still bypass the
  derivation and would compile. Fully meeting this needs the data layer's session key to be its
  own type instead of `Guid`, which is a JauntyQ-wide change and out of this spec's scope; it is
  in `work/todo.md`.
- R5 Sign-out, "revoke all other sessions", "revoke all sessions" and the active-session list
  (`SettingsEndpoints`, `PassphraseEndpoints`) MUST behave exactly as they do today.
- R6 The migration MUST convert existing rows in place. Sessions live at migration time stay
  live; no forced global sign-out. *(Inferred, not instructed: no production traffic exists yet,
  so preserving costs nothing and is the behaviour a populated deployment would need. If the
  intent is instead to burn every token that was ever stored in plaintext, this requirement
  inverts and the migration deletes the session rows.)*
- R7 The migration MUST be reversible in the sense that it does not destroy the ability to serve
  traffic if rolled back to the previous binary — or, if it is not, that MUST be stated in the
  migration's own header comment so an operator reads it before running it.
- R8 A test MUST fail if a future change writes a session's cookie value to the database in
  recoverable form. It MUST assert against what is actually in the table, not against the
  intermediate helper.
- R9 Coverage at all three tiers per the project's testing rule: unit for the derivation,
  integration for the full sign-in → authenticated-request → sign-out cycle against real
  PostgreSQL, e2e for a browser session surviving the flow.
- R10 The AOT publish MUST stay warning-free and within the existing size budget.

## Acceptance criteria

- **R1** Given a signed-in user, when the `bff_store` row for their session is read, then no
  column contains the cookie value; and when that stored value is presented as the session
  cookie, the request is answered as unauthenticated.
- **R2** Given a minted session, when the cookie value is inspected, then it carries ≥120 bits
  of CSPRNG entropy.
- **R3** Given the session lookup query, when its plan is considered, then it resolves by the
  primary key or a unique index on the stored value, with one round trip.
- **R4** *(Not met — see the requirement.)* Every session read, write and delete path in the app
  goes through `SessionToken`, and no path parses the session cookie as a `Guid` any more. A
  *new* bypassing path is not prevented mechanically; that is a known, recorded gap.
- **R5** Given a user with three active sessions, when they revoke all others, then two rows are
  deleted, the current session still authenticates, and the settings list shows one entry.
- **R6** Given a populated `bff_store`, when the migration runs, then every pre-existing session
  cookie still authenticates and every row's stored value satisfies R1.
- **R8** Given the assertion in R8, when the hashing step is removed from the write path, then
  that test fails. *(Verified by perturbation, per the project's testing rule — a green new test
  is not evidence until it has been made to fail.)*
- **R9** All three suites green; unit ≥ current 374, integration ≥ 247, e2e ≥ 59.
- **R10** `dotnet publish apps/web -c Release` exits 0 with zero warnings, within budget.

## Open questions

None blocking. R6 records the one inference made rather than leaving it implicit; reverse it in
`/plan` if the intent was a clean burn.
