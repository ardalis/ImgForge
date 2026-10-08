# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

ImgForge is a .NET tool for producing images from templates with text overlays.

## Build, Test, and Run

Standard .NET CLI commands (update once solution/project files exist):

```bash
dotnet build          # Build the solution
dotnet test           # Run all tests
dotnet test --filter "FullyQualifiedName~SomeTest"  # Run a single test
dotnet run --project src/ImgForge  # Run the tool
```

## Releasing

Publishing a GitHub release tagged `vX.Y.Z` (or `vX.Y.Z-suffix`) triggers `.github/workflows/publish.yml`, which
builds, tests, packs, and pushes to NuGet.org using the tag as the package version.

- Before releasing, `dotnet BuildTestFormat.cs` must pass on `main`.
- Do not bump `<Version>` in `src/ImgForge/ImgForge.csproj`; it is a `0.0.0-local` placeholder. The version comes only from the tag.
- Do not add `<PackageReleaseNotes>` to the csproj; the workflow links release notes to the GitHub release.
- Create releases with `gh release create vX.Y.Z --target main --generate-notes` — only when the user asks, since it publishes publicly.
- Manual `workflow_dispatch` runs are dry runs (`0.0.0-ci.N`, never pushed).
- See the "Publishing a Release" section of README.md for the full steps.
