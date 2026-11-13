using System.Globalization;
using System.Net;
using System.Text.Json;

namespace ImgForge.Core.RandomBackgrounds;

/// <summary>
/// Random photos from the <see href="https://www.pexels.com/api/documentation/">Pexels API</see>. Needs a free API key,
/// read from the <c>PEXELS_API_KEY</c> environment variable (200 requests per hour on the free tier).
/// </summary>
/// <remarks>
/// Pexels has no "random photo" endpoint, so this asks <c>api.pexels.com/v1/curated</c> for one photo from a random page,
/// then downloads that photo's image, sized and cropped by Pexels. As the Pexels guidelines ask, the result carries an
/// attribution line naming the photographer and Pexels. The key is only ever sent to <c>https://api.pexels.com</c> and
/// never appears in messages.
/// </remarks>
internal sealed class PexelsProvider : IRandomBackgroundProvider
{
    /// <summary>The environment variable holding the Pexels API key.</summary>
    public const string ApiKeyVariable = "PEXELS_API_KEY";

    /// <summary>The provider's <see cref="Name"/>.</summary>
    public const string ProviderName = "pexels.com";

    /// <summary>The highest curated page asked for at first (one photo per page).</summary>
    internal const int MaxPage = 1000;

    private const string ApiHost = "api.pexels.com";
    private const string ImageHost = "images.pexels.com";
    private const string WebUrl = "https://www.pexels.com";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    /// <param name="apiKey">The Pexels API key. Must not be empty.</param>
    public PexelsProvider(string apiKey) : this(HttpImageDownloader.SharedClient, apiKey) { }

    /// <param name="httpClient">The client to use.</param>
    /// <param name="apiKey">The Pexels API key. Must not be empty.</param>
    public PexelsProvider(HttpClient httpClient, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _httpClient = httpClient;
        _apiKey = apiKey.Trim();
    }

    public string Name => ProviderName;

    /// <summary>How long Pexels has to start responding (to each API call, and to the image download).</summary>
    public TimeSpan ResponseTimeout { get; init; } = HttpImageDownloader.DefaultResponseTimeout;

    /// <summary>Returns a random integer from 0 up to (not including) its argument. Replaceable for tests.</summary>
    internal Func<int, int> NextRandom { get; init; } = Random.Shared.Next;

    /// <summary>The API key from <see cref="ApiKeyVariable"/>, or <c>null</c> when it isn't set (or is only whitespace).</summary>
    public static string? ApiKeyFromEnvironment() => ApiProviderHelpers.KeyFromEnvironment(ApiKeyVariable);

    internal static Uri CuratedUrl(int page) =>
        new(string.Create(CultureInfo.InvariantCulture, $"https://{ApiHost}/v1/curated?per_page=1&page={page}"));

    public async Task<RandomBackgroundImage> FetchAsync(int width, int height, CancellationToken cancellationToken)
    {
        var page = await GetCuratedPageAsync(NextRandom(MaxPage) + 1, cancellationToken);
        if (page.Photo is null && page.TotalResults > 0)
        {
            // The curated list is shorter than MaxPage photos: try again within it.
            page = await GetCuratedPageAsync(NextRandom(Math.Min(page.TotalResults, MaxPage)) + 1, cancellationToken);
        }

        var photo = page.Photo ?? throw new RandomBackgroundProviderException("the site returned no photos");
        var image = await HttpImageDownloader.DownloadImageAsync(
            _httpClient, SizedImageUrl(photo.OriginalUrl, width, height), ResponseTimeout, cancellationToken);
        return image with { Attribution = photo.Attribution };
    }

    private async Task<PexelsPage> GetCuratedPageAsync(int page, CancellationToken cancellationToken)
    {
        using var request = ApiProviderHelpers.JsonRequest(CuratedUrl(page));
        // Pexels takes the bare key as the Authorization value (no scheme).
        request.Headers.TryAddWithoutValidation("Authorization", _apiKey);
        using var response = await ApiProviderHelpers.SendAsync(_httpClient, request, ResponseTimeout, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new RandomBackgroundProviderException(
                DescribeFailure(response.StatusCode, ApiProviderHelpers.IsRateLimitExhausted(response)));

        return ParsePage(await ApiProviderHelpers.ReadJsonAsync(response.Content, cancellationToken));
    }

    /// <summary>A user-facing reason for a failed API call. Never includes the key or the response body.</summary>
    /// <param name="status">The response status.</param>
    /// <param name="rateLimitExhausted">Pexels reported that no requests are left.</param>
    internal static string DescribeFailure(HttpStatusCode status, bool rateLimitExhausted = false) => status switch
    {
        HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests when rateLimitExhausted =>
            $"the rate limit was reached (HTTP {(int)status}); try again later",
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            $"the API key in {ApiKeyVariable} was rejected (HTTP {(int)status}); check that it is a valid Pexels API key",
        HttpStatusCode.TooManyRequests =>
            "too many requests (HTTP 429); try again later",
        _ => $"the site returned HTTP {(int)status}"
    };

    /// <summary>Reads the first photo (if any) and the total count from a <c>/v1/curated</c> response.</summary>
    /// <exception cref="RandomBackgroundProviderException">The JSON is malformed or the photo is missing a required field.</exception>
    internal static PexelsPage ParsePage(byte[] json) => ApiProviderHelpers.Parse(json, root =>
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("photos", out var photos) || photos.ValueKind != JsonValueKind.Array)
            throw new RandomBackgroundProviderException(ApiProviderHelpers.UnexpectedResponse);

        var total = root.TryGetProperty("total_results", out var t) && t.TryGetInt32(out var n) ? Math.Max(n, 0) : 0;
        if (photos.GetArrayLength() == 0)
            return new PexelsPage(null, total);

        var first = photos[0];
        var original = ApiProviderHelpers.HttpsUrlOnHost(ApiProviderHelpers.GetString(first, "src", "original"), ImageHost)
            ?? throw new RandomBackgroundProviderException(ApiProviderHelpers.UnexpectedResponse);

        var name = ApiProviderHelpers.GetString(first, "photographer");
        // Only link to the photographer's page on pexels.com.
        var profile = ApiProviderHelpers.HttpsUrlOnHost(
            ApiProviderHelpers.GetString(first, "photographer_url"), "www.pexels.com", "pexels.com");
        return new PexelsPage(new PexelsPhoto(original, BuildAttribution(name, profile)), total);
    });

    /// <summary>The Pexels credit line, e.g. "Photo by Jane Doe (https://www.pexels.com/@jane) on Pexels (https://www.pexels.com)".</summary>
    internal static string BuildAttribution(string? photographerName, Uri? profileUrl)
    {
        var name = ApiProviderHelpers.CleanName(photographerName);
        if (name.Length == 0)
            return $"Photo from Pexels ({WebUrl})";

        var who = profileUrl is null ? name : $"{name} ({profileUrl.AbsoluteUri})";
        return $"Photo by {who} on Pexels ({WebUrl})";
    }

    /// <summary>The original image URL with Pexels' sizing parameters, so Pexels crops it to the requested size.</summary>
    internal static Uri SizedImageUrl(Uri originalUrl, int width, int height) =>
        ApiProviderHelpers.AppendQuery(originalUrl,
            string.Create(CultureInfo.InvariantCulture, $"auto=compress&cs=tinysrgb&fit=crop&w={width}&h={height}"));
}

/// <summary>One page of a Pexels curated-photos response.</summary>
/// <param name="Photo">The page's photo, or <c>null</c> when the page is empty.</param>
/// <param name="TotalResults">How many curated photos there are, as reported by Pexels.</param>
internal sealed record PexelsPage(PexelsPhoto? Photo, int TotalResults);

/// <summary>The parts of a Pexels photo that ImgForge uses.</summary>
/// <param name="OriginalUrl">The original image URL (on images.pexels.com), before sizing parameters.</param>
/// <param name="Attribution">The credit line for the photographer.</param>
internal sealed record PexelsPhoto(Uri OriginalUrl, string Attribution);
