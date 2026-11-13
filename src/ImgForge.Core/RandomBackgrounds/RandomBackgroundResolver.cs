namespace ImgForge.Core.RandomBackgrounds;

/// <summary>
/// Downloads the image for <c>--bg random</c> before the template is rendered, so a slow or failing provider
/// fails fast with a clear message instead of using up the page-load timeout.
/// </summary>
/// <remarks>
/// Providers are tried in order and the first one that returns a usable image wins. A provider that fails (HTTP
/// error, no response, non-image content, network error, or a download that takes too long) is skipped and the next
/// one is tried. Each provider enforces its own short "start responding" timeout (about 5 seconds), and the resolver
/// caps each provider's whole download at <see cref="DownloadTimeout"/>, so one dead provider can't use up the budget.
/// </remarks>
internal sealed class RandomBackgroundResolver
{
    private readonly IReadOnlyList<IRandomBackgroundProvider> _providers;

    /// <summary>Uses the default provider order.</summary>
    public RandomBackgroundResolver() : this(new PicsumProvider()) { }

    /// <param name="providers">The providers to try, in order. At least one is required.</param>
    public RandomBackgroundResolver(params IEnumerable<IRandomBackgroundProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var list = providers.ToArray();
        if (list.Length == 0)
            throw new ArgumentException("At least one random background provider is required.", nameof(providers));
        if (list.Any(p => p is null))
            throw new ArgumentException("Random background providers cannot be null.", nameof(providers));
        _providers = list;
    }

    /// <summary>The providers, in the order they are tried.</summary>
    internal IReadOnlyList<IRandomBackgroundProvider> Providers => _providers;

    /// <summary>The most time one provider's whole download (response and body) may take.</summary>
    public TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Where downloaded images are saved (the system temp folder by default).</summary>
    internal string TempDirectory { get; init; } = Path.GetTempPath();

    /// <summary>
    /// Downloads a random image from the first provider that succeeds and saves it to a new temp file.
    /// The caller owns (and must delete) the file.
    /// </summary>
    /// <exception cref="ImageLoadException">Every provider failed, timed out, or didn't return an image.</exception>
    public async Task<ResolvedRandomBackground> ResolveToFileAsync(
        int width, int height, CancellationToken cancellationToken = default)
    {
        var failures = new List<RandomBackgroundProviderFailure>();

        foreach (var provider in _providers)
        {
            var (image, failureReason) = await TryFetchAsync(provider, width, height, cancellationToken);
            if (image is null)
            {
                failures.Add(new RandomBackgroundProviderFailure(provider.Name, failureReason!));
                continue;
            }

            var path = await SaveAsync(image, cancellationToken);
            return new ResolvedRandomBackground(path, provider.Name, failures);
        }

        throw new ImageLoadException(BuildErrorMessage(failures));
    }

    private async Task<(RandomBackgroundImage? Image, string? FailureReason)> TryFetchAsync(
        IRandomBackgroundProvider provider, int width, int height, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DownloadTimeout);

        try
        {
            return (await provider.FetchAsync(width, height, timeout.Token), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, $"the image did not finish downloading within {HttpImageDownloader.FormatSeconds(DownloadTimeout)}");
        }
        catch (RandomBackgroundProviderException ex)
        {
            return (null, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return (null, $"the site could not be reached ({ex.GetBaseException().Message.TrimEnd('.', ' ')})");
        }
    }

    private async Task<string> SaveAsync(RandomBackgroundImage image, CancellationToken cancellationToken)
    {
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

    /// <summary>Builds the user-facing message for when every provider failed.</summary>
    internal static string BuildErrorMessage(IReadOnlyList<RandomBackgroundProviderFailure> failures)
    {
        var lines = new List<string>();
        if (failures.Count == 1)
        {
            lines.Add($"Could not load a random background image from {failures[0].ProviderName} " +
                $"(used by --bg random): {failures[0].Reason}.");
        }
        else
        {
            lines.Add("Could not load a random background image for --bg random. Every provider failed:");
            lines.AddRange(failures.Select(f => $"  - {f.ProviderName}: {f.Reason}"));
        }

        lines.Add("You can:");
        lines.Add("  - Check your network connection, then try again later.");
        lines.Add("  - Pass a different background with --bg (a local image file or another URL), " +
            "or omit --bg to use the template's built-in background design.");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The warning shown when an earlier provider failed but a later one supplied the image.</summary>
    internal static string BuildFallbackWarning(ResolvedRandomBackground resolved) =>
        "Warning: " +
        string.Join("; ", resolved.Failures.Select(f => $"{f.ProviderName} failed ({f.Reason})")) +
        $". Used {resolved.ProviderName} for --bg random instead.";

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

/// <summary>A random background saved to a temp file.</summary>
/// <param name="Path">The temp file. The caller owns (and must delete) it.</param>
/// <param name="ProviderName">The provider the image came from.</param>
/// <param name="Failures">Providers tried before <paramref name="ProviderName"/> that failed, in order.</param>
internal sealed record ResolvedRandomBackground(
    string Path, string ProviderName, IReadOnlyList<RandomBackgroundProviderFailure> Failures);

/// <summary>Why one provider couldn't supply a random background.</summary>
/// <param name="ProviderName">The provider's user-facing name.</param>
/// <param name="Reason">A user-facing reason such as "the site returned HTTP 522".</param>
internal sealed record RandomBackgroundProviderFailure(string ProviderName, string Reason);
