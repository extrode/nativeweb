# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Nothing in flight.** The scaffold generator's seven silent-wrong-answer defects are fixed and
  merged, three on 2026-09-06 and the four the review of that branch left open on the same day;
  see `previous.md`. What is left of that audit is one note in `../todo.md` about a cascaded
  refusal reporting the outer fault before the inner one, which costs a second build and hides
  nothing.

  **The age private key is off the host as of 2026-09-06**, in the operator's own store, which the
  runbook records without naming, because this repository is public. That closes one of R17b's two
  blockers and not R17b: readable needs the key and the bytes, and the bytes are still only on the
  machine being backed up. The 2026-09-01 key is not being rotated -- it was printed into a session
  during the drill, and the operator decided that reach acceptable for `nativeweb-dev`.
