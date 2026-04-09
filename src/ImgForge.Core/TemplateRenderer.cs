using Scriban;

namespace ImgForge.Core;

public class TemplateRenderer
{
    private static readonly IReadOnlyDictionary<string, string> FilterMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["blue-mono"] = "grayscale(100%) sepia(100%) hue-rotate(190deg) saturate(300%) brightness(1.15)",
            ["mono"]      = "grayscale(100%)",
            ["none"]      = "none"
        };

    public string Render(GenerateOptions opts)
    {
        var (templateSource, templateDir) = LoadTemplateSource(opts.Template);
        var bg = opts.Background?.Equals("random", StringComparison.OrdinalIgnoreCase) == true
            ? $"https://picsum.photos/{opts.Width}/{opts.Height}"
            : ResolveLocalPath(opts.Background);

        var template = Template.ParseLiquid(templateSource);

        var overlays = opts.Overlays
            .Select(o => new OverlayModel(o.Src, o.Style))
            .ToList();

        HeadshotModel? headshot = null;
        if (opts.Headshot is not null)
        {
            var src = ResolveLocalPath(opts.Headshot.Src) ?? opts.Headshot.Src;
            var filterCss = FilterMap.TryGetValue(opts.Headshot.Filter, out var css)
                ? css
                : opts.Headshot.Filter; // allow raw CSS filter strings
            headshot = new HeadshotModel(src, filterCss);
        }

        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (opts.Vars is not null)
            foreach (var (k, v) in opts.Vars)
                vars[k] = v;

        var model = new RenderModel(opts.Title, bg ?? string.Empty, opts.Width, opts.Height, overlays, headshot, vars);

        var rendered = template.Render(model, member => member.Name);

        if (templateDir is not null)
            rendered = InjectBaseTag(rendered, templateDir);

        return rendered;
    }

    /// <summary>
    /// Loads a template and returns its HTML source plus the directory it lives in
    /// (or null for built-in embedded resources, which have no file-system directory).
    /// </summary>
    /// <remarks>
    /// Three template forms are supported:
    /// <list type="bullet">
    ///   <item>Built-in name (e.g. "blog") — embedded resource, no base directory.</item>
    ///   <item>Path to a .html file — file is read directly; base dir is the file's parent directory.</item>
    ///   <item>Path to a directory — must contain a "template.html" file; base dir is the directory itself.</item>
    /// </list>
    /// </remarks>
    private static (string source, string? templateDir) LoadTemplateSource(string template)
    {
        // Template folder: a directory containing template.html
        if (Directory.Exists(template))
        {
            var templateFile = Path.Combine(template, "template.html");
            if (!File.Exists(templateFile))
                throw new FileNotFoundException(
                    $"Template folder '{template}' does not contain a 'template.html' file.",
                    templateFile);
            return (File.ReadAllText(templateFile), Path.GetFullPath(template));
        }

        bool isBuiltIn = !template.Contains('/') &&
                         !template.Contains('\\') &&
                         !template.EndsWith(".html", StringComparison.OrdinalIgnoreCase);

        if (isBuiltIn)
        {
            var resourceName = $"ImgForge.Core.templates.{template}.html";
            var assembly = typeof(TemplateRenderer).Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException($"Built-in template '{template}' not found.", resourceName);
            using var reader = new StreamReader(stream);
            return (reader.ReadToEnd(), null);
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(template));
        return (File.ReadAllText(template), dir);
    }

    /// <summary>
    /// Injects a &lt;base&gt; tag into the HTML so that relative URLs in the template
    /// (e.g. a watermark image sitting next to template.html) resolve against the
    /// template's directory rather than the temporary file location used by the browser.
    /// </summary>
    private static string InjectBaseTag(string html, string templateDir)
    {
        var basePath = templateDir.Replace('\\', '/');
        if (!basePath.EndsWith('/'))
            basePath += '/';
        var baseTag = $"<base href=\"file:///{basePath}\">";

        var headIndex = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        if (headIndex >= 0)
        {
            var insertPos = headIndex + "<head>".Length;
            return html.Insert(insertPos, "\n  " + baseTag);
        }

        // No <head> element — prepend the tag so it still takes effect.
        return baseTag + "\n" + html;
    }

    private static string? ResolveLocalPath(string? path)
    {
        if (path is null)
            return null;

        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var fullPath = Path.GetFullPath(path);
        return $"file:///{fullPath.Replace('\\', '/')}";
    }
}

internal record OverlayModel(string src, string style);

internal record HeadshotModel(string src, string filter_css);

internal record RenderModel(
    string title,
    string bg,
    int width,
    int height,
    IReadOnlyList<OverlayModel> overlays,
    HeadshotModel? headshot,
    Dictionary<string, string> vars
);
