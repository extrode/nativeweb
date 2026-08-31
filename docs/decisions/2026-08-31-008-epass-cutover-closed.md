# 008 — The epass.io cutover is closed as a NativeWeb concern

Date: 2026-08-31
Status: accepted
Supersedes the open half of: `../architecture/002-epass-cutover-capability-gap.md`
Companion: `2026-08-31-007-idp-leaves-nativeweb.md`

## Decision

**"The epass.io cutover" is no longer an item in this repository — not in flight, not queued, not
blocked.** It is finished work belonging to `../epass.io`, and this repo's only remaining
relationship to it is being an upstream two of its projects compile against.

The term is retired here. A future session finding it in an old document should read this file and
stop, rather than reopening it.

## What was measured, 2026-08-31

The check was run against the real sibling checkout at `C:\home\code\extrode.com\epass.io` (branch
`dev`, HEAD `b39e838`), not taken on report.

| Question | Measured answer |
|---|---|
| Is the port still waiting on NativeWeb? | **No.** epass.io built its own `apps/web-native` and has merged `/login`, TOTP, logout, e-mail-code login, `/register` + verify, `/reset`, the landing, `/authorize` with device enrolment, `/device`, passkey sign-in and device approve — each proven live against a real API. |
| What does epass.io consume from here? | Two projects, by `ProjectReference` through `$(NativeWebRoot)`: `libs/nativeweb.render` and `libs/nativeweb.security`. Nothing else. |
| Did the identity provider's departure break either? | **No.** The removal deleted `PairwiseSubject.cs` and `Pkce.cs` from `libs/nativeweb.security`, and epass.io uses neither — it has its own `libs/epass.security/PairwiseSubject.cs` and `libs/epass.auth` `Pkce`, and there is no `using NativeWeb.Security` anywhere in its `apps/` or `tests/`. |
| Did the public API of `libs/nativeweb.render` move under it? | **No.** Between epass.io's pinned `NATIVEWEB_REF` (`5b5833f`) and this repo's `dev`, the only deletions in `TemplateResults.cs` are comment lines and one internal local. |
| Can epass.io's CI fetch the ref it pins? | **Yes.** `5b5833f` is an ancestor of `origin/dev` and reachable on the remote today. |
| Is epass.io's red CI ours to fix? | **No.** Its three causes are a PAT that needs re-scoping to resource owner `extrode`, a stale `JAUNTYQ_REF`, and a `ci-samples` run nobody has triggered. All three are in that repo and all three need a push, which is the user's. |

## The one thing this repo could give epass.io, and why it is not done here

`dev` is **21 commits ahead of `origin/dev`, unpushed**. epass.io pins a SHA 87 commits behind
local `dev`; it can only bump that pin to something the remote has. So a push of `dev` is the sole
outstanding NativeWeb-side input to epass.io — and per the standing rule, **pushing is the user's
call, always**. It is recorded here as a decision waiting on a person, not as work.

Note that epass.io is not *blocked* on it: the pin it holds resolves today.

## Consequences

- `work/status/current.md` loses the cutover bullet.
- `work/milestones.md` milestone 4 moves from `in progress` to `shipped`: "be a stable upstream"
  is a condition that is met and stays met, not a task with an end.
- The stale advice in `epass.io/work/todo.md` — "copy from `nativeweb/apps/web`, never from
  `nativeweb/skeleton`" — names a directory that left this repo on 2026-08-31. Reported to the
  user; that file belongs to the other repo and is not edited from here.
