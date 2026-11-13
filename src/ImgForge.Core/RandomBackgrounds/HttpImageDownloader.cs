using System.Net.Http.Headers;

namespace ImgForge.Core.RandomBackgrounds;

/// <summary>Shared HTTP plumbing for providers that download an image from a URL.</summary>
internal static class HttpImageDownloader
{
    /// <summary>Largest image accepted, so a misbehaving server can't exhaust memory or disk.</summary>
    internal const long MaxImageBytes = 50 * 1024 * 1024;

    /// <summary>
    /// One client for the process, so repeated calls don't exhaust sockets. Redirects are followed (Picsum
    /// redirects to its CDN). Timeouts come from the caller's cancellation token, not <see cref="HttpClient.Timeout"/>.
    /// </summary>
    internal static HttpClient SharedClient { get; } = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ImgForge", null));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        return client;
    }

    /// <summary>GETs <paramref name="url"/> and returns its body if it is a successful image response.</summary>
    /// <exception cref="RandomBackgroundProviderException">Non-success status, non-image, empty, or oversized response.</exception>
    public static async Task<RandomBackgroundImage> DownloadImageAsync(
        HttpClient client, Uri url, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new RandomBackgroundProviderException($"the site returned HTTP {(int)response.StatusCode}");

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new RandomBackgroundProviderException(
                $"the site responded, but not with an image (content type: {mediaType ?? "none"})");

        if (response.Content.Headers.ContentLength > MaxImageBytes)
            throw new RandomBackgroundProviderException(TooLarge);

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxImageBytes)
                throw new RandomBackgroundProviderException(TooLarge);
            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
            throw new RandomBackgroundProviderException("the site returned an empty image");

        return new RandomBackgroundImage(buffer.ToArray(), mediaType.ToLowerInvariant());
    }

    private static string TooLarge => $"the image is larger than {MaxImageBytes / (1024 * 1024)} MB";
}
