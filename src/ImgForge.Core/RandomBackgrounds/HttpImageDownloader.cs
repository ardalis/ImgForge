using System.Globalization;
using System.Net.Http.Headers;

namespace ImgForge.Core.RandomBackgrounds;

/// <summary>Shared HTTP plumbing for providers that download an image from a URL.</summary>
internal static class HttpImageDownloader
{
    /// <summary>Largest image accepted, so a misbehaving server can't exhaust memory or disk.</summary>
    internal const long MaxImageBytes = 50 * 1024 * 1024;

    /// <summary>How long a site has to start responding (send headers) before we give up on it.</summary>
    internal static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Raster formats Chromium can draw as a background. Anything else (HTML, SVG, ...) is rejected.</summary>
    private static readonly HashSet<string> SupportedMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif", "image/avif"
    };

    /// <summary>
    /// One client for the process, so repeated calls don't exhaust sockets. Pooled connections are recycled so DNS
    /// changes are picked up when used from a long-running process. Redirects are followed (Picsum redirects to its
    /// CDN). Timeouts come from cancellation tokens, not <see cref="HttpClient.Timeout"/>.
    /// </summary>
    internal static HttpClient SharedClient { get; } = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ImgForge", null));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        return client;
    }

    /// <summary>GETs <paramref name="url"/> and returns its body if it is a successful image response.</summary>
    /// <param name="responseTimeout">How long the site has to start responding. The body download is bounded only by
    /// <paramref name="cancellationToken"/>, so large images on slow connections aren't cut off.</param>
    /// <exception cref="RandomBackgroundProviderException">
    /// No response in time, or a non-success, non-image, empty, or oversized response.
    /// </exception>
    public static async Task<RandomBackgroundImage> DownloadImageAsync(
        HttpClient client, Uri url, TimeSpan responseTimeout, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        using (var responseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            responseCts.CancelAfter(responseTimeout);
            try
            {
                response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, responseCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new RandomBackgroundProviderException($"the site did not respond within {FormatSeconds(responseTimeout)}");
            }
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new RandomBackgroundProviderException($"the site returned HTTP {(int)response.StatusCode}");

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                throw new RandomBackgroundProviderException(
                    $"the site responded, but not with an image (content type: {mediaType ?? "none"})");
            if (!SupportedMediaTypes.Contains(mediaType))
                throw new RandomBackgroundProviderException(
                    $"the site returned an unsupported image type ({mediaType})");

            if (response.Content.Headers.ContentLength > MaxImageBytes)
                throw new RandomBackgroundProviderException(TooLarge);

            using var buffer = new MemoryStream();
            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                var chunk = new byte[81920];
                int read;
                while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
                {
                    if (buffer.Length + read > MaxImageBytes)
                        throw new RandomBackgroundProviderException(TooLarge);
                    buffer.Write(chunk, 0, read);
                }
            }
            catch (IOException ex)
            {
                // e.g. HttpIOException ("response ended prematurely") when the connection drops mid-download.
                throw new RandomBackgroundProviderException(
                    $"the connection was lost while downloading the image ({ex.GetBaseException().Message.TrimEnd('.', ' ')})");
            }

            if (buffer.Length == 0)
                throw new RandomBackgroundProviderException("the site returned an empty image");

            return new RandomBackgroundImage(buffer.ToArray(), mediaType.ToLowerInvariant());
        }
    }

    internal static string FormatSeconds(TimeSpan t)
    {
        var value = t.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture);
        return $"{value} second{(t.TotalSeconds == 1 ? "" : "s")}";
    }

    private static string TooLarge => $"the image is larger than {MaxImageBytes / (1024 * 1024)} MB";
}
