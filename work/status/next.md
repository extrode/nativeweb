# Next

Exactly **one** task, the next one to start, pulled from `../tasklist.md` when the rotation
advances. The full task list for this body of work is `../tasklist.md`; the unscheduled backlog is
`../todo.md`; where the project is going is `../milestones.md`.

- **H2 (spec 002, IdP hardening)** — `fix/idp-csp-htmx-indicator`: `htmx-config` meta to stop
  htmx injecting its un-nonced indicator stylesheet, indicator rules into `components.css`,
  widen the e2e CSP assertion back to `Assert.Empty(violations)`.
