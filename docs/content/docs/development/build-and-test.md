---
title: "Build and Test"
weight: 10
---

Information for developers working on ImgForge.

## Building the Project

```bash
dotnet build
```

## Running Tests

### Unit Tests Only

Run unit tests without integration tests (no browser required):

```bash
dotnet test --filter "Category!=Integration"
```

### Full Test Suite

Run all tests including integration tests:

```bash
dotnet test
```

Chromium is downloaded automatically on the first run of integration tests — no separate install step needed.

## Dependencies

| Package | Role |
|---|---|
| [Scriban](https://github.com/scriban/scriban) | Liquid-syntax template engine — substitutes `{{ title }}`, `{{ bg }}`, `{{ width }}`, `{{ height }}`, and `{% for img in overlays %}` in HTML templates |
| [PuppeteerSharp](https://github.com/hardkoded/puppeteer-sharp) | Headless Chromium driver — renders the HTML with full CSS support and captures a pixel-perfect screenshot. Chromium is downloaded automatically on first run. |
| [System.CommandLine](https://github.com/dotnet/command-line-api) | Parses CLI arguments and subcommands |
| [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) | Used in tests to verify output PNG dimensions |
| [xunit](https://xunit.net/) | Test framework |

## Installing as a Global Tool

After building, you can install your local build as a global tool:

```bash
dotnet pack
dotnet tool install --global --add-source ./nupkg ImgForge
```

Or update an existing installation:

```bash
dotnet pack
dotnet tool update --global --add-source ./nupkg ImgForge
```
