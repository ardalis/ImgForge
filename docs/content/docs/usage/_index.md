---
title: "Usage"
weight: 20
---

Complete reference for the `imgforge generate` command.

## Basic Command Structure

```bash
imgforge generate \
  --template blog \             # built-in name or path to .html file/folder; optional if a default template exists
  --title "My Title" \
  --bg ./cover.jpg \            # optional background image
  --format blog \               # dimension preset
  --out og.png                  # output file path
```

## All Flags

| Flag | Required | Default | Description |
|---|---|---|---|
| `--template` | No | `.tools/imgforge/template.html` | Built-in template name (`blog`, `youtube`, `blog-subtitle`) or path to a `.html` file or a directory containing `template.html`. If omitted, a default template is looked up (see [Default Template Path](templates/#default-template-path)); it's an error if none is found |
| `--title` | Yes | — | Main heading text injected into the template |
| `--subtitle` | No | — | Optional subtitle rendered below the title in templates that support it (e.g. `blog-subtitle`) |
| `--bg` | No | — | Background image: local file path, HTTP(S) URL, `random` (downloads a random image from [picsum.photos](https://picsum.photos) before rendering, falling back to a solid-color placeholder from [placehold.co](https://placehold.co); Unsplash, Pexels, and Pixabay are tried first when `UNSPLASH_ACCESS_KEY`, `PEXELS_API_KEY`, or `PIXABAY_API_KEY` is set), or `random:<provider>` to use one provider only (see [Random Image](#random-image)) |
| `--overlay` | No | — | Overlay image source. Repeatable for multiple overlays. Passed to the template as-is (see [Overlay Images](#overlay-images)) |
| `--headshot` | No | — | Guest headshot image (local path or HTTP(S) URL) placed in the template's headshot slot |
| `--headshot-filter` | No | `blue-mono` | Filter applied to the headshot. Built-in: `blue-mono`, `mono`, `none`. Or supply a raw CSS `filter` string |
| `--var` | No | — | Arbitrary template variable as `key=value` (e.g. `--var episode=42`). Accessible in templates as `{{ vars.episode }}`. Repeatable |
| `--format` | No | — | Output format preset that sets width and height. See [Format Presets](formats) for options |
| `--out` | No | title slug `.png` | Output PNG file path |
| `--out-dir` | No | `.` | Output directory. Filename is derived from `--title`. Ignored if `--out` is provided |
| `--width` | No | from `--format` | Viewport width in pixels. Overrides `--format`. With no `--format`, `--width`, or `--height`, you're prompted to pick a format; if only `--height` is given, width falls back to `1200` |
| `--height` | No | from `--format` | Viewport height in pixels. Overrides `--format`. With no `--format`, `--width`, or `--height`, you're prompted to pick a format; if only `--width` is given, height falls back to `630` |

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

Use `random` to download a random image from [picsum.photos](https://picsum.photos). If picsum.photos fails, ImgForge
uses a solid dark-slate image from [placehold.co](https://placehold.co) instead, so you still get an image. If
an API key is set for Unsplash, Pexels, or Pixabay, those are tried first (see [Unsplash](#unsplash-optional-api-key)
and [Pexels and Pixabay](#pexels-and-pixabay-optional-api-keys)):

```bash
imgforge generate --template blog --title "My Post" --bg random --format blog
```

To use one provider only, use `random:<provider>`. There's no fallback, so if that provider fails, the command fails
with an error naming it (use plain `random` to try each provider in turn):

| `--bg` value | Image |
|---|---|
| `random` | Default: [picsum.photos](https://picsum.photos), falling back to [placehold.co](https://placehold.co) (Unsplash, Pexels, and Pixabay first when their keys are set) |
| `random:picsum` | A random photo from picsum.photos |
| `random:loremflickr` | A random Flickr photo from [loremflickr.com](https://loremflickr.com) matching `abstract` (currently unreliable, see below) |
| `random:loremflickr:<keyword>` | A loremflickr.com photo matching a keyword, or several separated by commas (e.g. `technology` or `city,night`) |
| `random:placeholder` | A solid dark-slate (`#1e293b`) image from placehold.co |
| `random:placeholder:<color>` | A solid image in a 6-digit hex color (e.g. `0f172a` or `#0F172A`) |
| `random:unsplash` | A random photo from the [Unsplash API](https://unsplash.com/developers) (needs `UNSPLASH_ACCESS_KEY`, see below) |
| `random:pexels` | A curated photo from the [Pexels API](https://www.pexels.com/api/) (needs `PEXELS_API_KEY`, see below) |
| `random:pixabay` | A popular photo from the [Pixabay API](https://pixabay.com/api/docs/) (needs `PIXABAY_API_KEY`, see below) |

```bash
imgforge generate --template blog --title "My Post" --bg random:placeholder:0f172a --format blog
```

LoremFlickr isn't in the default order because loremflickr.com image requests have been failing (HTTP 500 as of
November 2026). If you pick it while it's down, you get an error naming the HTTP status. An unknown provider name or an
invalid keyword or color fails right away, with an error listing what's valid. A local file whose name starts with
`random:` (possible on Linux and macOS) needs a path prefix, e.g. `--bg ./random:photo.jpg`.

#### Unsplash (optional API key)

Set the `UNSPLASH_ACCESS_KEY` environment variable to an Unsplash access key (free from
[unsplash.com/developers](https://unsplash.com/developers); 50 requests per hour on the free tier) to turn on the
Unsplash provider. Plain `random` then tries Unsplash first (Unsplash, then picsum.photos, then placehold.co), and
`random:unsplash` uses only Unsplash. Without the key, Unsplash is skipped, and `random:unsplash` fails right away
with an error asking you to set it.

```bash
export UNSPLASH_ACCESS_KEY=your-access-key   # PowerShell: $env:UNSPLASH_ACCESS_KEY = "your-access-key"
imgforge generate --template blog --title "My Post" --bg random --format blog
```

When the image comes from Unsplash, ImgForge prints a credit line to stderr, such as
`Photo by Jane Doe (https://unsplash.com/@janedoe?...) on Unsplash (https://unsplash.com/?...)`. Unsplash's
[API guidelines](https://help.unsplash.com/en/articles/2511245-unsplash-api-guidelines) ask you to credit the
photographer and Unsplash wherever you use the photo. If the key is rejected (HTTP 401) or the hourly rate limit is
reached (HTTP 403 or 429), plain `random` moves on to the next provider with a warning. The key is only sent to
`api.unsplash.com` and is never printed.

#### Pexels and Pixabay (optional API keys)

[Pexels](https://www.pexels.com/api/) and [Pixabay](https://pixabay.com/api/docs/) work the same way as Unsplash.
Set `PEXELS_API_KEY` and/or `PIXABAY_API_KEY` to a free API key to turn each one on. Plain `random` tries every
provider whose key is set, in this order: Unsplash, Pexels, Pixabay, then picsum.photos, then placehold.co.
`random:pexels` and `random:pixabay` use only that provider, and fail right away if its key isn't set.

```bash
export PEXELS_API_KEY=your-pexels-key     # PowerShell: $env:PEXELS_API_KEY = "your-pexels-key"
export PIXABAY_API_KEY=your-pixabay-key   # PowerShell: $env:PIXABAY_API_KEY = "your-pixabay-key"
imgforge generate --template blog --title "My Post" --bg random:pexels --format blog
```

- **Pexels** picks a photo from its curated collection, cropped by Pexels to the requested size, and ImgForge prints a
  credit line to stderr such as `Photo by Jane Doe (https://www.pexels.com/@janedoe) on Pexels (https://www.pexels.com)`,
  as the [Pexels guidelines](https://www.pexels.com/api/documentation/#guidelines) ask. The free tier allows 200
  requests per hour.
- **Pixabay** picks a popular photo in the right orientation (up to 1280 pixels wide, scaled to cover the template)
  and prints a credit line such as `Photo by janedoe on Pixabay (https://pixabay.com/photos/...)`. Pixabay doesn't
  require credit but appreciates it. ImgForge downloads the image rather than linking to it, as Pixabay's terms
  require. Pixabay takes the key in the request URL; ImgForge only sends it to `pixabay.com/api/` and never prints it.
  Pixabay asks API users to cache search results for 24 hours. ImgForge makes one search per run and doesn't cache
  results between runs, so if you generate many images in a row, keep within its limit of 100 requests per minute.

If a key is rejected or a rate limit is reached, plain `random` moves on to the next provider with a warning. Keys are
never printed.

This requires network access to the random image providers. ImgForge downloads the image before rendering the
template. Each provider gets 5 seconds to start responding and 30 seconds to finish the download. With plain
`random`, if a provider is unreachable, slow, or returns an HTTP error (or something that isn't an image), ImgForge
moves on to the next provider in its list (Unsplash, Pexels, and Pixabay if enabled, then picsum.photos, then placehold.co) and prints a warning naming the provider
that failed. If every provider fails, the command fails with an error that lists each provider and why it failed,
and no image is written. Other remote background URLs are loaded by the page, which has up to 30 seconds to finish
loading, and fail the same way. Try again later, use a local image or another URL with `--bg`, or omit `--bg` to use
the template's built-in background design.

### When Remote Images Fail to Load

- **Images you pass on the command line** (`--bg`, `--headshot`, `--overlay`): if a remote URL can't be reached
  (offline, DNS failure, connection refused) or returns an HTTP error such as 404, ImgForge stops with an
  error naming the option and site, exits with code 1, and writes no image.
- **Remote resources referenced by the template itself** (fonts, stylesheets, hard-coded images): a failure
  prints a `Warning:` line to stderr, and the image is still generated without that resource.

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
  --overlay https://example.com/logo.png --format blog

# Multiple overlays
imgforge generate --template blog --title "My Post" \
  --overlay https://example.com/logo.png \
  --overlay https://example.com/badge.png \
  --format blog
```

{{< hint type=note >}}
`--overlay` values are passed to the template unchanged (unlike `--bg` and `--headshot`, they are not converted to `file:///` URIs). The page is rendered from a temporary file, so a relative path like `./logo.png` won't resolve with the built-in templates. Use an HTTP(S) URL or a `file:///` URI; with a file or folder template, relative paths resolve against the template's folder.
{{< /hint >}}

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

Omit `--format` and `--width`/`--height` to be prompted to pick a format preset from a numbered list:

```bash
imgforge generate --template blog --title "My Post" --out out.png
```
