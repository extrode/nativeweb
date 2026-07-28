# work/

The state of the work underway. Not documentation — documentation is `../docs/`.

| File | Holds |
|---|---|
| `tasklist.md` | Every task for the body of work currently underway. Stays put; only the ticks change. |
| `status/current.md` | The one task in flight. |
| `status/next.md` | The one task queued, pulled from the tasklist. |
| `status/previous.md` | The task that just finished. |
| `todo.md` | Unscheduled backlog. Out-of-scope findings land here as one line. |
| `milestones.md` | Where the project is going. Coarse, rarely edited. |
| `archive/` | Completed tasklists, `YYYY-MM-DD-<slug>.md`, one per shipped body of work. |

The `status/` trio is a three-deep sliding window over `tasklist.md`, one task each. `/status roll`
advances it. What shipped goes to `../CHANGELOG.md`, at the repo root.

Updated in the same branch as the work, never batched to the end of a session.
