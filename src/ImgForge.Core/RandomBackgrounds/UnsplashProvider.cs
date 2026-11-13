using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

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
    internal const int MaxJsonBytes = ApiProviderHelpers.MaxJsonBytes;

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
    public static string? AccessKeyFromEnvironment() => ApiProviderHelpers.KeyFromEnvironment(AccessKeyVariable);

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
        using var response = await ApiProviderHelpers.SendAsync(_httpClient, request, ResponseTimeout, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new RandomBackgroundProviderException(
                DescribeFailure(response.StatusCode, ApiProviderHelpers.IsRateLimitExhausted(response)));

        var json = await ApiProviderHelpers.ReadJsonAsync(response.Content, cancellationToken);
        return ParsePhoto(json);
    }

    private HttpRequestMessage CreateApiRequest(Uri url)
    {
        var request = ApiProviderHelpers.JsonRequest(url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Client-ID", _accessKey);
        request.Headers.Add("Accept-Version", "v1");
        return request;
    }

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

    /// <summary>Reads the fields ImgForge needs from a <c>/photos/random</c> response.</summary>
    /// <exception cref="RandomBackgroundProviderException">The JSON is malformed or missing a required field.</exception>
    internal static UnsplashPhoto ParsePhoto(byte[] json) => ApiProviderHelpers.Parse(json, root =>
    {
        var rawUrl = ApiProviderHelpers.HttpsUrlOnHost(ApiProviderHelpers.GetString(root, "urls", "raw"), ImageHost)
            ?? throw new RandomBackgroundProviderException(ApiProviderHelpers.UnexpectedResponse);

        // Never send the access key anywhere but api.unsplash.com.
        var downloadLocation = ApiProviderHelpers.HttpsUrlOnHost(
            ApiProviderHelpers.GetString(root, "links", "download_location"), ApiHost);

        var name = ApiProviderHelpers.GetString(root, "user", "name");
        // Only link to the photographer's page on unsplash.com.
        var profile = ApiProviderHelpers.HttpsUrlOnHost(ApiProviderHelpers.GetString(root, "user", "links", "html"), WebHost);
        return new UnsplashPhoto(rawUrl, downloadLocation, BuildAttribution(name, profile));
    });

    /// <summary>The Unsplash-style credit line, e.g. "Photo by Jane Doe (https://unsplash.com/@jane?...) on Unsplash (...)".</summary>
    internal static string BuildAttribution(string? photographerName, Uri? profileUrl)
    {
        var unsplash = $"https://{WebHost}/?{ReferralQuery}";

        // The name comes from the API; drop control, format (e.g. right-to-left override), and line/paragraph
        // separator characters so it can't mess up or spoof the terminal line.
        var name = ApiProviderHelpers.CleanName(photographerName);
        if (name.Length == 0)
            return $"Photo from Unsplash ({unsplash})";

        var who = profileUrl is null ? name : $"{name} ({WithReferral(profileUrl)})";
        return $"Photo by {who} on Unsplash ({unsplash})";
    }

    /// <summary>The raw image URL with Unsplash's sizing parameters, so Unsplash crops it to the requested size.</summary>
    internal static Uri SizedImageUrl(Uri rawUrl, int width, int height)
    {
        var sizing = string.Create(CultureInfo.InvariantCulture, $"w={width}&h={height}&fit=crop&fm=jpg&q=80");
        return ApiProviderHelpers.AppendQuery(rawUrl, sizing);
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

    private static Uri WithReferral(Uri url) => ApiProviderHelpers.AppendQuery(url, ReferralQuery);
}

/// <summary>The parts of an Unsplash random-photo response that ImgForge uses.</summary>
/// <param name="RawUrl">The base image URL (on images.unsplash.com), before sizing parameters.</param>
/// <param name="DownloadLocation">The api.unsplash.com URL to trigger when the photo is used, if any.</param>
/// <param name="Attribution">The credit line for the photographer.</param>
internal sealed record UnsplashPhoto(Uri RawUrl, Uri? DownloadLocation, string Attribution);
