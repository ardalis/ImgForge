---
title: "Introduction"
weight: 1
---

A .NET CLI tool for generating blog and YouTube thumbnail images from HTML templates.

## What is ImgForge?

ImgForge is a command-line tool that creates images from HTML templates with customizable text overlays. It's perfect for:

- **Blog post Open Graph images** — Social preview cards for sharing on Twitter, LinkedIn, Facebook
- **YouTube thumbnails** — Eye-catching video preview images
- **Podcast artwork** — Show and episode art
- **GitHub repository previews** — Social preview cards for repositories

## How It Works

ImgForge follows a three-stage pipeline:

1. **Template rendering** — An HTML template (built-in or custom) is loaded and variable placeholders are replaced with the supplied values (title, background, dimensions, overlays) using [Scriban](https://github.com/scriban/scriban).
2. **Browser rendering** — The resulting HTML is loaded into a headless Chromium browser via [PuppeteerSharp](https://github.com/hardkoded/puppeteer-sharp), which applies all CSS layout and styling exactly as a real browser would.
3. **Screenshot capture** — PuppeteerSharp screenshots the rendered page at the specified viewport size and writes a PNG to the output path.

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
ImageGenerator    (PuppeteerSharp: loads HTML in headless Chromium, screenshots to PNG)
      │
      ▼
    output.png
```

## Next Steps

- [Getting Started](../getting-started) — Install and run your first command
- [Usage Guide](../usage) — Learn all the available options
- [Templates](../usage/templates) — Understand built-in and custom templates
