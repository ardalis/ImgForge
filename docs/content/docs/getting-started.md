---
title: "Getting Started"
weight: 10
---

# Getting Started

Get up and running with ImgForge in minutes.

## Installation

### Install as a Global Tool

The recommended way to install ImgForge is as a global .NET tool:

```bash
dotnet tool install --global ImgForge
```

After installation, you can use the `imgforge` command from anywhere:

```bash
imgforge generate --template blog --title "Hello!" --bg random --format blog
```

Chromium is downloaded automatically on the first run — no separate install step needed.

### Run Directly from NuGet via DNX

You can also run ImgForge without installing it globally using [DNX](https://github.com/dotnet/dotnet-exec):

```bash
# Simple example
dnx -y imgforge -- generate --template blog --title "Hello World" --bg random --format blog

# YouTube thumbnail
dnx -y imgforge -- generate --template youtube --title "My Video" --bg random --format youtube
```

The `-y` flag automatically accepts any prompts. Note the `--` before the arguments that should be sent to `imgforge`.

## Quick Examples

### Generate a Blog Post Open Graph Image

Create a social preview card with a random background:

```bash
imgforge generate --template blog --title "Getting Started with ImgForge" --bg random --format blog
```

Or use a specific background image:

```bash
imgforge generate --template blog --title "My Post Title" --bg ./cover.jpg --out og.png --format blog
```

### Create a YouTube Thumbnail

```bash
imgforge generate --template youtube --title "How to Build Better Software" --bg random --format youtube
```

### Generate a Blog Card with Subtitle and Headshot

```bash
imgforge generate --template blog-subtitle \
  --title "Advanced .NET Patterns" \
  --subtitle "A practical guide" \
  --bg random \
  --headshot ./photo.jpg \
  --format blog
```

All three built-in templates (`blog`, `youtube`, `blog-subtitle`) work immediately without any additional setup.

## Next Steps

- [Usage Guide](usage) — Learn about all available options and flags
- [Templates](templates) — Explore built-in templates and create your own
- [Format Presets](formats) — Understand dimension presets for different platforms
