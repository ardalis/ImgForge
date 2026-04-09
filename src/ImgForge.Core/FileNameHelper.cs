using System.Text.RegularExpressions;

namespace ImgForge.Core;

public static partial class FileNameHelper
{
    [GeneratedRegex(@"[^a-z0-9\s-]")]
    private static partial Regex NonSlugCharacters();

    [GeneratedRegex(@"[\s-]+")]
    private static partial Regex WhitespaceOrHyphen();

    /// <summary>
    /// Converts a title string to a slug-style filename with a .png extension.
    /// Example: "Hello, World!" -> "hello-world.png"
    /// </summary>
    public static string TitleToFileName(string title)
    {
        var lower = title.ToLowerInvariant();
        var stripped = NonSlugCharacters().Replace(lower, "");
        var slugged = WhitespaceOrHyphen().Replace(stripped, "-").Trim('-');
        return slugged + ".png";
    }

    /// <summary>
    /// Resolves the output file path from command options.
    /// Priority: explicitOut > Path.Combine(outDir, slug) > slug in current directory.
    /// </summary>
    public static string ResolveOutputPath(string? explicitOut, string? outDir, string title)
    {
        if (explicitOut is not null) return explicitOut;
        var dir = outDir ?? ".";
        return Path.Combine(dir, TitleToFileName(title));
    }
}
