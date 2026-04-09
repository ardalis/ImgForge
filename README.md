# ImgForge

A .NET CLI tool for generating blog and YouTube thumbnail images from HTML templates.

## How It Works

ImgForge follows a three-stage pipeline:

1. **Template rendering** — An HTML template (built-in or custom) is loaded and variable placeholders are replaced with the supplied values (title, background, dimensions, overlays) using [Scriban](https://github.com/scriban/scriban).
2. **Browser rendering** — The resulting HTML is loaded into a headless Chromium browser via [Microsoft.Playwright](https://playwright.dev/dotnet/), which applies all CSS layout and styling exactly as a real browser would.
3. **Screenshot capture** — Playwright screenshots the rendered page at the specified viewport size and writes a PNG to the output path.

```
GenerateOptions
      │
      ▼
TemplateRenderer  (Scriban: injects title, bg, width, height, overlays into HTML)
      │
      ▼
    HTML string
      │
      ▼
ImageGenerator    (Playwright: loads HTML in headless Chromium, screenshots to PNG)
      │
      ▼
    output.png
```

## Dependencies

| Package | Role |
|---|---|
| [Scriban](https://github.com/scriban/scriban) | Liquid-syntax template engine — substitutes `{{ title }}`, `{{ bg }}`, `{{ width }}`, `{{ height }}`, and `{% for img in overlays %}` in HTML templates |
| [Microsoft.Playwright](https://playwright.dev/dotnet/) | Headless Chromium driver — renders the HTML with full CSS support and captures a pixel-perfect screenshot |
| [System.CommandLine](https://github.com/dotnet/command-line-api) | Parses CLI arguments and subcommands |
| [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) | Used in tests to verify output PNG dimensions |
| [xunit](https://xunit.net/) | Test framework |

## Usage

```bash
imgforge generate \
  --template blog \              # built-in name ("blog", "youtube") or path to a .html file or folder (with a template.html file in it)
  --title "Modular Monoliths Done Right" \
  --bg ./images/cover.jpg \      # local path or HTTP(S) URL; optional
  --overlay ./logo.png \         # optional; repeat for multiple overlays
  --headshot ./guest.jpg \       # optional guest headshot; placed in a styled circle
  --headshot-filter blue-mono \  # built-in: blue-mono (default), mono, none — or a raw CSS filter string
  --format podcast-episode \     # sets width/height from a named preset (see formats below)
  --out og.png \
  --width 1200 \                 # explicit width — overrides --format; prompted if neither is given
  --height 630                   # explicit height — overrides --format; prompted if neither is given
```

### Format presets

Use `--format <name>` instead of `--width`/`--height`. Explicit dimensions always override the preset.
If neither `--format` nor explicit dimensions are provided, the tool prompts you interactively.

| `--format` value | Width | Height | Use case |
|---|---|---|---|
| `youtube` | 1280 | 720 | YouTube video thumbnail |
| `blog` / `og` | 1200 | 630 | Blog post / Open Graph social card |
| `github` | 1280 | 640 | GitHub repository social preview |
| `podcast-show` | 3000 | 3000 | Podcast show art |
| `podcast-episode` | 3000 | 3000 | Podcast episode art |

## Image Dimension Reference

Ideal dimensions vary by platform and use case. Use `--width` and `--height` to match:

| Use Case | Width | Height | Aspect Ratio | Template | Notes |
|---|---|---|---|---|---|
| **YouTube Video Thumbnail** | 1280 | 720 | 16:9 | `templates/youtube-bold.html` | Minimum 640×360; displayed at up to 1280×720 |
| **Blog / Open Graph (og:image)** | 1200 | 630 | ~1.91:1 | `templates/blog-gradient.html` | Recommended by Facebook, LinkedIn, Slack, and most social crawlers. Twitter also accepts this size. |
| **GitHub Repository Social Preview** | 1280 | 640 | 2:1 | `templates/github-social.html` | Displayed at 640×320 on repository pages |
| **Podcast Show Art** | 3000 | 3000 | 1:1 | `templates/podcast-show.html` | Apple Podcasts minimum 1400×1400; Spotify and most platforms prefer 3000×3000, max 512 KB |
| **Podcast Episode Art** | 3000 | 3000 | 1:1 | `templates/podcast-episode.html` | Same requirements as show art; some hosts fall back to show art if omitted |

### Quick reference commands

```bash
# YouTube thumbnail (using --format preset)
imgforge generate --template templates/youtube-bold.html --title "My Video Title" --bg ./bg.jpg --out thumb.png --format youtube

# YouTube thumbnail with guest headshot (blue monochrome filter)
imgforge generate --template templates/youtube-bold.html --title "Building Better APIs" \
  --headshot ./guest.jpg --out thumb.png --format youtube

# Blog / Open Graph card
imgforge generate --template templates/blog-gradient.html --title "My Post Title" --bg ./cover.jpg --out og.png --format blog

# GitHub social preview
imgforge generate --template templates/github-social.html --title "my-repo" --bg ./cover.jpg --out social-preview.png --format github

# Podcast show art
imgforge generate --template templates/podcast-show.html --title "My Podcast" --out podcast-show.png --format podcast-show

# Podcast episode art with guest headshot, season/episode labels, and named show in footer
imgforge generate --template templates/podcast-episode.html \
  --title "Build vs Buy with guest Ardalis" \
  --headshot ./guest.jpg --headshot-filter blue-mono \
  --var season=3 --var episode=42 --var show="It Depends by NimblePros" \
  --out podcast-episode.png --format podcast-episode

# Podcast episode art — greyscale headshot instead of blue tint
imgforge generate --template templates/podcast-episode.html --title "My Episode Title" \
  --headshot ./guest.jpg --headshot-filter mono --out podcast-episode.png --format podcast-episode

# Omit --format and --width/--height to be prompted interactively
imgforge generate --template templates/podcast-episode.html --title "My Episode Title" --out out.png

# Omit --out to use title for output name
imgforge generate --template templates/youtube-bold.html --title "This is the title of the show" --format youtube
```

## Built-in Templates

| Name | Default dimensions | Description |
|---|---|---|
| `blog` | 1200×630 | Open Graph / social preview card |
| `youtube` | 1280×720 | YouTube thumbnail |

## Custom Templates

Any `.html` file (or a folder with a `template.html` file in it) can be used as a template. Scriban Liquid syntax is supported for variable injection:

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
| `bg` | `string` | Background image URI — local `file:///` paths and HTTP(S) URLs are both accepted |
| `width` | `int` | Viewport width in pixels |
| `height` | `int` | Viewport height in pixels |
| `overlays` | array | List of `{ src, style }` overlay image objects |
| `headshot` | object or `null` | Guest headshot — exposes `headshot.src` (file URI) and `headshot.filter_css` (CSS filter string). `null` when `--headshot` is not supplied. |

### Headshot filters

Pass a name to `--headshot-filter` or supply any raw [CSS `filter`](https://developer.mozilla.org/en-US/docs/Web/CSS/filter) string.

| Name | CSS applied | Effect |
|---|---|---|
| `blue-mono` *(default)* | `grayscale(100%) sepia(100%) hue-rotate(190deg) saturate(300%) brightness(1.15)` | Light-blue monochrome — "black and white but in shades of blue" |
| `mono` | `grayscale(100%)` | True greyscale |
| `none` | `none` | No filter; original colours kept |
| *(custom)* | *(your string)* | Any valid CSS filter string, e.g. `sepia(80%) hue-rotate(270deg)` |

## Build and Test

```bash
dotnet build

# Install Playwright's Chromium browser (first time only)
pwsh src/ImgForge.Core/bin/Debug/net10.0/.playwright/package/playwright.ps1 install chromium

# Unit tests (no browser required)
dotnet test --filter "Category!=Integration"

# Full test suite including integration tests
dotnet test
```

## Install as a Global Tool

```bash
dotnet tool install --global ImgForge
imgforge generate --template blog --title "Hello!" --out hello.png
```

## Run Directly from NuGet via DNX

```bash
dnx -y imgforge generate --template blog --title "Hello!" --out hello.png
```
