# NativeWeb

A server-first architecture for ASP.NET Core: Minimal APIs, server-rendered HTML, NativeAOT,
and no client framework. Explicit code over convention, compile-time errors over runtime blanks,
and a page that works before any JavaScript runs.

**New here, read these two in order.** Everything else on this site answers a specific question
and is not written to be read straight through.

| Read | What you get |
| --- | --- |
| [Getting started](getting-started.md) | A clean clone to a running application and a green suite, then stamping your own product from `skeleton/`. |
| [Tutorial: your first feature](tutorial-first-feature.md) | One feature through every layer it touches — a read model, a JauntyQ query, a renderer, an endpoint, and a test at each of the three tiers. |

## Then, as you need it

- **[Architecture](../01-architecture/native-web-architecture.md)** — why the stack is what it
  is, and what it refuses to do. Everything else assumes it.
- **[Progressive enhancement](../01-architecture/progressive-enhancement.md)** — the `data-nw-*`
  conventions. Mandatory for every browser-facing page.
- **[Configuration](../02-reference/configuration.md)** — every setting, its default, and what a
  wrong or absent value actually costs.
- **[Template diagnostics](../02-reference/template-diagnostics.md)** — `NWT001`–`NWT016`, what
  each build error means and how to fix it.
- **[Security baseline](../02-reference/security-baseline.md)** — the ASVS-derived checklist the
  identity provider is held to.
- **[Migration lock review](../02-reference/migration-lock-review.md)** — how long `db/migrate.sh`
  holds its locks. Read before pointing it at a live database.

Specs, plans, decisions and lessons live in the repository rather than on this site; they record
how the project is built rather than how to use it.
