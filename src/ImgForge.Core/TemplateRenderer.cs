using Scriban;

namespace ImgForge.Core;

public class TemplateRenderer
{
    public string Render(GenerateOptions opts)
    {
        var templateSource = LoadTemplateSource(opts.Template);
        var bg = ResolveBackground(opts.Background);

        var template = Template.ParseLiquid(templateSource);

        var overlays = opts.Overlays
            .Select(o => new OverlayModel(o.Src, o.Style))
            .ToList();

        var model = new RenderModel(opts.Title, bg ?? string.Empty, opts.Width, opts.Height, overlays);

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

    private static string? ResolveBackground(string? background)
    {
        if (background is null)
            return null;

        if (background.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            background.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            background.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            return background;
        }

        var fullPath = Path.GetFullPath(background);
        return $"file:///{fullPath.Replace('\\', '/')}";
    }
}

internal record OverlayModel(string src, string style);

internal record RenderModel(
    string title,
    string bg,
    int width,
    int height,
    IReadOnlyList<OverlayModel> overlays
);
