namespace ImgForge.Core;

public record GenerateOptions(
    string Template,
    string Title,
    string? Background,
    IReadOnlyList<OverlayImage> Overlays,
    string Out,
    int Width = 1200,
    int Height = 630
);

public record OverlayImage(string Src, string Style = "");
