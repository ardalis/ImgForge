using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ImgForge.Core.RandomBackgrounds;

/// <summary>
/// Random photos from the <see href="https://unsplash.com/documentation#get-a-random-photo">Unsplash API</see>.
/// Needs a free access key, read from the <c>UNSPLASH_ACCESS_KEY</c> environment variable (50 requests per hour on
/// the free tier).
/// </summary>
/// <remarks>
/// Two steps: ask <c>api.unsplash.com/photos/random</c> for a photo (JSON), then download its image, sized and cropped
/// by Unsplash. As the API guidelines require, the photo's <c>download_location</c> is then triggered and the result
/// carries an attribution line naming the photographer. The key is only ever sent to <c>https://api.unsplash.com</c>
/// and never appears in messages.
/// </remarks>
internal sealed class UnsplashProvider : IRandomBackgroundProvider
{
    /// <summary>The environment variable holding the Unsplash access key.</summary>
    public const string AccessKeyVariable = "UNSPLASH_ACCESS_KEY";

    /// <summary>The provider's <see cref="Name"/>.</summary>
    public const string ProviderName = "unsplash.com";

    /// <summary>The largest API (JSON) response accepted.</summary>
    internal const int MaxJsonBytes = 1024 * 1024;

    private const string ApiHost = "api.unsplash.com";
    private const string ImageHost = "images.unsplash.com";
    private const string WebHost = "unsplash.com";
    private const string ReferralQuery = "utm_source=imgforge&utm_medium=referral";

    private readonly HttpClient _httpClient;
    private readonly string _accessKey;

    /// <param name="accessKey">The Unsplash access key. Must not be empty.</param>
    public UnsplashProvider(string accessKey) : this(HttpImageDownloader.SharedClient, accessKey) { }

    /// <param name="httpClient">The client to use.</param>
    /// <param name="accessKey">The Unsplash access key. Must not be empty.</param>
    public UnsplashProvider(HttpClient httpClient, string accessKey)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessKey);
        _httpClient = httpClient;
        _accessKey = accessKey.Trim();
    }

    public string Name => ProviderName;

    /// <summary>How long Unsplash has to start responding (to the API call, and again to the image download).</summary>
    public TimeSpan ResponseTimeout { get; init; } = HttpImageDownloader.DefaultResponseTimeout;

    /// <summary>
    /// The access key from <see cref="AccessKeyVariable"/>, or <c>null</c> when it isn't set (or is only whitespace).
    /// </summary>
    public static string? AccessKeyFromEnvironment() =>
        Environment.GetEnvironmentVariable(AccessKeyVariable) is { } key && !string.IsNullOrWhiteSpace(key)
            ? key.Trim()
            : null;

    internal static Uri RandomPhotoUrl(int width, int height)
    {
        var orientation = width > height ? "landscape" : width < height ? "portrait" : "squarish";
        return new Uri($"https://{ApiHost}/photos/random?orientation={orientation}&content_filter=high");
    }

    public async Task<RandomBackgroundImage> FetchAsync(int width, int height, CancellationToken cancellationToken)
    {
        var photo = await GetRandomPhotoAsync(width, height, cancellationToken);
        var image = await HttpImageDownloader.DownloadImageAsync(
            _httpClient, SizedImageUrl(photo.RawUrl, width, height), ResponseTimeout, cancellationToken);
        await TriggerDownloadAsync(photo.DownloadLocation, cancellationToken);
        return image with { Attribution = photo.Attribution };
    }

    private async Task<UnsplashPhoto> GetRandomPhotoAsync(int width, int height, CancellationToken cancellationToken)
    {
        using var request = CreateApiRequest(RandomPhotoUrl(width, height));

        HttpResponseMessage response;
        using (var responseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            responseCts.CancelAfter(ResponseTimeout);
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, responseCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new RandomBackgroundProviderException(
                    $"the site did not respond within {HttpImageDownloader.FormatSeconds(ResponseTimeout)}");
            }
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new RandomBackgroundProviderException(
                    DescribeFailure(response.StatusCode, IsRateLimitExhausted(response)));

            var json = await ReadJsonAsync(response.Content, cancellationToken);
            return ParsePhoto(json);
        }
    }

    private HttpRequestMessage CreateApiRequest(Uri url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Client-ID", _accessKey);
        request.Headers.Add("Accept-Version", "v1");
        // Replaces the shared client's default "Accept: image/*" for this request.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>True when Unsplash says no requests are left this hour (<c>X-Ratelimit-Remaining: 0</c>).</summary>
    private static bool IsRateLimitExhausted(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Ratelimit-Remaining", out var values) &&
        values.FirstOrDefault()?.Trim() == "0";

    /// <summary>A user-facing reason for a failed API call. Never includes the key or the response body.</summary>
    /// <param name="status">The response status.</param>
    /// <param name="rateLimitExhausted">Unsplash reported that no requests are left this hour.</param>
    internal static string DescribeFailure(HttpStatusCode status, bool rateLimitExhausted = false) => status switch
    {
        HttpStatusCode.Unauthorized =>
            $"the access key in {AccessKeyVariable} was rejected (HTTP 401); check that it is a valid Unsplash access key",
        HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests when rateLimitExhausted =>
            $"the hourly rate limit was reached (HTTP {(int)status}); try again later",
        HttpStatusCode.TooManyRequests =>
            "too many requests (HTTP 429); try again later",
        HttpStatusCode.Forbidden =>
            "access was denied (HTTP 403); the hourly rate limit may have been reached, so try again later",
        _ => $"the site returned HTTP {(int)status}"
    };

    private static async Task<byte[]> ReadJsonAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxJsonBytes)
            throw new RandomBackgroundProviderException(UnexpectedResponse);

        using var buffer = new MemoryStream();
        try
        {
            await using var body = await content.ReadAsStreamAsync(cancellationToken);
            var chunk = new byte[16384];
            int read;
            while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxJsonBytes)
                    throw new RandomBackgroundProviderException(UnexpectedResponse);
                buffer.Write(chunk, 0, read);
            }
        }
        catch (IOException ex)
        {
            throw new RandomBackgroundProviderException(
                $"the connection was lost while reading the response ({ex.GetBaseException().Message.TrimEnd('.', ' ')})");
        }

        return buffer.ToArray();
    }

    /// <summary>Reads the fields ImgForge needs from a <c>/photos/random</c> response.</summary>
    /// <exception cref="RandomBackgroundProviderException">The JSON is malformed or missing a required field.</exception>
    internal static UnsplashPhoto ParsePhoto(byte[] json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var rawUrl = AbsoluteHttpsUrl(GetString(root, "urls", "raw"));
            if (rawUrl is null || !rawUrl.Host.Equals(ImageHost, StringComparison.OrdinalIgnoreCase))
                throw new RandomBackgroundProviderException(UnexpectedResponse);

            var downloadLocation = AbsoluteHttpsUrl(GetString(root, "links", "download_location"));
            if (downloadLocation is not null && !downloadLocation.Host.Equals(ApiHost, StringComparison.OrdinalIgnoreCase))
                downloadLocation = null; // Never send the access key anywhere but api.unsplash.com.

            var name = GetString(root, "user", "name");
            var profile = AbsoluteHttpsUrl(GetString(root, "user", "links", "html"));
            if (profile is not null && !profile.Host.Equals(WebHost, StringComparison.OrdinalIgnoreCase))
                profile = null; // Only link to the photographer's page on unsplash.com.
            return new UnsplashPhoto(rawUrl, downloadLocation, BuildAttribution(name, profile));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // InvalidOperationException: e.g. GetString() on a string with an invalid surrogate escape.
            throw new RandomBackgroundProviderException(UnexpectedResponse);
        }
    }

    /// <summary>The Unsplash-style credit line, e.g. "Photo by Jane Doe (https://unsplash.com/@jane?...) on Unsplash (...)".</summary>
    internal static string BuildAttribution(string? photographerName, Uri? profileUrl)
    {
        var unsplash = $"https://{WebHost}/?{ReferralQuery}";

        // The name comes from the API; drop control, format (e.g. right-to-left override), and line/paragraph
        // separator characters so it can't mess up or spoof the terminal line.
        var name = new string((photographerName ?? "").Where(IsPrintable).ToArray()).Trim();
        if (name.Length == 0)
            return $"Photo from Unsplash ({unsplash})";

        var who = profileUrl is null ? name : $"{name} ({WithReferral(profileUrl)})";
        return $"Photo by {who} on Unsplash ({unsplash})";
    }

    /// <summary>The raw image URL with Unsplash's sizing parameters, so Unsplash crops it to the requested size.</summary>
    internal static Uri SizedImageUrl(Uri rawUrl, int width, int height)
    {
        var sizing = string.Create(CultureInfo.InvariantCulture, $"w={width}&h={height}&fit=crop&fm=jpg&q=80");
        var query = rawUrl.Query.TrimStart('?');
        return new UriBuilder(rawUrl) { Query = query.Length == 0 ? sizing : $"{query}&{sizing}" }.Uri;
    }

    /// <summary>
    /// Tells Unsplash the photo was used, as its API guidelines require. Best effort: a failure or timeout here doesn't
    /// fail the image, which has already been downloaded. That includes the caller's token being cancelled (e.g. the
    /// resolver's per-provider budget running out), so a downloaded image isn't thrown away; a real cancellation by the
    /// user still stops the run when the resolver next uses its token.
    /// </summary>
    private async Task TriggerDownloadAsync(Uri? downloadLocation, CancellationToken cancellationToken)
    {
        if (downloadLocation is null)
            return;

        using var request = CreateApiRequest(downloadLocation);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ResponseTimeout);
        try
        {
            using var _ = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Ignored: the image is already downloaded.
        }
        catch (HttpRequestException)
        {
            // Ignored: the image is already downloaded.
        }
    }

    private static bool IsPrintable(char c) => char.GetUnicodeCategory(c) is not (
        UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or
        UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate);

    private static Uri WithReferral(Uri url)
    {
        var query = url.Query.TrimStart('?');
        return new UriBuilder(url) { Query = query.Length == 0 ? ReferralQuery : $"{query}&{ReferralQuery}" }.Uri;
    }

    private static Uri? AbsoluteHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps ? url : null;

    private static string? GetString(JsonElement element, params string[] path)
    {
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
                return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private const string UnexpectedResponse = "the site returned an unexpected response";
}

/// <summary>The parts of an Unsplash random-photo response that ImgForge uses.</summary>
/// <param name="RawUrl">The base image URL (on images.unsplash.com), before sizing parameters.</param>
/// <param name="DownloadLocation">The api.unsplash.com URL to trigger when the photo is used, if any.</param>
/// <param name="Attribution">The credit line for the photographer.</param>
internal sealed record UnsplashPhoto(Uri RawUrl, Uri? DownloadLocation, string Attribution);
