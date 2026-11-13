using System.Net;
using System.Text;
using ImgForge.Core.RandomBackgrounds;

namespace ImgForge.Tests;

public class UnsplashProviderTests : IDisposable
{
    private const string Key = "test-access-key-123";
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private const string PhotoJson = """
        {
          "id": "abc123",
          "urls": { "raw": "https://images.unsplash.com/photo-123?ixid=xyz&ixlib=rb-4.0.3", "full": "ignored" },
          "links": { "download_location": "https://api.unsplash.com/photos/abc123/download?ixid=xyz" },
          "user": { "name": "Jane Doe", "links": { "html": "https://unsplash.com/@janedoe" } }
        }
        """;

    private readonly string _tempDir = Directory.CreateTempSubdirectory("imgforge-test-").FullName;

    /// <summary>Answers the API with <paramref name="apiResponse"/>, images.unsplash.com with a JPEG, and the
    /// download_location with 200.</summary>
    private static FakeHttpMessageHandler Unsplash(Func<HttpResponseMessage>? apiResponse = null,
        Func<HttpResponseMessage>? downloadResponse = null) =>
        new((request, _) =>
        {
            var url = request.RequestUri!;
            if (url.Host == "images.unsplash.com")
                return Task.FromResult(FakeHttpMessageHandler.Image(JpegBytes));
            if (url.AbsolutePath.EndsWith("/download", StringComparison.Ordinal))
                return Task.FromResult(downloadResponse?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.OK));
            return Task.FromResult(apiResponse?.Invoke() ?? Json(PhotoJson));
        });

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task FetchAsync_CallsApiDownloadsSizedImageAndTriggersDownloadLocation()
    {
        var handler = Unsplash();
        var provider = new UnsplashProvider(new HttpClient(handler), Key);

        var image = await provider.FetchAsync(1280, 720, CancellationToken.None);

        Assert.Equal(JpegBytes, image.Content);
        Assert.Equal("image/jpeg", image.MediaType);
        Assert.Equal("unsplash.com", provider.Name);
        Assert.Collection(handler.Requests,
            api =>
            {
                Assert.Equal("https://api.unsplash.com/photos/random?orientation=landscape&content_filter=high",
                    api.RequestUri!.AbsoluteUri);
                Assert.Equal("Client-ID", api.Headers.Authorization!.Scheme);
                Assert.Equal(Key, api.Headers.Authorization.Parameter);
                Assert.Equal("v1", Assert.Single(api.Headers.GetValues("Accept-Version")));
                Assert.Equal("application/json", Assert.Single(api.Headers.Accept).MediaType);
            },
            img =>
            {
                Assert.Equal(
                    "https://images.unsplash.com/photo-123?ixid=xyz&ixlib=rb-4.0.3&w=1280&h=720&fit=crop&fm=jpg&q=80",
                    img.RequestUri!.AbsoluteUri);
                Assert.Null(img.Headers.Authorization);
            },
            download =>
            {
                Assert.Equal("https://api.unsplash.com/photos/abc123/download?ixid=xyz", download.RequestUri!.AbsoluteUri);
                Assert.Equal(Key, download.Headers.Authorization!.Parameter);
            });
    }

    [Fact]
    public async Task FetchAsync_ReturnsAttributionWithReferralLinks()
    {
        var provider = new UnsplashProvider(new HttpClient(Unsplash()), Key);

        var image = await provider.FetchAsync(1280, 720, CancellationToken.None);

        Assert.Equal(
            "Photo by Jane Doe (https://unsplash.com/@janedoe?utm_source=imgforge&utm_medium=referral) " +
            "on Unsplash (https://unsplash.com/?utm_source=imgforge&utm_medium=referral)",
            image.Attribution);
    }

    [Theory]
    [InlineData(1280, 720, "landscape")]
    [InlineData(720, 1280, "portrait")]
    [InlineData(1000, 1000, "squarish")]
    public void RandomPhotoUrl_OrientationMatchesSize(int width, int height, string orientation) =>
        Assert.Equal($"https://api.unsplash.com/photos/random?orientation={orientation}&content_filter=high",
            UnsplashProvider.RandomPhotoUrl(width, height).AbsoluteUri);

    [Fact]
    public void SizedImageUrl_RawUrlWithoutQuery_AddsSizing() =>
        Assert.Equal("https://images.unsplash.com/photo-1?w=800&h=600&fit=crop&fm=jpg&q=80",
            UnsplashProvider.SizedImageUrl(new Uri("https://images.unsplash.com/photo-1"), 800, 600).AbsoluteUri);

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized,
        "the access key in UNSPLASH_ACCESS_KEY was rejected (HTTP 401); check that it is a valid Unsplash access key")]
    [InlineData(HttpStatusCode.Forbidden,
        "access was denied (HTTP 403); the hourly rate limit may have been reached, so try again later")]
    [InlineData(HttpStatusCode.TooManyRequests, "too many requests (HTTP 429); try again later")]
    [InlineData(HttpStatusCode.InternalServerError, "the site returned HTTP 500")]
    public async Task FetchAsync_ApiError_ThrowsClearMessageWithoutKey(HttpStatusCode status, string expected)
    {
        var handler = Unsplash(() => Json("""{"errors":["Rate Limit Exceeded"]}""", status));
        var provider = new UnsplashProvider(new HttpClient(handler), Key);

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal(expected, ex.Message);
        Assert.DoesNotContain(Key, ex.Message);
        Assert.Single(handler.Requests); // No image download after a failed API call.
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"urls":{}}""")]
    [InlineData("""{"urls":{"raw":42}}""")]
    [InlineData("""{"urls":{"raw":"http://images.unsplash.com/photo-1"}}""")]
    [InlineData("""{"urls":{"raw":"https://evil.example/photo-1"}}""")]
    [InlineData("""{"urls":{"raw":"/relative/photo-1"}}""")]
    [InlineData("""{"urls":{"raw":"https://images.unsplash.com/photo-1"},"user":{"name":"a\uD800b"}}""")]
    public async Task FetchAsync_UnexpectedApiResponse_Throws(string json)
    {
        var handler = Unsplash(() => Json(json));
        var provider = new UnsplashProvider(new HttpClient(handler), Key);

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal("the site returned an unexpected response", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task FetchAsync_RateLimitRemainingZero_SaysTheHourlyLimitWasReached(HttpStatusCode status)
    {
        var handler = Unsplash(() =>
        {
            var response = Json("Rate Limit Exceeded", status);
            response.Headers.Add("X-Ratelimit-Remaining", "0");
            return response;
        });
        var provider = new UnsplashProvider(new HttpClient(handler), Key);

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal($"the hourly rate limit was reached (HTTP {(int)status}); try again later", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_ImageDownloadFails_DoesNotTriggerDownloadLocation()
    {
        var handler = new FakeHttpMessageHandler((request, _) => Task.FromResult(
            request.RequestUri!.Host == "images.unsplash.com" ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : IsDownloadLocation(request) ? new HttpResponseMessage(HttpStatusCode.OK)
            : Json(PhotoJson)));
        var provider = new UnsplashProvider(new HttpClient(handler), Key);

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal("the site returned HTTP 404", ex.Message);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, IsDownloadLocation);
    }

    [Fact]
    public async Task FetchAsync_CallerTokenCancelledDuringDownloadLocation_StillReturnsImage()
    {
        using var cts = new CancellationTokenSource();
        var handler = new FakeHttpMessageHandler(async (request, ct) =>
        {
            if (request.RequestUri!.Host == "images.unsplash.com")
                return FakeHttpMessageHandler.Image(JpegBytes);
            if (!IsDownloadLocation(request))
                return Json(PhotoJson);
            await cts.CancelAsync(); // e.g. the resolver's per-provider budget runs out now.
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException();
        });
        var provider = new UnsplashProvider(new HttpClient(handler), Key);

        var image = await provider.FetchAsync(1280, 720, cts.Token);

        Assert.Equal(JpegBytes, image.Content);
    }

    [Fact]
    public async Task FetchAsync_DownloadLocationDoesNotRespond_ReturnsImageAfterResponseTimeout()
    {
        var handler = new FakeHttpMessageHandler(async (request, ct) =>
        {
            if (request.RequestUri!.Host == "images.unsplash.com")
                return FakeHttpMessageHandler.Image(JpegBytes);
            if (!IsDownloadLocation(request))
                return Json(PhotoJson);
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException();
        });
        var provider = new UnsplashProvider(new HttpClient(handler), Key) { ResponseTimeout = TimeSpan.FromMilliseconds(100) };

        var image = await provider.FetchAsync(1280, 720, CancellationToken.None);

        Assert.Equal(JpegBytes, image.Content);
    }

    private static bool IsDownloadLocation(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/download", StringComparison.Ordinal);

    [Fact]
    public async Task FetchAsync_OversizedApiResponse_Throws()
    {
        var huge = "{\"pad\":\"" + new string('a', UnsplashProvider.MaxJsonBytes) + "\"}";
        var provider = new UnsplashProvider(new HttpClient(Unsplash(() => Json(huge))), Key);

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal("the site returned an unexpected response", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_DownloadLocationOnOtherHost_IsNotCalledSoTheKeyIsNotSentThere()
    {
        var json = PhotoJson.Replace("https://api.unsplash.com/photos/abc123/download", "https://evil.example/download");
        var handler = Unsplash(() => Json(json));
        var provider = new UnsplashProvider(new HttpClient(handler), Key);

        await provider.FetchAsync(1280, 720, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.Host == "evil.example");
    }

    [Fact]
    public async Task FetchAsync_DownloadLocationFails_StillReturnsImage()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith("/download", StringComparison.Ordinal)
                ? throw new HttpRequestException("connection reset")
                : Task.FromResult(request.RequestUri.Host == "images.unsplash.com"
                    ? FakeHttpMessageHandler.Image(JpegBytes)
                    : Json(PhotoJson)));
        var provider = new UnsplashProvider(new HttpClient(handler), Key);

        var image = await provider.FetchAsync(1280, 720, CancellationToken.None);

        Assert.Equal(JpegBytes, image.Content);
    }

    [Fact]
    public async Task FetchAsync_ApiDoesNotRespond_ThrowsAfterResponseTimeout()
    {
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException();
        });
        var provider = new UnsplashProvider(new HttpClient(handler), Key) { ResponseTimeout = TimeSpan.FromMilliseconds(100) };

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal("the site did not respond within 0.1 seconds", ex.Message);
    }

    [Fact]
    public void ParsePhoto_NoPhotographer_UsesGenericAttribution()
    {
        var photo = UnsplashProvider.ParsePhoto(Encoding.UTF8.GetBytes(
            """{"urls":{"raw":"https://images.unsplash.com/photo-1"}}"""));

        Assert.Null(photo.DownloadLocation);
        Assert.Equal("Photo from Unsplash (https://unsplash.com/?utm_source=imgforge&utm_medium=referral)", photo.Attribution);
    }

    [Theory]
    [InlineData("Jane\u001b[31m\nDoe", "Jane[31mDoe")]
    [InlineData("Jane\u202eeoD", "JaneeoD")]
    [InlineData("Jane\u2028Doe\u200b", "JaneDoe")]
    [InlineData("  Jane Doe  ", "Jane Doe")]
    public void BuildAttribution_StripsUnprintableCharactersFromName(string name, string expected) =>
        Assert.Equal($"Photo by {expected} on Unsplash (https://unsplash.com/?utm_source=imgforge&utm_medium=referral)",
            UnsplashProvider.BuildAttribution(name, null));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\u001b\n\u202e ")]
    public void BuildAttribution_NoPrintableName_UsesGenericLine(string? name) =>
        Assert.Equal("Photo from Unsplash (https://unsplash.com/?utm_source=imgforge&utm_medium=referral)",
            UnsplashProvider.BuildAttribution(name, new Uri("https://unsplash.com/@x")));

    [Fact]
    public void ParsePhoto_ProfileOnOtherHost_IsLeftOut()
    {
        var photo = UnsplashProvider.ParsePhoto(Encoding.UTF8.GetBytes(
            """{"urls":{"raw":"https://images.unsplash.com/photo-1"},"user":{"name":"Jane","links":{"html":"https://evil.example/jane"}}}"""));

        Assert.Equal("Photo by Jane on Unsplash (https://unsplash.com/?utm_source=imgforge&utm_medium=referral)", photo.Attribution);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyKey_Throws(string key) =>
        Assert.Throws<ArgumentException>(() => new UnsplashProvider(new HttpClient(), key));

    [Fact]
    public async Task Resolver_RateLimited_FallsThroughToNextProvider()
    {
        var unsplash = new UnsplashProvider(new HttpClient(Unsplash(() => Json("Rate Limit Exceeded", HttpStatusCode.Forbidden))), Key);
        var next = new StubRandomBackgroundProvider((_, _, _) =>
            Task.FromResult(new RandomBackgroundImage(JpegBytes, "image/jpeg")), "next.example");

        var resolved = await new RandomBackgroundResolver(unsplash, next) { TempDirectory = _tempDir }
            .ResolveToFileAsync(1280, 720);

        Assert.Equal("next.example", resolved.ProviderName);
        Assert.Null(resolved.Attribution);
        var failure = Assert.Single(resolved.Failures);
        Assert.Equal("unsplash.com", failure.ProviderName);
        Assert.StartsWith("access was denied (HTTP 403)", failure.Reason);
    }

    [Fact]
    public async Task Resolver_UnsplashSucceeds_CarriesAttribution()
    {
        var unsplash = new UnsplashProvider(new HttpClient(Unsplash()), Key);

        var resolved = await new RandomBackgroundResolver(unsplash) { TempDirectory = _tempDir }.ResolveToFileAsync(1280, 720);

        Assert.Equal("unsplash.com", resolved.ProviderName);
        Assert.StartsWith("Photo by Jane Doe", resolved.Attribution);
    }

    [Fact]
    public void DefaultProviders_WithKey_PutsUnsplashFirst() =>
        Assert.Collection(RandomBackgroundResolver.DefaultProviders(Key),
            p => Assert.IsType<UnsplashProvider>(p),
            p => Assert.IsType<PicsumProvider>(p),
            p => Assert.IsType<PlaceholderProvider>(p));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DefaultProviders_WithoutKey_SkipsUnsplash(string? key) =>
        Assert.Collection(RandomBackgroundResolver.DefaultProviders(key),
            p => Assert.IsType<PicsumProvider>(p),
            p => Assert.IsType<PlaceholderProvider>(p));

    // ---- --bg random:unsplash ----

    [Fact]
    public void Spec_RandomUnsplashWithKey_CreatesUnsplashProvider()
    {
        var spec = RandomBackgroundSpec.Parse("random:Unsplash", _ => Key);

        Assert.Equal("unsplash", spec.ProviderName);
        Assert.Equal("--bg random:unsplash", spec.OptionText);
        Assert.DoesNotContain(Key, spec.ToString());
        var provider = Assert.IsType<UnsplashProvider>(spec.CreateProvider(new HttpClient()));
        Assert.Equal("unsplash.com", provider.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Spec_RandomUnsplashWithoutKey_ThrowsExplainingTheVariable(string? key)
    {
        var ex = Assert.Throws<ArgumentException>(() => RandomBackgroundSpec.Parse("random:unsplash", _ => key));

        Assert.Equal(
            "Invalid --bg value \"random:unsplash\": Unsplash needs an access key. Set the UNSPLASH_ACCESS_KEY " +
            "environment variable to your Unsplash access key (free at https://unsplash.com/developers).",
            ex.Message);
    }

    [Fact]
    public void Spec_RandomUnsplashWithOption_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => RandomBackgroundSpec.Parse("random:unsplash:city", _ => Key));

        Assert.Equal("Invalid --bg value \"random:unsplash:city\": unsplash doesn't take an option. Use \"random:unsplash\".",
            ex.Message);
    }

    [Fact]
    public void Spec_OtherProvider_DoesNotReadKey() =>
        RandomBackgroundSpec.Parse("random:picsum", _ => throw new InvalidOperationException("key read"));

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);
}
