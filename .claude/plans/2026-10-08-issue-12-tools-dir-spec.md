# Issue #12 — Add support for tools-dir spec

Issue: https://github.com/ardalis/ImgForge/issues/12
Spec: https://github.com/tools-dir/spec (v0.1-draft)

## Summary

Adopt the `.tools/` directory convention: ImgForge's repository-local configuration lives in
`.tools/imgforge/`. Today the only such configuration is the default template
(`.imgforge/template.html`), used when `--template` is omitted.

## Assumptions / decisions

- New default template location: `.tools/imgforge/template.html`.
- The legacy `.imgforge/template.html` location keeps working (spec §8/§9: preserve existing behavior).
- Precedence (spec §8 requires this be documented): `.tools/imgforge/` wins when both exist.
- Paths are resolved relative to the current working directory, matching existing behavior
  (no git-root discovery; out of scope for this issue).
- This repo's own `.imgforge/` folder is migrated to `.tools/imgforge/` as a dogfooding example.
- `BuildTestFormat.cs` does not exist in this repo; verification uses
  `dotnet build`, `dotnet test`, and `dotnet format --verify-no-changes`.

## Phase 1 — Core resolution

- [x] Update `TemplatePathResolver` to check `.tools/imgforge/template.html`, then `.imgforge/template.html`.
- [x] Update the error message to list both searched locations.
- [x] Update `--template` help text in `GenerateCommand`.

## Phase 2 — Tests (tests/ImgForge.Tests/TemplatePathResolverTests.cs, unit)

- [x] Only `.tools/imgforge/template.html` exists → it is used.
- [x] Only legacy `.imgforge/template.html` exists → it is used.
- [x] Both exist → `.tools/imgforge/` wins.
- [x] Neither exists → helpful error naming both paths.
- [x] Explicit `--template` still wins.

## Phase 3 — Repo migration and docs

- [x] `git mv .imgforge .tools/imgforge`.
- [x] Update README and docs (`docs/content/docs/usage/templates.md`) — also fix the incorrect
      `/.imgforge/index.html` reference; document precedence and legacy support.

## Phase 4 — Independent review

- [x] Separate agent (fresh context) reviews the full diff for correctness, missed requirements,
      test gaps, security, performance, and accessibility.
- [x] Address all significant findings (or record reasons here).

## Phase 5 — Verification

- [x] `dotnet build` succeeds.
- [x] `dotnet test` passes.
- [x] `dotnet format --verify-no-changes` reports nothing.

## Review outcome

No high/medium findings. Low-severity suggestions:

- Addressed: console tip suggesting migration when the legacy `.imgforge/` template is used.
- Addressed: README now uses the same numbered list as the docs and states lookup is relative to the current directory.
- Not addressed: walking up to the git root (spec §7). Deliberately out of scope; existing behavior is CWD-relative. Candidate follow-up issue.
- Not addressed: test for a *directory* named `template.html`. `File.Exists` returns false for directories, so this is BCL behavior rather than ImgForge logic.

## Verification notes

NuGet audit (warnings as errors) was failing restore on `main`:

- `SixLabors.ImageSharp` 3.1.12 (test-only, used to read PNG dimensions). Patched 4.x and the 3.x line are under the
  Six Labors Split License (paid commercial license for some users); Apache-licensed 2.x is also vulnerable. Per the
  maintainer's request to avoid a paid-license dependency, ImageSharp was removed and replaced by a small
  PNG IHDR reader in the test project (`tests/ImgForge.Tests/PngHeader.cs`).
- `Scriban` 7.1.0 — bumped to 7.5.0 (BSD-2-Clause, same major version).

After these changes `dotnet build` and `dotnet test` pass with NuGet audit enabled. `dotnet format --verify-no-changes`
reports pre-existing CRLF/alignment violations in untouched code (CI does not run format); changed code adds none.
