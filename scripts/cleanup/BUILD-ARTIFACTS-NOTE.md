# Build artifacts cleanup

`build-artifacts-cleanup-2026-09-16.ps1`, in this folder, deletes build and tool-output
directories (`bin`, `obj`, `dist`, published `wwwroot/vendor` copies, scratch output under `tmp/`)
to reclaim disk space. The directories it removes are listed inside the script.

## What it does

- **Does nothing by default.** Bare invocation is a dry run.
- Deletes only with `-e` / `--execute`.
- Re-checks every path against `git ls-files` at run time and leaves alone anything git tracks,
  even if the path is on its list. A stale list makes the script a no-op on those entries, not a
  data-loss risk.
- Reports "already gone" instead of erroring on anything already cleaned up by hand.

Run it from PowerShell, from anywhere (it resolves its own repo root):

```powershell
pwsh -NoProfile scripts/cleanup/build-artifacts-cleanup-2026-09-16.ps1        # dry run
pwsh -NoProfile scripts/cleanup/build-artifacts-cleanup-2026-09-16.ps1 -e     # delete
```

## Keep it updated

If the build or artifact layout changes (new output directories, moved paths, renamed or removed
projects), update the list inside the `.ps1`. The script is the source of truth for what gets
deleted; this file only explains it. A stale script is not dangerous, but it silently stops
reclaiming space for artifact directories that are not on its list.
