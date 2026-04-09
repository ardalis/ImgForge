# ImgForge Implementation Plan

## Overview

ImgForge is a .NET 10 CLI tool (packaged as a `dotnet tool`) for generating blog and YouTube thumbnail
images from HTML templates. The approach: load an HTML template, inject variables (title, background
image, overlay images) via Scriban, render in a Playwright headless Chromium browser, and screenshot
to PNG.

The plan is broken into five incremental phases. Each phase ends with a working, verifiable state so
progress can be confirmed before moving on.

---

## Phases at a Glance

- [x] **1. Repo & Solution Scaffolding**
- [x] **2. Core Library — Models and Template Rendering**
- [x] **3. Core Library — Image Generation (Playwright)**
- [x] **4. CLI Entry Point**
- [x] **5. Built-in Templates, Tests, and Polish**

---

## 1. Repo & Solution Scaffolding

Establish the full solution layout, shared build properties, and central package management before
writing any feature code. Getting this right up-front prevents drift and rework later.

- [x] **1.1** Create the solution file using the modern `.slnx` format:

  ```bash
  dotnet new sln --name ImgForge --format slnx
  ```

- [x] **1.2** Create project directories:

  ```
  src/ImgForge/
  src/ImgForge.Core/
  src/ImgForge.Core/templates/
  tests/ImgForge.Tests/
  ```

- [x] **1.3** Create `Directory.Build.props` at the repo root with shared properties:

  ```xml
  <Project>
    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <Nullable>enable</Nullable>
      <ImplicitUsings>enable</ImplicitUsings>
      <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
      <LangVersion>latest</LangVersion>
    </PropertyGroup>
  </Project>
  ```

- [x] **1.4** Create `Directory.Packages.props` at the repo root to enable Central Package Management:

  ```xml
  <Project>
    <PropertyGroup>
      <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    </PropertyGroup>
    <ItemGroup>
      <PackageVersion Include="Microsoft.Playwright" Version="1.44.0" />
      <PackageVersion Include="Scriban" Version="5.10.0" />
      <PackageVersion Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
      <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
      <PackageVersion Include="xunit" Version="2.9.0" />
      <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
      <PackageVersion Include="SixLabors.ImageSharp" Version="3.1.4" />
    </ItemGroup>
  </Project>
  ```

  > Note: verify latest stable versions before finalizing.

- [x] **1.5** Create `src/ImgForge.Core/ImgForge.Core.csproj`:

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <OutputType>Library</OutputType>
    </PropertyGroup>
    <ItemGroup>
      <EmbeddedResource Include="templates\**\*.html" />
    </ItemGroup>
    <ItemGroup>
      <PackageReference Include="Microsoft.Playwright" />
      <PackageReference Include="Scriban" />
    </ItemGroup>
  </Project>
  ```

- [x] **1.6** Create `src/ImgForge/ImgForge.csproj` (CLI, packaged as dotnet tool):

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <OutputType>Exe</OutputType>
      <PackAsTool>true</PackAsTool>
      <ToolCommandName>imgforge</ToolCommandName>
      <PackageId>ImgForge</PackageId>
      <Version>0.1.0</Version>
      <Description>Generate blog and YouTube thumbnail images from HTML templates.</Description>
    </PropertyGroup>
    <ItemGroup>
      <PackageReference Include="System.CommandLine" />
      <ProjectReference Include="..\ImgForge.Core\ImgForge.Core.csproj" />
    </ItemGroup>
  </Project>
  ```

- [x] **1.7** Create `tests/ImgForge.Tests/ImgForge.Tests.csproj`:

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <ItemGroup>
      <PackageReference Include="Microsoft.NET.Test.Sdk" />
      <PackageReference Include="xunit" />
      <PackageReference Include="xunit.runner.visualstudio" />
      <PackageReference Include="SixLabors.ImageSharp" />
      <ProjectReference Include="..\..\src\ImgForge.Core\ImgForge.Core.csproj" />
    </ItemGroup>
  </Project>
  ```

- [x] **1.8** Add all three projects to the solution:

  ```bash
  dotnet sln add src/ImgForge/ImgForge.csproj
  dotnet sln add src/ImgForge.Core/ImgForge.Core.csproj
  dotnet sln add tests/ImgForge.Tests/ImgForge.Tests.csproj
  ```

- [x] **1.9** Verify the solution builds cleanly with no warnings:

  ```bash
  dotnet build
  ```

---

## 2. Core Library — Models and Template Rendering

Implement the pure-logic layer with no browser dependency. Unit tests can run fully offline after
this phase.

- [x] **2.1** Create `src/ImgForge.Core/GenerateOptions.cs` with the domain model:

  ```csharp
  namespace ImgForge.Core;

  public record GenerateOptions(
      string Template,
      string Title,
      string? Background,
      IReadOnlyList<OverlayImage> Overlays,
      string Out,
      int Width = 1200,
      int Height = 630
  );

  public record OverlayImage(string Src, string Style = "");
  ```

- [x] **2.2** Create `src/ImgForge.Core/TemplateRenderer.cs` implementing these rules:

  - If `Template` has no path separator and no `.html` extension → load embedded resource
    `ImgForge.Core.templates.{name}.html` from the assembly manifest.
  - Otherwise treat `Template` as a file path and read from disk.
  - Convert any local `Background` path to a `file:///` URI; leave HTTP(S) URLs unchanged.
  - Use Scriban `Template.Parse` + `template.Render(scriptObject)` to inject variables.
  - Expose a single `string Render(GenerateOptions opts)` method.

- [x] **2.3** Add placeholder embedded template files so the build succeeds:

  - `src/ImgForge.Core/templates/blog.html` — minimal valid HTML (content added in Phase 5)
  - `src/ImgForge.Core/templates/youtube.html` — minimal valid HTML (content added in Phase 5)

- [x] **2.4** Write unit tests in `tests/ImgForge.Tests/TemplateRendererTests.cs`:

  - Inline template string is rendered with all variables substituted.
  - Built-in template name resolves to embedded resource without throwing.
  - Local file path for background is converted to a `file:///` URI.
  - HTTP URL for background is passed through unchanged.
  - Missing variable in template does not throw (Scriban default behavior).

- [x] **2.5** Confirm unit tests pass with no browser installed:

  ```bash
  dotnet test --filter "Category!=Integration"
  ```

---

## 3. Core Library — Image Generation (Playwright)

Add the Playwright rendering layer. This is the highest-risk component; isolating it in its own
phase validates the approach before the CLI is wired up.

- [x] **3.1** Create `src/ImgForge.Core/ImageGenerator.cs`:

  ```csharp
  namespace ImgForge.Core;

  public class ImageGenerator(TemplateRenderer renderer)
  {
      public async Task<string> GenerateAsync(GenerateOptions opts)
      {
          var html = renderer.Render(opts);
          using var playwright = await Playwright.CreateAsync();
          await using var browser = await playwright.Chromium.LaunchAsync();
          var page = await browser.NewPageAsync();
          await page.SetViewportSizeAsync(opts.Width, opts.Height);
          await page.SetContentAsync(html, new() { WaitUntil = WaitUntilState.NetworkIdle });
          await page.ScreenshotAsync(new() { Path = opts.Out, FullPage = false });
          return opts.Out;
      }
  }
  ```

- [x] **3.2** Install Playwright's Chromium browser (one-time setup after first build):

  ```bash
  dotnet build
  pwsh src/ImgForge.Core/bin/Debug/net9.0/playwright.ps1 install chromium
  ```

- [x] **3.3** Write integration tests in `tests/ImgForge.Tests/ImageGeneratorTests.cs`:

  - Tag all tests `[Trait("Category", "Integration")]`.
  - Generate an image using a minimal inline HTML template to a temp file path.
  - Assert the output file exists.
  - Assert image dimensions match the requested width and height (use `SixLabors.ImageSharp`
    to read the PNG header without loading pixel data).
  - Clean up the temp file in `Dispose` / `finally`.

- [x] **3.4** Run integration tests in isolation to confirm browser rendering works:

  ```bash
  dotnet test --filter "Category=Integration"
  ```

---

## 4. CLI Entry Point

Wire the `System.CommandLine` surface to `ImageGenerator`. After this phase the tool is fully
functional end-to-end from the command line.

- [x] **4.1** Create `src/ImgForge/Commands/GenerateCommand.cs`:

  - Define a `generate` subcommand with options matching the CLI interface below.
  - Resolve `--overlay` as a multi-value option that can be repeated.
  - Instantiate `TemplateRenderer` and `ImageGenerator`, call `GenerateAsync`, and print the
    output path on success.
  - Return a non-zero exit code and write a descriptive message to `stderr` on any exception.

- [x] **4.2** Create `src/ImgForge/Program.cs` using the `System.CommandLine` root-command pattern:

  ```csharp
  var rootCommand = new RootCommand("ImgForge — generate images from HTML templates");
  rootCommand.AddCommand(GenerateCommand.Build());
  return await rootCommand.InvokeAsync(args);
  ```

- [x] **4.3** Validate all required options have defaults or clear error messages when omitted.

- [x] **4.4** Smoke-test the CLI end-to-end:

  ```bash
  dotnet run --project src/ImgForge -- generate \
    --template blog \
    --title "Hello ImgForge" \
    --bg https://picsum.photos/1200/630 \
    --out test-out.png
  ```

  Confirm `test-out.png` is created and is a valid 1200×630 PNG.

- [x] **4.5** Verify `--help` output is well-formed:

  ```bash
  dotnet run --project src/ImgForge -- generate --help
  ```

---

## 5. Built-in Templates, Tests, and Polish

Replace the placeholder templates with production-quality designs, add edge-case tests, and
prepare the package for distribution.

- [x] **5.1** Implement `src/ImgForge.Core/templates/blog.html` — 1200×630 OG/social preview:

  - Full-bleed background image (`background-size: cover`)
  - Semi-transparent dark scrim behind the title for readability
  - Centered `{{ title }}` in large sans-serif white text
  - Optional logo overlay anchored top-left via `{% for img in overlays %}`

- [x] **5.2** Implement `src/ImgForge.Core/templates/youtube.html` — 1280×720 thumbnail:

  - Full-bleed background image
  - Bold title bottom-left aligned with strong drop-shadow
  - Optional channel logo overlay anchored top-right

- [x] **5.3** Verify both built-in templates render correctly via smoke test:

  ```bash
  dotnet run --project src/ImgForge -- generate \
    --template youtube \
    --title "10 Tips for Clean Code" \
    --bg https://picsum.photos/1280/720 \
    --out youtube-test.png \
    --width 1280 --height 720
  ```

- [x] **5.4** Add a `.gitignore` entry for `*.png` output files generated during development
  (if not already covered).

- [x] **5.5** Add `<PackageReleaseNotes>` and `<Authors>` to `ImgForge.csproj` for NuGet
  package metadata completeness.

- [x] **5.6** Add a `global.json` at the repo root pinning the .NET SDK version:

  ```json
  {
    "sdk": {
      "version": "9.0.0",
      "rollForward": "latestMinor"
    }
  }
  ```

- [x] **5.7** Add a `.editorconfig` at the repo root enforcing consistent style (indent size,
  charset, final newline).

- [x] **5.8** Confirm the full test suite passes (unit + integration):

  ```bash
  dotnet test
  ```

- [x] **5.9** Do a local pack and install to verify the `dotnet tool install` flow:

  ```bash
  dotnet pack src/ImgForge/ImgForge.csproj -o ./nupkg
  dotnet tool install --global ImgForge --add-source ./nupkg
  imgforge generate --template blog --title "Packaged!" --out packaged-test.png
  dotnet tool uninstall --global ImgForge
  ```

---

## CLI Interface Reference

```bash
imgforge generate \
  --template blog \              # built-in name OR path to a .html file
  --title "Modular Monoliths Done Right" \
  --bg ./images/cover.jpg \      # local path or HTTP(S) URL; optional
  --overlay ./logo.png \         # optional; repeat for multiple overlays
  --out og.png \
  --width 1200 \                 # default: 1200
  --height 630                   # default: 630
```

---

## Template Variable Reference (Scriban syntax)

```html
<html>
<body style="width:{{ width }}px; height:{{ height }}px; margin:0;
             background-image:url('{{ bg }}'); background-size:cover;
             display:flex; align-items:center; justify-content:center;
             font-family:sans-serif; color:white;">
  <h1 style="font-size:64px; text-align:center;">{{ title }}</h1>
  {% for img in overlays %}
  <img src="{{ img.src }}" style="position:absolute; {{ img.style }}" />
  {% endfor %}
</body>
</html>
```

| Variable | Type | Description |
|---|---|---|
| `title` | `string` | Main heading text |
| `bg` | `string` | Background image URI (local `file:///` or HTTP URL) |
| `width` | `int` | Viewport/canvas width in pixels |
| `height` | `int` | Viewport/canvas height in pixels |
| `overlays` | `array` | List of `{ src, style }` overlay image objects |

---

## Key NuGet Packages

| Package | Project | Purpose |
|---|---|---|
| `Microsoft.Playwright` | Core | Headless Chromium rendering |
| `Scriban` | Core | Mustache-style template variable injection |
| `System.CommandLine` | CLI | Subcommand argument parsing |
| `SixLabors.ImageSharp` | Tests | Read PNG dimensions for integration assertions |
| `xunit` | Tests | Test framework |

---

## Final Verification Sequence

```bash
# 1. Clean build
dotnet build

# 2. Install Chromium (first time only)
pwsh src/ImgForge.Core/bin/Debug/net9.0/playwright.ps1 install chromium

# 3. Unit tests only (no browser required)
dotnet test --filter "Category!=Integration"

# 4. Full suite including integration tests
dotnet test

# 5. End-to-end CLI smoke test — blog template
dotnet run --project src/ImgForge -- generate \
  --template blog \
  --title "Hello ImgForge" \
  --bg https://picsum.photos/1200/630 \
  --out test-blog.png

# 6. End-to-end CLI smoke test — youtube template
dotnet run --project src/ImgForge -- generate \
  --template youtube \
  --title "Hello ImgForge" \
  --bg https://picsum.photos/1280/720 \
  --out test-youtube.png \
  --width 1280 --height 720
```

---

## Success Criteria

- `dotnet build` produces zero errors and zero warnings with `TreatWarningsAsErrors` enabled.
- All unit tests pass without a browser present (`Category!=Integration` filter).
- All integration tests pass when Chromium is installed.
- `test-blog.png` is a valid 1200×630 PNG created by the CLI.
- `test-youtube.png` is a valid 1280×720 PNG created by the CLI.
- `imgforge generate --help` displays clean, complete usage text.
- The tool can be packed, installed globally via `dotnet tool install`, and invoked as `imgforge`.
- Central Package Management is in place and no project file specifies a package version directly.
