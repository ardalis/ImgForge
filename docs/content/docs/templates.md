---
title: "Templates"
weight: 30
---

# Templates

ImgForge supports both built-in templates and custom HTML templates.

## Built-in Templates

ImgForge includes **3 embedded templates** that are always available when you install the tool:

| Template Name | Default Dimensions | Description | Usage |
|---|---|---|---|
| `blog` | 1200×630 | Open Graph / social preview card with centered title | `--template blog` |
| `youtube` | 1280×720 | YouTube thumbnail with bold title styling | `--template youtube` |
| `blog-subtitle` | 1200×630 | Open Graph card with faded background, optional subtitle, and headshot support | `--template blog-subtitle` |

These templates are embedded in the tool's DLL and work immediately after installation—no need to download template files.

### Example Usage

```bash
# Simple blog Open Graph image
imgforge generate --template blog --title "My Blog Post" --bg random --format blog

# YouTube thumbnail
imgforge generate --template youtube --title "My Video Title" --bg random --format youtube

# Blog card with subtitle and headshot
imgforge generate --template blog-subtitle \
  --title "Building Modular Monoliths" \
  --subtitle "A Practical Guide" \
  --bg ./cover.jpg \
  --headshot ./author.jpg \
  --format blog
```

## Additional Repository Templates

The ImgForge repository's `templates/` folder contains additional example templates that are **not embedded** in the tool:

- `youtube-bold.html` — Bold YouTube thumbnail variant
- `podcast-episode.html` — Podcast episode artwork
- `podcast-show.html` — Podcast show artwork
- `github-social.html` — GitHub repository social preview
- `blog-gradient.html` — Blog card with gradient overlay

To use these templates:

### Option 1: Reference by File Path

Copy templates from the repository and reference them by path:

```bash
imgforge generate --template ./templates/youtube-bold.html --title "My Title" --format youtube
```

### Option 2: Clone and Run from Source

```bash
git clone https://github.com/ardalis/ImgForge.git
cd ImgForge

# Use repository templates
imgforge generate --template templates/podcast-episode.html \
  --title "My Episode" \
  --format podcast-episode
```

## Custom Templates

Any `.html` file (or a folder with a `template.html` file in it) can be used as a template.

### Simple Template Example

```html
<html>
<body style="width:{{ width }}px; height:{{ height }}px; margin:0;
             background-image:url('{{ bg }}'); background-size:cover;
             display:flex; align-items:center; justify-content:center;
             font-family:sans-serif; color:white;">
  <h1 style="font-size:64px; text-align:center;">{{ title }}</h1>
</body>
</html>
```

Save this as `my-template.html` and use it:

```bash
imgforge generate --template ./my-template.html \
  --title "Hello World" \
  --bg ./cover.jpg \
  --width 1200 \
  --height 630
```

## Template Variables

Templates use [Scriban](https://github.com/scriban/scriban) Liquid syntax for variable injection.

| Variable | Type | Description |
|---|---|---|
| `title` | `string` | Main heading text |
| `subtitle` | `string` | Optional subtitle text — empty string when `--subtitle` is not supplied; guard with `{% if subtitle %}` in templates |
| `bg` | `string` | Background image URI — local `file:///` paths, HTTP(S) URLs, and random picsum URLs are all resolved before injection |
| `width` | `int` | Viewport width in pixels |
| `height` | `int` | Viewport height in pixels |
| `overlays` | array | List of `{ src, style }` overlay image objects |
| `headshot` | object or `null` | Guest headshot — exposes `headshot.src` (file URI) and `headshot.filter_css` (CSS filter string). `null` when `--headshot` is not supplied |
| `vars` | object | Arbitrary key/value pairs supplied via `--var key=value`. Access as `{{ vars.key }}` |

### Using Variables in Templates

```html
<html>
<body style="width:{{ width }}px; height:{{ height }}px;">
  <h1>{{ title }}</h1>
  
  {% if subtitle %}
  <h2>{{ subtitle }}</h2>
  {% endif %}
  
  {% for img in overlays %}
  <img src="{{ img.src }}" style="position:absolute; {{ img.style }}" />
  {% endfor %}
  
  {% if headshot %}
  <img src="{{ headshot.src }}" style="filter:{{ headshot.filter_css }};" />
  {% endif %}
  
  {% if vars.season %}
  <p>Season {{ vars.season }}, Episode {{ vars.episode }}</p>
  {% endif %}
</body>
</html>
```

## Template Directories

When you pass a directory path to `--template`, ImgForge looks for `template.html` inside it and injects a `<base>` tag so that relative image references resolve correctly.

```text
my-template/
  template.html
  watermark.png   ← referenced as just "watermark.png" inside the template
  logo.svg
```

```bash
imgforge generate --template ./my-template --title "Hello" --format blog
```

Inside `template.html`, you can reference files in the same directory:

```html
<body>
  <img src="watermark.png" style="position:absolute; bottom:20px; right:20px;" />
  <img src="logo.svg" style="position:absolute; top:20px; left:20px;" />
</body>
```

## Default Template Path

If you omit `--template`, ImgForge uses `/.imgforge/index.html` as the default custom template path.
