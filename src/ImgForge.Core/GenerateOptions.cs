namespace ImgForge.Core;

public record GenerateOptions(
    string Template,
    string Title,
    string? Background,
    IReadOnlyList<OverlayImage> Overlays,
    string Out,
    int Width = 1200,
    int Height = 630,
    HeadshotOptions? Headshot = null,
    IReadOnlyDictionary<string, string>? Vars = null
);

public record OverlayImage(string Src, string Style = "");

public record HeadshotOptions(string Src, string Filter = "blue-mono");
