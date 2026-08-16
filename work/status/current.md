# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Spec 007 — session tokens hashed at rest.** The IdP's session cookie value is the
  `bff_store` primary key, stored in the clear, so any database read yields live bearer tokens.
  Branch `feat/007-session-token-at-rest`; task breakdown at
  `docs/specs/007-session-token-at-rest/007-tasks.md`.
