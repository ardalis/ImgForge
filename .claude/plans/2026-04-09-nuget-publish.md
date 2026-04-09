# Plan: Publish ImgForge to NuGet.org with GitHub Actions

## Overview

This plan covers everything needed to ship `ImgForge` as a public NuGet global tool and keep it
automatically published on every GitHub Release. The work is split into four focused phases:

1. Polish the `.csproj` metadata so the NuGet listing looks professional.
2. Fix the three copied-in GitHub Actions workflow files so they reference the correct project paths,
   use `dotnet test` instead of the wrong `dotnet testcoverage.cs` script, and carry the right names.
3. Set up the GitHub repository (OIDC trusted publisher, `NUGET_USER` secret, NuGet.org package
   reservation).
4. Cut the first release and verify end-to-end publication.

No source files are modified during planning — all file edits happen during execution.

---

## Phases at a Glance

- [ ] **1. NuGet Package Metadata**
- [ ] **2. GitHub Actions Workflow Fixes**
- [ ] **3. GitHub & NuGet.org Repository Setup**
- [ ] **4. First Release and Verification**

---

## 1. NuGet Package Metadata

Ensure `src/ImgForge/ImgForge.csproj` has all the metadata NuGet.org needs for a quality listing.
The existing file already has `PackageId`, `Version`, `Description`, `Authors`, and
`PackageReleaseNotes`. The following properties are missing and should be added.

- [ ] **1.1** Add `<RepositoryUrl>` pointing to the GitHub repo URL (e.g.
  `https://github.com/ardalis/ImgForge`).
- [ ] **1.2** Add `<RepositoryType>git</RepositoryType>`.
- [ ] **1.3** Add `<PackageTags>` with relevant search terms, e.g.
  `dotnet-tool;image;thumbnail;youtube;blog;html;template;playwright`.
- [ ] **1.4** Add `<PackageLicenseExpression>MIT</PackageLicenseExpression>` (or whichever license
  applies; do not use the deprecated `PackageLicenseUrl`).
- [ ] **1.5** Add `<PackageProjectUrl>https://github.com/ardalis/ImgForge</PackageProjectUrl>`.
- [ ] **1.6** Add `<PackageReadmeFile>README.md</PackageReadmeFile>` and include the root
  `README.md` as a `<None Include>` item with `Pack=true` and `PackagePath="\"` so NuGet.org
  renders the readme on the package page.
- [ ] **1.7** Optionally add a `<PackageIcon>` if a 128x128 PNG icon is available; skip if not
  ready — it can be added in a future release. The `NoWarn>NU5111` suppression already present
  covers the missing icon warning, so this is low priority.
- [ ] **1.8** Verify `dotnet pack src/ImgForge/ImgForge.csproj --configuration Release` succeeds
  locally and inspect the resulting `.nupkg` with NuGet Package Explorer or
  `dotnet tool install --global` from the local feed to confirm the tool installs and runs.

---

## 2. GitHub Actions Workflow Fixes

Three workflow files were copied from `Ardalis.Cli` and need to be updated for this project. Each
set of changes is listed precisely so they can be applied one file at a time.

### 2a. `.github/workflows/build.yml`

The CI workflow that runs on every push and PR.

- [ ] **2a.1** Rename the workflow: change `name: ardalis CLI Build and Test` to
  `name: ImgForge Build and Test`.
- [ ] **2a.2** Update the `PROJECT_FILE` env var:
  change `src/Ardalis.Cli/Ardalis.Cli.csproj` to `src/ImgForge/ImgForge.csproj`.
- [ ] **2a.3** Replace the broken test step:

  Remove:
  ```yaml
  - name: Test with coverage
    run: dotnet testcoverage.cs
  ```

  **Decision point — code coverage PR comments (choose one option):**

  **Option A — Keep code coverage PR comments** (the current design using
  `irongut/CodeCoverageSummary` and `comment-on-pr.yml`). This requires the
  `Microsoft.Testing.Extensions.CodeCoverage` package in the test project and a Cobertura-format
  output file at `coverage/coverage.cobertura.xml`. Replace the test step with:
  ```yaml
  - name: Test with coverage
    run: >
      dotnet test tests/ImgForge.Tests/ImgForge.Tests.csproj
      --configuration Release
      --collect:"XPlat Code Coverage"
      --results-directory coverage
      -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura
  ```
  Then keep all the subsequent `irongut/CodeCoverageSummary`, artifact upload, and
  `workflow-outputs` steps unchanged.

  **Option B — Simplify (recommended for now)**: Drop all coverage reporting steps and the
  `comment-on-pr.yml` workflow entirely. Replace the broken test step with a plain:
  ```yaml
  - name: Test
    run: dotnet test --configuration Release --no-restore
  ```
  Then delete the `Code Coverage Summary Report`, `Upload code coverage results artifact`,
  `Save workflow outputs for PR comment`, and `Upload workflow outputs` steps. This can always be
  re-added later.

- [ ] **2a.4** If Option B is chosen, delete `.github/workflows/comment-on-pr.yml` entirely
  (see task 2c below). If Option A is chosen, skip 2c.

### 2b. `.github/workflows/publish.yml`

The release/publish workflow that pushes to NuGet.org.

- [ ] **2b.1** Update the `PROJECT_FILE` env var:
  change `src/Ardalis.Cli/Ardalis.Cli.csproj` to `src/ImgForge/ImgForge.csproj`.
- [ ] **2b.2** Update the `TEST_PROJECT` env var:
  change `tests/Ardalis.Cli.Tests/Ardalis.Cli.Tests.csproj` to
  `tests/ImgForge.Tests/ImgForge.Tests.csproj`.
- [ ] **2b.3** Fix the broken test step. The current step runs:
  ```yaml
  run: dotnet run --project "${{ env.TEST_PROJECT }}" --configuration Release --no-build
  ```
  Replace it with:
  ```yaml
  run: dotnet test "${{ env.TEST_PROJECT }}" --configuration Release --no-restore
  ```
- [ ] **2b.4** Confirm the `NuGet/login@v1` step and the `dotnet nuget push` step are otherwise
  correct — they look structurally sound in the copied file; no other changes needed there.
- [ ] **2b.5** Confirm `dotnet restore` in this workflow targets the solution or the specific project
  (currently it restores only `PROJECT_FILE`). Consider changing restore to solution-wide
  (`dotnet restore`) so the test project dependencies are also restored before the test step.

### 2c. `.github/workflows/comment-on-pr.yml`

This workflow triggers via `workflow_run` on the build workflow name.

- [ ] **2c.1** If **Option A** was chosen in 2a.3: Update the `workflow_run` trigger:
  change `workflows: ["ardalis CLI Build and Test"]` to `workflows: ["ImgForge Build and Test"]`
  so it matches the renamed build workflow.
- [ ] **2c.2** If **Option B** was chosen in 2a.3: Delete this file entirely — it has no purpose
  without the coverage artifact steps in `build.yml`.

---

## 3. GitHub & NuGet.org Repository Setup

One-time configuration of secrets and trusted publisher settings. These are done in browser UIs,
not in code.

- [ ] **3.1** **Reserve the package on NuGet.org**: Go to nuget.org, sign in as `ardalis`, and
  verify the package ID `ImgForge` is not already taken. If available, it will be claimed
  automatically on the first push. No manual reservation is needed.
- [ ] **3.2** **Create a NuGet.org trusted publisher (OIDC)**:
  - On nuget.org, navigate to Account Settings > API Keys (or Trusted Publishers).
  - Add a new Trusted Publisher of type GitHub Actions.
  - Set the owner to `ardalis`, repository to `ardalis/ImgForge`, workflow to `publish.yml`, and
    environment to the default (none required unless you want a GitHub environment gate).
  - This grants the workflow permission to push packages via OIDC without a stored API key.
- [ ] **3.3** **Add `NUGET_USER` secret in GitHub**:
  - In the `ardalis/ImgForge` repo, go to Settings > Secrets and variables > Actions.
  - Add a repository secret named `NUGET_USER` with the value of the NuGet.org account username
    (e.g. `ardalis`). The `NuGet/login@v1` action requires this to exchange the OIDC token for a
    short-lived API key.
- [ ] **3.4** **Confirm the repo is public** (NuGet.org publishing from a private repo requires
  additional OIDC configuration; a public repo is the simplest path).
- [ ] **3.5** **Verify branch protection** on `main` does not block the publish workflow from
  running on releases.

---

## 4. First Release and Verification

Cut the `v0.1.0` GitHub Release and confirm the full pipeline.

- [ ] **4.1** Ensure all workflow file changes from Phase 2 are merged to `main`.
- [ ] **4.2** Confirm a successful `ImgForge Build and Test` CI run on `main` after the workflow
  fixes land (the build badge should be green).
- [ ] **4.3** Create a GitHub Release using the `gh` CLI:
  - Tag: `v0.1.0`
  - Target branch: `main`
  - Title: `v0.1.0 — Initial Release`
  - Body: brief summary of what the tool does.
  - **Publish release**
- [ ] **4.4** Watch the `Publish to NuGet` workflow run in the Actions tab — confirm all steps
  pass, especially `NuGet login (OIDC -> temp API key)` and `Push to NuGet`.
- [ ] **4.5** After the workflow completes, navigate to
  `https://www.nuget.org/packages/ImgForge/0.1.0` and confirm the package is listed, the readme
  renders, the tags are present, and the license is shown.
- [ ] **4.6** Smoke test the published tool via `dnx`
  
  ```bash
  dnx -y imgforge -- --help
  ```

- [ ] **4.7** Smoke test the published tool via `dotnet tool install`
  
  ```bash
  dotnet tool install --global ImgForge --version 0.1.0
  imgforge --help
  ```

---

## Success Criteria

- `dotnet pack src/ImgForge/ImgForge.csproj` produces a `.nupkg` with correct metadata (readme,
  license, tags, repo URL).
- `build.yml` passes on every push and PR to `main`, using `dotnet test` (not any custom script).
- All three workflow files reference `ImgForge` paths/names — no remaining references to
  `Ardalis.Cli`.
- The `comment-on-pr.yml` workflow either correctly tracks the renamed build workflow name, or has
  been cleanly removed alongside the coverage steps (whichever option was chosen).
- The `NUGET_USER` secret is configured and the NuGet.org OIDC trusted publisher entry is in place.
- Publishing the `v0.1.0` GitHub Release triggers the publish workflow automatically and produces a
  successful push to NuGet.org.
- `dotnet tool install --global ImgForge` installs successfully and `imgforge --help` executes.
