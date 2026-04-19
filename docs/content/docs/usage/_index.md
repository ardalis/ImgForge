---
title: "Usage"
weight: 20
---

Complete reference for the `imgforge generate` command.

## Basic Command Structure

```bash
imgforge generate \
  --template blog \             # built-in name or path to .html file/folder
  --title "My Title" \
  --bg ./cover.jpg \            # optional background image
  --format blog \               # dimension preset
  --out og.png                  # output file path
```

## All Flags

| Flag | Required | Default | Description |
|---|---|---|---|
| `--template` | Yes | — | Built-in template name (`blog`, `youtube`, `blog-subtitle`) or path to a `.html` file or a directory containing `template.html` |
| `--title` | Yes | — | Main heading text injected into the template |
| `--subtitle` | No | — | Optional subtitle rendered below the title in templates that support it (e.g. `blog-subtitle`) |
| `--bg` | No | — | Background image: local file path, HTTP(S) URL, or `random` (fetches a random image from [picsum.photos](https://picsum.photos)) |
| `--overlay` | No | — | Overlay image path. Repeatable for multiple overlays |
| `--headshot` | No | — | Guest headshot image path placed in the template's headshot slot |
| `--headshot-filter` | No | `blue-mono` | Filter applied to the headshot. Built-in: `blue-mono`, `mono`, `none`. Or supply a raw CSS `filter` string |
| `--var` | No | — | Arbitrary template variable as `key=value` (e.g. `--var episode=42`). Accessible in templates as `{{ vars.episode }}`. Repeatable |
| `--format` | No | — | Output format preset that sets width and height. See [Format Presets](formats) for options |
| `--out` | No | title slug `.png` | Output PNG file path |
| `--out-dir` | No | `.` | Output directory. Filename is derived from `--title`. Ignored if `--out` is provided |
| `--width` | No | `1200` | Viewport width in pixels. Overrides `--format` |
| `--height` | No | `630` | Viewport height in pixels. Overrides `--format` |

## Background Images

The `--bg` flag supports three input types:

### Local File Path

```bash
imgforge generate --template blog --title "My Post" --bg ./images/cover.jpg --format blog
```

### HTTP(S) URL

```bash
imgforge generate --template blog --title "My Post" \
  --bg https://example.com/image.jpg --format blog
```

### Random Image

Use `random` to fetch a random image from [picsum.photos](https://picsum.photos):

```bash
imgforge generate --template blog --title "My Post" --bg random --format blog
```

## Headshot Images

Add a guest headshot with optional filters:

```bash
# Blue monochrome filter (default)
imgforge generate --template youtube --title "Building Better APIs" \
  --headshot ./guest.jpg --format youtube

# Greyscale filter
imgforge generate --template youtube --title "My Video" \
  --headshot ./guest.jpg --headshot-filter mono --format youtube

# No filter (original colors)
imgforge generate --template youtube --title "My Video" \
  --headshot ./guest.jpg --headshot-filter none --format youtube

# Custom CSS filter
imgforge generate --template youtube --title "My Video" \
  --headshot ./guest.jpg --headshot-filter "sepia(80%) hue-rotate(270deg)" --format youtube
```

### Built-in Headshot Filters

| Name | CSS Applied | Effect |
|---|---|---|
| `blue-mono` *(default)* | `grayscale(100%) sepia(100%) hue-rotate(190deg) saturate(300%) brightness(1.15)` | Light-blue monochrome — "black and white but in shades of blue" |
| `mono` | `grayscale(100%)` | True greyscale |
| `none` | `none` | No filter; original colours kept |

## Overlay Images

Add one or more overlay images:

```bash
# Single overlay
imgforge generate --template blog --title "My Post" \
  --overlay ./logo.png --format blog

# Multiple overlays
imgforge generate --template blog --title "My Post" \
  --overlay ./logo.png \
  --overlay ./badge.png \
  --format blog
```

## Custom Variables

Pass arbitrary key-value pairs to templates:

```bash
imgforge generate --template templates/podcast-episode.html \
  --title "Build vs Buy" \
  --var season=3 \
  --var episode=42 \
  --var show="My Podcast" \
  --format podcast-episode
```

In your template, access these as `{{ vars.season }}`, `{{ vars.episode }}`, etc.

## Output Options

### Specific File Path

```bash
imgforge generate --template blog --title "My Post" --out ./output/og-image.png --format blog
```

### Output Directory (Filename from Title)

```bash
imgforge generate --template blog --title "My Post Title" --out-dir ./output --format blog
# Creates: ./output/my-post-title.png
```

### Default (Current Directory)

```bash
imgforge generate --template blog --title "My Post Title" --format blog
# Creates: ./my-post-title.png
```

## Examples

### Blog Open Graph Image with Subtitle

```bash
imgforge generate --template blog-subtitle \
  --title "Next Level AI Agents" \
  --subtitle "MCP Servers" \
  --bg ./cover.jpg \
  --headshot ./logo.png \
  --format blog \
  --out og.png
```

### YouTube Thumbnail with Random Background

```bash
imgforge generate --template youtube --title "My Video Title" --bg random --format youtube
```

### Podcast Episode Art

```bash
imgforge generate --template templates/podcast-episode.html \
  --title "Build vs Buy with guest Ardalis" \
  --headshot ./guest.jpg \
  --headshot-filter blue-mono \
  --var season=3 \
  --var episode=42 \
  --var show="It Depends by NimblePros" \
  --out podcast-episode.png \
  --format podcast-episode
```

### Interactive Mode

Omit `--format` and `--width`/`--height` to be prompted interactively:

```bash
imgforge generate --template blog --title "My Post" --out out.png
```
