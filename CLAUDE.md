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
