using System.Globalization;
using System.Net;
using System.Text.Json;

namespace ImgForge.Core.RandomBackgrounds;

/// <summary>
/// Random photos from the <see href="https://pixabay.com/api/docs/">Pixabay API</see>. Needs a free API key, read from
/// the <c>PIXABAY_API_KEY</c> environment variable (100 requests per minute).
/// </summary>
/// <remarks>
/// Asks <c>pixabay.com/api/</c> for a random page of popular photos in the right orientation, picks one at random, and
/// downloads its image to a temp file (Pixabay doesn't allow hotlinking its images). The result carries an attribution
/// line naming the photographer, which Pixabay appreciates though it doesn't require it. Pixabay takes the key as a
/// query parameter, so it is only ever sent to <c>https://pixabay.com/api/</c>, and no message ever includes that URL.
/// </remarks>
internal sealed class PixabayProvider : IRandomBackgroundProvider
{
    /// <summary>The environment variable holding the Pixabay API key.</summary>
    public const string ApiKeyVariable = "PIXABAY_API_KEY";

    /// <summary>The provider's <see cref="Name"/>.</summary>
    public const string ProviderName = "pixabay.com";

    /// <summary>Photos per API page.</summary>
    internal const int PerPage = 20;

    /// <summary>
    /// The highest page asked for. Pixabay returns at most 500 hits for a query, so 10 pages of 20 stay well inside that.
    /// </summary>
    internal const int MaxPage = 10;

    private const string WebHost = "pixabay.com";
    private const string CdnHost = "cdn.pixabay.com";
    private const string WebUrl = "https://pixabay.com/";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    /// <param name="apiKey">The Pixabay API key. Must not be empty.</param>
    public PixabayProvider(string apiKey) : this(HttpImageDownloader.SharedClient, apiKey) { }

    /// <param name="httpClient">The client to use.</param>
    /// <param name="apiKey">The Pixabay API key. Must not be empty.</param>
    public PixabayProvider(HttpClient httpClient, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _httpClient = httpClient;
        _apiKey = apiKey.Trim();
    }

    public string Name => ProviderName;

    /// <summary>How long Pixabay has to start responding (to the API call, and again to the image download).</summary>
    public TimeSpan ResponseTimeout { get; init; } = HttpImageDownloader.DefaultResponseTimeout;

    /// <summary>Returns a random integer from 0 up to (not including) its argument. Replaceable for tests.</summary>
    internal Func<int, int> NextRandom { get; init; } = Random.Shared.Next;

    /// <summary>The API key from <see cref="ApiKeyVariable"/>, or <c>null</c> when it isn't set (or is only whitespace).</summary>
    public static string? ApiKeyFromEnvironment() => ApiProviderHelpers.KeyFromEnvironment(ApiKeyVariable);

    /// <summary>The search URL. Contains the key, so it must never be shown to the user.</summary>
    internal static Uri SearchUrl(string apiKey, int width, int height, int page)
    {
        var orientation = width > height ? "horizontal" : width < height ? "vertical" : "all";
        return new Uri(string.Create(CultureInfo.InvariantCulture,
            $"https://{WebHost}/api/?key={Uri.EscapeDataString(apiKey)}&image_type=photo&orientation={orientation}" +
            $"&safesearch=true&per_page={PerPage}&page={page}"));
    }

    public async Task<RandomBackgroundImage> FetchAsync(int width, int height, CancellationToken cancellationToken)
    {
        var hits = await SearchAsync(width, height, NextRandom(MaxPage) + 1, cancellationToken);
        if (hits.Count == 0)
            throw new RandomBackgroundProviderException("the site returned no photos");

        var photo = hits[NextRandom(hits.Count)];
        var image = await HttpImageDownloader.DownloadImageAsync(_httpClient, photo.ImageUrl, ResponseTimeout, cancellationToken);
        return image with { Attribution = photo.Attribution };
    }

    private async Task<IReadOnlyList<PixabayPhoto>> SearchAsync(
        int width, int height, int page, CancellationToken cancellationToken)
    {
        using var request = ApiProviderHelpers.JsonRequest(SearchUrl(_apiKey, width, height, page));
        using var response = await ApiProviderHelpers.SendAsync(_httpClient, request, ResponseTimeout, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new RandomBackgroundProviderException(
                DescribeFailure(response.StatusCode, ApiProviderHelpers.IsRateLimitExhausted(response)));

        return ParseHits(await ApiProviderHelpers.ReadJsonAsync(response.Content, cancellationToken));
    }

    /// <summary>A user-facing reason for a failed API call. Never includes the key, the URL, or the response body.</summary>
    /// <param name="status">The response status.</param>
    /// <param name="rateLimitExhausted">Pixabay reported that no requests are left.</param>
    internal static string DescribeFailure(HttpStatusCode status, bool rateLimitExhausted = false) => status switch
    {
        // Pixabay answers a missing or invalid key with 400.
        HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            $"the request was rejected (HTTP {(int)status}); check that {ApiKeyVariable} is a valid Pixabay API key",
        HttpStatusCode.TooManyRequests when rateLimitExhausted =>
            "the rate limit was reached (HTTP 429); try again in a minute",
        HttpStatusCode.TooManyRequests =>
            "too many requests (HTTP 429); try again later",
        _ => $"the site returned HTTP {(int)status}"
    };

    /// <summary>Reads the usable photos from a search response.</summary>
    /// <exception cref="RandomBackgroundProviderException">The JSON is malformed or has no <c>hits</c> array.</exception>
    /// <remarks>Hits without a usable image URL are skipped, so a response with only bad hits yields an empty list.</remarks>
    internal static IReadOnlyList<PixabayPhoto> ParseHits(byte[] json) => ApiProviderHelpers.Parse(json, root =>
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
            throw new RandomBackgroundProviderException(ApiProviderHelpers.UnexpectedResponse);

        var photos = new List<PixabayPhoto>();
        foreach (var hit in hits.EnumerateArray())
        {
            var imageUrl =
                ApiProviderHelpers.HttpsUrlOnHost(ApiProviderHelpers.GetString(hit, "largeImageURL"), WebHost, CdnHost) ??
                ApiProviderHelpers.HttpsUrlOnHost(ApiProviderHelpers.GetString(hit, "webformatURL"), WebHost, CdnHost);
            if (imageUrl is null)
                continue;

            var name = ApiProviderHelpers.GetString(hit, "user");
            // Only link to a page on pixabay.com.
            var page = ApiProviderHelpers.HttpsUrlOnHost(ApiProviderHelpers.GetString(hit, "pageURL"), WebHost);
            photos.Add(new PixabayPhoto(imageUrl, BuildAttribution(name, page)));
        }

        return photos;
    });

    /// <summary>The Pixabay credit line, e.g. "Photo by jane on Pixabay (https://pixabay.com/photos/...)".</summary>
    internal static string BuildAttribution(string? userName, Uri? pageUrl)
    {
        var link = pageUrl?.AbsoluteUri ?? WebUrl;
        var name = ApiProviderHelpers.CleanName(userName);
        return name.Length == 0 ? $"Photo from Pixabay ({link})" : $"Photo by {name} on Pixabay ({link})";
    }
}

/// <summary>The parts of a Pixabay search hit that ImgForge uses.</summary>
/// <param name="ImageUrl">The image to download (on pixabay.com or cdn.pixabay.com).</param>
/// <param name="Attribution">The credit line for the photographer.</param>
internal sealed record PixabayPhoto(Uri ImageUrl, string Attribution);
