namespace ImgForge.Core;

/// <summary>A remote request that failed while the rendered template was loading.</summary>
/// <param name="Url">The originally requested URL (before any redirects).</param>
/// <param name="Reason">A Chromium network error such as <c>net::ERR_NAME_NOT_RESOLVED</c>, or <c>HTTP 404</c>.</param>
internal sealed record FailedRequest(string Url, string Reason);

/// <summary>Where a remote resource referenced by the rendered page came from.</summary>
internal enum ResourceSource
{
    RandomBackground,
    Background,
    Headshot,
    Overlay,
    Template
}

/// <summary>A failed request attributed to the option (or template) that referenced it.</summary>
internal sealed record ResourceLoadFailure(ResourceSource Source, string Url, string Host, string Reason)
{
    public bool IsRequestedByUser => Source != ResourceSource.Template;
}

/// <summary>
/// Attributes failed remote requests to the CLI option that asked for them and builds the messages shown
/// to the user.
/// </summary>
internal static class ResourceLoadFailures
{
    private const string RandomBackgroundHost = "picsum.photos";

    /// <summary>
    /// Maps failed HTTP(S) requests to the option that referenced them. Requests that don't match a
    /// user-supplied URL are attributed to the template. Non-HTTP(S) requests are ignored.
    /// </summary>
    public static IReadOnlyList<ResourceLoadFailure> Classify(GenerateOptions opts, IEnumerable<FailedRequest> failed)
    {
        var requested = RequestedUrls(opts);
        var results = new List<ResourceLoadFailure>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var request in failed)
        {
            if (!TryParseHttp(request.Url, out var uri) || !seen.Add(MatchKey(uri)))
                continue;

            var source = requested.TryGetValue(MatchKey(uri), out var s) ? s : ResourceSource.Template;
            results.Add(new ResourceLoadFailure(source, uri.AbsoluteUri, uri.Host, request.Reason));
        }

        return results;
    }

    /// <summary>Builds the error for remote images the user asked for that failed to load.</summary>
    public static string BuildErrorMessage(IReadOnlyCollection<ResourceLoadFailure> failures)
    {
        var lines = new List<string>();

        if (failures.Count == 1)
        {
            var f = failures.First();
            lines.Add($"Could not load {Describe(f)}: {DescribeReason(f.Reason)}.");
        }
        else
        {
            lines.Add($"Could not load {failures.Count} requested images:");
            lines.AddRange(failures.Select(f => $"  - {Describe(f)}: {DescribeReason(f.Reason)}."));
        }

        lines.Add("You can:");
        lines.Add("  - Check your network connection and the URL, then try again later.");

        var sources = failures.Select(f => f.Source).ToHashSet();
        if (sources.Contains(ResourceSource.RandomBackground) || sources.Contains(ResourceSource.Background))
            lines.Add("  - Pass a different background with --bg (a local image file or another URL), " +
                      "or omit --bg to use the template's built-in background design.");
        if (sources.Contains(ResourceSource.Headshot))
            lines.Add("  - Pass a different image with --headshot (a local image file or another URL), or omit --headshot.");
        if (sources.Contains(ResourceSource.Overlay))
            lines.Add("  - Pass a different image with --overlay (a local image file or another URL), or remove that --overlay.");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Builds the warning for a remote resource referenced by the template itself.</summary>
    public static string BuildWarning(ResourceLoadFailure failure) =>
        $"Warning: Could not load {SafeDisplayUrl(failure.Url)} referenced by the template: " +
        $"{DescribeReason(failure.Reason)}. The image was generated without it.";

    private static Dictionary<string, ResourceSource> RequestedUrls(GenerateOptions opts)
    {
        var urls = new Dictionary<string, ResourceSource>(StringComparer.Ordinal);

        void Add(string? url, ResourceSource source)
        {
            if (TryParseHttp(url, out var uri))
                urls.TryAdd(MatchKey(uri), source);
        }

        if (TemplateRenderer.IsRandomBackground(opts.Background))
            Add(TemplateRenderer.RandomBackgroundUrl(opts.Width, opts.Height), ResourceSource.RandomBackground);
        else
            Add(opts.Background, ResourceSource.Background);

        Add(opts.Headshot?.Src, ResourceSource.Headshot);

        foreach (var overlay in opts.Overlays)
            Add(overlay.Src, ResourceSource.Overlay);

        return urls;
    }

    /// <summary>
    /// A comparison key that treats the URL the user typed and the URL Chromium reports as equal even when
    /// they're spelled differently: punycode vs. Unicode host names, default ports, and percent-encoding
    /// (Chromium escapes some characters, such as apostrophes, that .NET leaves as-is). Fragments are never
    /// sent, so they're ignored.
    /// </summary>
    private static string MatchKey(Uri uri) =>
        $"{uri.Scheme}://{uri.IdnHost.ToLowerInvariant()}:{uri.Port}" +
        Uri.UnescapeDataString(uri.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped));

    private static string Describe(ResourceLoadFailure f) => f.Source switch
    {
        ResourceSource.RandomBackground => $"a random background image from {RandomBackgroundHost} (used by --bg random)",
        ResourceSource.Background => $"the --bg background image from {f.Host}",
        ResourceSource.Headshot => $"the --headshot image from {f.Host}",
        ResourceSource.Overlay => $"the --overlay image from {f.Host}",
        _ => SafeDisplayUrl(f.Url)
    };

    private static string DescribeReason(string reason)
    {
        if (reason.StartsWith("HTTP ", StringComparison.Ordinal))
            return $"the site returned {reason}";

        // Chromium blocks responses to image requests that aren't images (typically an HTML error page).
        if (reason is "net::ERR_BLOCKED_BY_ORB" or "net::ERR_BLOCKED_BY_RESPONSE")
            return $"the site responded, but not with an image; it may have returned an error page ({reason})";

        if (IsUnreachable(reason))
            return $"the site could not be reached ({reason})";

        return $"the request failed ({reason})";
    }

    private static readonly string[] UnreachablePrefixes =
    [
        "net::ERR_NAME_", "net::ERR_CONNECTION_", "net::ERR_ADDRESS_", "net::ERR_INTERNET_",
        "net::ERR_NETWORK_", "net::ERR_PROXY_", "net::ERR_TUNNEL_", "net::ERR_SSL_", "net::ERR_CERT_"
    ];

    private static bool IsUnreachable(string reason) =>
        UnreachablePrefixes.Any(p => reason.StartsWith(p, StringComparison.Ordinal))
        || reason is "net::ERR_TIMED_OUT" or "net::ERR_EMPTY_RESPONSE" or "net::ERR_INVALID_HTTP_RESPONSE";

    /// <summary>Scheme, host, port and path only, so credentials or tokens in the URL aren't echoed.</summary>
    private static string SafeDisplayUrl(string url) =>
        TryParseHttp(url, out var uri)
            ? $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}{uri.AbsolutePath}"
            : url;

    private static bool TryParseHttp(string? url, out Uri uri)
    {
        if (Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }
}
