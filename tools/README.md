# tools

Maintenance tooling for the shared classes duplicated between `Com.H` and `Com.H.Data.Common`.

## Why this exists

`Com.H.Data.Common` deliberately ships with **no NuGet dependencies**. To do that it carries
private (`internal`) copies of eight `Com.H` utility classes rather than taking a package
reference. The duplication is a considered trade-off, but it has one failure mode: fixing a
bug in one repo and forgetting the other.

`check-shared-drift.ps1` catches exactly that.

## check-shared-drift.ps1

The two copies are **not** required to be identical — they legitimately differ (access
modifiers, target-framework guards, members that only make sense in one library). Instead,
the script records an *approved baseline* hash for each side and reports when one side moves
without the other.

```powershell
pwsh tools/check-shared-drift.ps1                     # check; exit 1 on drift
pwsh tools/check-shared-drift.ps1 -Diff DataMapper    # what actually differs
pwsh tools/check-shared-drift.ps1 -Approve            # re-baseline after syncing
```

### Statuses

| Status | Meaning |
|---|---|
| `OK` | Both sides match their approved baseline. |
| `DRIFT` | One side changed, the other didn't. **This is the one that matters** — exits 1. |
| `REVIEW` | Both sides changed. Confirm they still agree, then `-Approve`. |
| `DOCS` | Only comments / XML docs moved. Informational; does not fail. |
| `MISSING` | A file in the manifest no longer exists. |

### How the comparison works

Source is reduced to a canonical form before hashing, so cosmetic edits don't cry wolf:

- comments and XML docs are separated out and hashed independently
- `using` / `namespace` declarations and `#region` markers are dropped
- `public` and `internal` are folded to a single token
- string and character literals are lifted out during whitespace normalization and put back
  before hashing, so their contents still count but their formatting doesn't
- all remaining whitespace around punctuation is removed, then the text is split into
  statement units — so re-indenting or re-wrapping a line is **not** drift, but changing what
  it does **is**

Comment stripping is literal-aware (a `//` inside a regex string is not treated as a comment).

### Adding or removing a shared class

Edit `shared-classes.json`, then run `-Approve`. The `note` field is the place to record *why*
two copies legitimately differ — it's printed by `-Diff`.

## install-git-hooks.ps1

Installs the check as a git hook in **both** repos so it runs without you remembering.

```powershell
pwsh tools/install-git-hooks.ps1                             # warn-only pre-commit (default)
pwsh tools/install-git-hooks.ps1 -Blocking                   # reject the commit instead
pwsh tools/install-git-hooks.ps1 -HookType pre-push -Blocking
pwsh tools/install-git-hooks.ps1 -Uninstall
```

Default is **warn-only**, because the natural workflow is *fix repo A, commit, fix repo B,
commit* — a blocking hook would reject the first of those two commits every time. The hook
skips silently if the sibling repo or `pwsh` isn't available, so fresh clones and CI agents
aren't affected.

## CI

The script's exit code is CI-ready:

```yaml
- name: Shared-class drift
  shell: pwsh
  run: ./tools/check-shared-drift.ps1 -ComHRoot ../Com.H
```

It needs both repositories checked out side by side.
