namespace ImgForge.Core.RandomBackgrounds;

/// <summary>
/// Downloads the image for <c>--bg random</c> before the template is rendered, so a slow or failing provider
/// fails fast with a clear message instead of using up the page-load timeout.
/// </summary>
/// <remarks>
/// Providers enforce their own short "start responding" timeout (about 5 seconds). The resolver adds an overall
/// cap on the whole download, so a large image on a slow connection still has time to arrive.
/// </remarks>
internal sealed class RandomBackgroundResolver(IRandomBackgroundProvider provider)
{
    public RandomBackgroundResolver() : this(new PicsumProvider()) { }

    /// <summary>The provider used, e.g. for diagnostics and tests.</summary>
    internal IRandomBackgroundProvider Provider => provider;

    /// <summary>The most time the whole download (response and body) may take.</summary>
    public TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Where downloaded images are saved (the system temp folder by default).</summary>
    internal string TempDirectory { get; init; } = Path.GetTempPath();

    /// <summary>Downloads a random image and saves it to a new temp file. The caller owns (and must delete) the file.</summary>
    /// <returns>The full path of the temp file.</returns>
    /// <exception cref="ImageLoadException">The provider failed, timed out, or didn't return an image.</exception>
    public async Task<string> ResolveToFileAsync(int width, int height, CancellationToken cancellationToken = default)
    {
        var image = await FetchAsync(width, height, cancellationToken);

        var path = Path.Combine(TempDirectory, $"imgforge-bg-{Guid.NewGuid():N}{ExtensionFor(image.MediaType)}");
        try
        {
            // CreateNew: never follow or overwrite something already at this path.
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await file.WriteAsync(image.Content, cancellationToken);
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
        timeout.CancelAfter(DownloadTimeout);

        try
        {
            return await provider.FetchAsync(width, height, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure($"the image did not finish downloading within {HttpImageDownloader.FormatSeconds(DownloadTimeout)}");
        }
        catch (RandomBackgroundProviderException ex)
        {
            throw Failure(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            throw Failure($"the site could not be reached ({ex.GetBaseException().Message.TrimEnd('.', ' ')})");
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

    /// <summary>Deletes a file, ignoring failures (e.g. a virus scanner holding it open).</summary>
    internal static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }
}
