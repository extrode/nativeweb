# Current

What's in flight right now. Keep it SMALL: one focus at a time where possible.

Exactly **one** task, the one being worked on right now. Not a list: the list is `../tasklist.md`,
and this is a window onto one entry of it. If two things are genuinely in flight, that is a fact
worth seeing here rather than hiding, but it is the exception.

- **Acting on the fable review of `origin/dev..dev`, in two branches.** The review of the T15
  drill and the credential move returned fourteen findings on 2026-09-06; three were then
  confirmed by running something, and one it had listed as impossible to check without a shell
  turned out to be a CI breaker.

  **Done on `fix/ci-credential-config-file`:** all four credential steps create the user-level
  NuGet config before writing to it, because `dotnet nuget add source --configfile` exits 1 with
  "File '...' does not exist" rather than creating it -- measured on both shells against a
  scratch `HOME` and `APPDATA`. The guard is rebuilt around job blocks: it accepted
  `--configfile NuGet.Config`, which resolves into the workspace on a case-insensitive
  filesystem, and stayed green with a whole credential step deleted. Both mutations are red now,
  repo unit tier 196 green.

  **Still to do:** the documentation overclaims the same review found. R17 is marked met though
  its own wording requires offsite backups and the drill's bucket was MinIO on the same physical
  host; `docs/02-reference/deployment.md`'s opening callout still says R16 and R17 are unmet and
  that the tables below read `NOT YET MEASURED`, 350 lines above the tables that now hold real
  numbers; `011-tasks.md:126` is ticked with the pre-drill paragraph kept verbatim beneath it,
  saying the box stays unchecked.
