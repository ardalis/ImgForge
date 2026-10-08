# Issue #14 — Locate .tools/imgforge if it's not directly in the working directory

Issue: https://github.com/ardalis/ImgForge/issues/14
Follow-up to #12 (see `2026-10-08-issue-12-tools-dir-spec.md`). Spec: https://github.com/tools-dir/spec §7/§8.

## Summary

When `--template` is omitted, ImgForge only looks for `.tools/imgforge/template.html` (and the legacy
`.imgforge/template.html`) in the current directory. Running from a subdirectory of a repository finds
nothing. Walk up parent directories to the repository root, but never beyond it.

## Assumptions / decisions

- **Repository root** = the nearest directory (CWD or an ancestor) containing a `.git` entry. `.git` may be a
  directory or a file (worktrees/submodules), so check both.
- **Search order:** start at CWD and move up one directory at a time, stopping after the repo root. In each
  directory check `.tools/imgforge/template.html`, then `.imgforge/template.html`. The nearest directory wins
  (a legacy template in a subdirectory beats `.tools/` at the root) — closest configuration is most specific,
  and this keeps precedence identical to today when run from the root.
- **Not in a repository** (no `.git` found in any ancestor): only the CWD is checked — exactly today's behavior.
  We never walk past a repo root, and outside a repo there is no root to walk to.
- **Returned path:** when found in CWD, the same relative path as today (`.tools/imgforge/template.html`), so
  existing messages/tests are unchanged. When found in an ancestor, the full path is returned (the renderer
  already handles absolute `.html` paths and uses the file's folder as `<base>`).
- The legacy-location console tip switches from string equality to a new `TemplatePathResolver.IsLegacyTemplatePath`
  helper so it fires for legacy templates found in parent directories too.
- Nested repositories/monorepos: the nearest `.git` is the boundary (spec §14 leaves this out of scope).
- No `BuildTestFormat.cs` exists in this repo; verification uses `dotnet build`, `dotnet test`,
  and `dotnet format --verify-no-changes` (scoped to changed files, since pre-existing violations exist — see #12 plan).

## Phase 1 — Core resolution (`src/ImgForge.Core/TemplatePathResolver.cs`)

- [x] Walk from CWD up to (and including) the repo root; stop at CWD when not in a repo.
- [x] Return relative path for CWD hits, full path for ancestor hits.
- [x] Add `IsLegacyTemplatePath(string)`.
- [x] Update the not-found error message to mention the search extends up to the repository root.
- [x] Update the `--template` help text and legacy tip in `GenerateCommand`.

## Phase 2 — Tests (`tests/ImgForge.Tests/TemplatePathResolverTests.cs`, unit)

- [x] Existing tests still pass (CWD behavior unchanged).
- [x] Template at repo root, run from nested subdirectory → found (full path).
- [x] Template above the repo root (outside the repo) → not found; throws.
- [x] No `.git` anywhere, template in parent → not found (no walking outside a repo).
- [x] Nearer directory wins over repo root.
- [x] `.git` as a file (worktree) is treated as a repo root.
- [x] Legacy template in a parent directory is found and `IsLegacyTemplatePath` returns true.

## Phase 3 — Docs

- [x] README and `docs/content/docs/usage/templates.md`: describe parent-directory lookup up to the repo root.

## Phase 4 — Independent review

- [x] Separate agent (fresh context) reviews the full diff for correctness, missed requirements,
      test gaps, security, performance, and accessibility.
- [x] Address all significant findings (or record reasons here).

## Phase 5 — Verification

- [x] `dotnet build` succeeds.
- [x] `dotnet test` passes.
- [x] `dotnet format --verify-no-changes` reports nothing for changed files.

## Review outcome

No high findings. Addressed:

- Medium: the "not in a repository" test depended on no `.git` existing above the machine's temp folder. Added an
  internal `ResolveTemplate(template, ceilingDirectory)` overload (via `InternalsVisibleTo` on ImgForge.Core);
  tests pass the temp folder as the ceiling so results are machine-independent.
- Low: the not-found error now distinguishes "not inside a git repository" from "searched up to repository root '<path>'".
- Low: added tests — CWD is the repo root (relative path returned), CWD template beats repo-root template,
  nested `.git` (submodule) is the search boundary.

Not addressed (by design / acceptable):

- Intermediate directories between CWD and the repo root are searched, nearest wins — intentional, documented.
- Any `.git` file (without a `gitdir:` line) counts as a root; `GIT_DIR` overrides and bare repos ignored — acceptable CLI default.
- Symlink/junction CWDs are not resolved to real paths — no practical impact identified.
- Tests still change the process-wide CWD (pre-existing pattern, not a regression).

## Verification notes

- `dotnet build`: 0 errors. `dotnet test`: 53/53 passed.
- `dotnet format --verify-no-changes` on changed files: no violations in new code; `GenerateCommand.cs` still reports
  the 21 pre-existing alignment violations on untouched lines (same count as before this change; CI does not run format).
