---
title: "Format Presets"
weight: 20
---

Use `--format <name>` to set width and height based on platform-specific presets. Explicit `--width` and `--height` always override the preset.

## Available Presets

| `--format` value | Width | Height | Use case |
|---|---|---|---|
| `youtube` | 1280 | 720 | YouTube video thumbnail |
| `blog` / `og` | 1200 | 630 | Blog post / Open Graph social card |
| `github` | 1280 | 640 | GitHub repository social preview |
| `podcast-show` | 3000 | 3000 | Podcast show art |
| `podcast-episode` | 3000 | 3000 | Podcast episode art |

## Usage Examples

### YouTube Thumbnail

```bash
imgforge generate --template youtube --title "My Video" --bg random --format youtube
```

This sets dimensions to 1280×720.

### Blog / Open Graph Card

```bash
imgforge generate --template blog --title "My Post" --bg ./cover.jpg --format blog
```

This sets dimensions to 1200×630.

### GitHub Social Preview

```bash
imgforge generate --template templates/github-social.html \
  --title "my-repo" \
  --bg ./cover.jpg \
  --out social-preview.png \
  --format github
```

This sets dimensions to 1280×640.

### Podcast Show Art

```bash
imgforge generate --template templates/podcast-show.html \
  --title "My Podcast" \
  --out podcast-show.png \
  --format podcast-show
```

This sets dimensions to 3000×3000.

## Image Dimension Reference

Ideal dimensions vary by platform and use case:

| Use Case | Width | Height | Aspect Ratio | Notes |
|---|---|---|---|---|
| **YouTube Video Thumbnail** | 1280 | 720 | 16:9 | Minimum 640×360; displayed at up to 1280×720 |
| **Blog / Open Graph (og:image)** | 1200 | 630 | ~1.91:1 | Recommended by Facebook, LinkedIn, Slack, and most social crawlers. Twitter also accepts this size. |
| **GitHub Repository Social Preview** | 1280 | 640 | 2:1 | Displayed at 640×320 on repository pages |
| **Podcast Show Art** | 3000 | 3000 | 1:1 | Apple Podcasts minimum 1400×1400; Spotify and most platforms prefer 3000×3000, max 512 KB |
| **Podcast Episode Art** | 3000 | 3000 | 1:1 | Same requirements as show art; some hosts fall back to show art if omitted |

## Overriding Presets

Use explicit `--width` and `--height` to override any preset:

```bash
# Use blog preset (1200×630) but override to custom dimensions
imgforge generate --template blog --title "My Post" \
  --format blog \
  --width 1000 \
  --height 500
```

## Interactive Mode

If you omit both `--format` and explicit dimensions, ImgForge prompts you interactively:

```bash
imgforge generate --template blog --title "My Post" --out out.png
# Prompts for width and height
```

## Quick Reference Commands

```bash
# YouTube thumbnail (using --format preset)
imgforge generate --template youtube --title "My Video Title" --bg ./bg.jpg --out thumb.png --format youtube

# YouTube thumbnail with a random background
imgforge generate --template youtube --title "My Video Title" --bg random --out thumb.png --format youtube

# Blog / Open Graph card
imgforge generate --template blog --title "My Post Title" --bg ./cover.jpg --out og.png --format blog

# GitHub social preview
imgforge generate --template templates/github-social.html --title "my-repo" --bg ./cover.jpg --out social-preview.png --format github

# Podcast show art
imgforge generate --template templates/podcast-show.html --title "My Podcast" --out podcast-show.png --format podcast-show

# Podcast episode art with custom variables
imgforge generate --template templates/podcast-episode.html \
  --title "Build vs Buy with guest Ardalis" \
  --headshot ./guest.jpg --headshot-filter blue-mono \
  --var season=3 --var episode=42 --var show="It Depends by NimblePros" \
  --out podcast-episode.png --format podcast-episode
```
