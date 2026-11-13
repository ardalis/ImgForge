using System.Globalization;

namespace ImgForge.Core.RandomBackgrounds;

/// <summary>
/// Downloads the image for <c>--bg random</c> before the template is rendered, so a slow or failing provider
/// fails fast with a clear message instead of using up the page-load timeout.
/// </summary>
internal sealed class RandomBackgroundResolver(IRandomBackgroundProvider provider)
{
    public RandomBackgroundResolver() : this(new PicsumProvider()) { }

    /// <summary>How long the provider has to return the whole image.</summary>
    public TimeSpan ProviderTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Downloads a random image and saves it to a new temp file. The caller owns (and must delete) the file.</summary>
    /// <returns>The full path of the temp file.</returns>
    /// <exception cref="ImageLoadException">The provider failed, timed out, or didn't return an image.</exception>
    public async Task<string> ResolveToFileAsync(int width, int height, CancellationToken cancellationToken = default)
    {
        var image = await FetchAsync(width, height, cancellationToken);

        var path = Path.Combine(Path.GetTempPath(), $"imgforge-bg-{Guid.NewGuid():N}{ExtensionFor(image.MediaType)}");
        try
        {
            await File.WriteAllBytesAsync(path, image.Content, cancellationToken);
        }
        catch
        {
            TryDelete(path);
            throw;
        }

        return path;
    }

    private async Task<RandomBackgroundImage> FetchAsync(int width, int height, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProviderTimeout);

        try
        {
            return await provider.FetchAsync(width, height, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure($"the site did not respond within {FormatSeconds(ProviderTimeout)}");
        }
        catch (RandomBackgroundProviderException ex)
        {
            throw Failure(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            throw Failure($"the site could not be reached ({ex.Message})");
        }
    }

    private ImageLoadException Failure(string reason) => new(BuildErrorMessage(provider.Name, reason));

    internal static string BuildErrorMessage(string providerName, string reason) => string.Join(Environment.NewLine,
        $"Could not load a random background image from {providerName} (used by --bg random): {reason}.",
        "You can:",
        "  - Check your network connection, then try again later.",
        "  - Pass a different background with --bg (a local image file or another URL), " +
        "or omit --bg to use the template's built-in background design.");

    internal static string ExtensionFor(string mediaType) => mediaType switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        "image/avif" => ".avif",
        _ => ".jpg"
    };

    private static string FormatSeconds(TimeSpan t)
    {
        var value = t.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture);
        return $"{value} second{(t.TotalSeconds == 1 ? "" : "s")}";
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }
}
