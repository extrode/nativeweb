# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Scaffolding POC (brainstorm 001 T1.3)** — merged. `libs/nativeweb.scaffold{,.generator}`,
  `apps/sample`, and two new suites. Follow-ups are on `work/todo.md`; none block.
- Nothing else in flight. The away backlog slate is merged into `dev` and unpushed; what remains on
  `work/todo.md` is either blocked on the user (epass.io cutover sign-off, collector address,
  session key type, JauntyQ CI feed) or blocked upstream (Node 20 to 24, the framework `ref: dev`
  pin, the SDK fix that would let `Directory.Build.targets` lose its Debug compression workaround).
- Suite counts: **489 web-unit / 296 web-integration / 154 render / 49 scaffold-unit /
  17 scaffold-integration / 64 e2e**. All green except e2e, not run this session.
