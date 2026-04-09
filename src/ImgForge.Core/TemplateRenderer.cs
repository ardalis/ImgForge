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
        var templateSource = LoadTemplateSource(opts.Template);
        var bg = ResolveLocalPath(opts.Background);

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

        return template.Render(model, member => member.Name);
    }

    private static string LoadTemplateSource(string template)
    {
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
            return reader.ReadToEnd();
        }

        return File.ReadAllText(template);
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
