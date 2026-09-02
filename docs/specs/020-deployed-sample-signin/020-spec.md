# 020 — A deployed sample whose admin can be reached
Status: done
Created: 2026-09-02

## Problem

`apps/sample` marks `Article` as admin-only, so the admin index hides it and `/admin/articles`
redirects to `/signin`. The only sign-in the sample has grants the admin role to anyone who asks,
and for that reason it exists only in the Development environment. The deployed sample at
`nw-dev.example.com` runs as Production, so its `/signin` is a 404, the admin index shows Tags
alone, and the half of the reference application that demonstrates authorization cannot be seen
where it is deployed. Nobody found this before 2026-09-02 because every test signs in through
the Development route.

## Goals

- An operator who knows a secret can sign in to the deployed sample's admin and see every
  scaffolded model, Articles included.
- Nobody else can, and a stranger guessing at the form gains nothing from volume.
- A Production build that has no secret refuses to start and says which key is missing, in the
  same way the framework refuses a missing data-protection directory.
- The Development experience, and every test that relies on it, is unchanged.

## Non-goals

- A framework primitive. A single shared passphrase guarding one product's admin is that
  product's decision; the framework's sign-in library is the OAuth relying party in
  `libs/nativeweb.signin`, and the skeleton already uses it.
- Users, roles beyond the existing `admin`, password reset, lockout state in the database, or
  any identity schema. The Django line in `AGENTS.md` holds.
- Changing the skeleton. A stamped product signs in through a real provider.
- Hashing the stored secret. `secrets.env` already holds the database credentials at mode 640,
  and a hash would need a tool to produce it; a later spec may add one.

## User stories

- As the operator of nw-dev, I want to type the passphrase from `secrets.env` into a form and
  land on `/admin` signed in, so that I can show and use the Articles admin on the deployed host.
- As a visitor to nw-dev, I want a wrong or missing passphrase to be refused without saying
  anything more than "not accepted", so that the form leaks nothing about the secret.
- As the operator deploying a new host, I want the service to fail at startup with the key name
  when I forgot the passphrase, so that I do not find out from a 404 a day later.

## Requirements

- **R1** Outside the Development environment, `GET /signin` renders a form with one passphrase
  field and the antiforgery token, and `POST /signin` compares the submitted value with the
  configured secret in constant time.
- **R2** A matching passphrase signs the visitor in with the same cookie scheme, cookie name and
  `admin` role the Development route grants today, then redirects to the `ReturnUrl` when it is a local path
  and to `/admin` otherwise.
- **R3** A non-matching or empty passphrase re-renders the form with one fixed message and a
  401 status, and the response does not differ between "empty", "wrong" and "wrong length".
- **R4** `POST /signin` is rate-limited per client address, and the limit is stricter than the
  framework's general policy.
- **R5** The secret is read from the product's configuration section under one documented key.
  In Production a missing or empty value fails startup with a message naming that key; in every
  other non-Development environment the form is served and every attempt is refused.
- **R6** In the Development environment, `GET /signin` keeps its present behaviour: it signs the
  visitor in without a form. Nothing in the three test projects that sign in that way changes.
- **R7** A signed-in visitor can sign out, and after signing out `/admin/articles` redirects to
  `/signin` again.
- **R8** The deploy provisioner seeds the key into `secrets.env` as a blank REQUIRED entry with
  a comment, in the same style as the connection string, and `deploy.sh` refuses to promote a
  host whose value is blank.
- **R9** `docs/02-reference/configuration.md` documents the key, its failure mode per
  environment, and where it lives on a deployed host.

## Acceptance criteria

- **R1/R2** — Given a Production host with the secret set, When the operator posts the right
  passphrase with a valid antiforgery token, Then the response is a 302 to `/admin` carrying the
  session cookie, and `/admin` then lists Articles and Tags.
- **R3** — Given the same host, When an empty passphrase, a wrong one of the same length and a
  wrong one of another length are posted, Then all three responses are 401 with byte-identical
  bodies apart from the antiforgery token.
- **R4** — Given a client that posts more times in a minute than the limit allows, Then the
  surplus attempts are answered 429 before the comparison runs.
- **R5** — Given a Production build with the key unset, When it starts, Then it exits non-zero
  and its output names the key. Given a Staging build with the key unset, Then `/signin` renders
  and the right passphrase is still refused.
- **R6** — Given the existing integration and e2e suites, When they run after this change, Then
  they pass unchanged.
- **R7** — Given a signed-in session, When the visitor posts the sign-out form, Then the cookie
  is cleared and `/admin/articles` answers 302 to `/signin`.
- **R8** — Given a fresh provision run, Then `secrets.env` contains the key blank under a
  REQUIRED comment; given a blank value, `deploy.sh --check` names it and exits non-zero.

## Open questions

None. Two decisions were taken with the user on 2026-09-02 and are recorded so the plan does not
reopen them: the secret is a passphrase in `secrets.env` rather than the demo OAuth provider
(which grants admin to anyone) or a documented dev-only admin; and the sample keeps its cookie
scheme rather than adopting the relying-party library, because the deployed sample has no
identity provider to talk to.
