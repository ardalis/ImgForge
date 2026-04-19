# ImgForge Documentation

This directory contains the Hugo-based documentation site for ImgForge.

## Running the Documentation Site

```bash
cd docs
dotnet run --file serve.cs
```

The site will be available at http://localhost:1315

## Theme

The documentation uses the [Hugo Geekdoc](https://github.com/thegeeklab/hugo-geekdoc) theme, which is automatically downloaded when you run `serve.cs`.

## Structure

- `config/` - Hugo configuration files
- `content/` - Documentation content (markdown files)
- `serve.cs` - C# script to run Hugo server with automatic theme download
- `go.mod` - Go module file for Hugo modules
