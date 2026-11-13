using System.Net;
using System.Text;
using ImgForge.Core.RandomBackgrounds;

namespace ImgForge.Tests;

public class PixabayProviderTests : IDisposable
{
    private const string Key = "12345-test+pixabay&key";
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 7, 8, 9];

    private const string HitsJson = """
        {
          "total": 1000, "totalHits": 500,
          "hits": [
            { "id": 1, "pageURL": "https://pixabay.com/photos/one-1/", "user": "first",
              "largeImageURL": "https://pixabay.com/get/one_1280.jpg", "webformatURL": "https://pixabay.com/get/one_640.jpg" },
            { "id": 2, "pageURL": "https://pixabay.com/photos/two-2/", "user": "second",
              "largeImageURL": "https://pixabay.com/get/two_1280.jpg" }
          ]
        }
        """;

    private readonly string _tempDir = Directory.CreateTempSubdirectory("imgforge-test-").FullName;

    /// <summary>Answers /api/ with <paramref name="apiResponse"/> and any other URL with a JPEG.</summary>
    private static FakeHttpMessageHandler Pixabay(Func<HttpResponseMessage>? apiResponse = null) =>
        new((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/api/"
            ? apiResponse?.Invoke() ?? Json(HitsJson)
            : FakeHttpMessageHandler.Image(JpegBytes)));

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>A provider whose "random" numbers are always <paramref name="next"/> (clamped to the range).</summary>
    private static PixabayProvider NewProvider(FakeHttpMessageHandler handler, int next = 1) =>
        new(new HttpClient(handler), Key) { NextRandom = max => Math.Min(next, max - 1) };

    [Fact]
    public async Task FetchAsync_SearchesRandomPageAndDownloadsRandomHit()
    {
        var handler = Pixabay();
        var provider = NewProvider(handler);

        var image = await provider.FetchAsync(1280, 720, CancellationToken.None);

        Assert.Equal(JpegBytes, image.Content);
        Assert.Equal("pixabay.com", provider.Name);
        Assert.Collection(handler.Requests,
            api =>
            {
                Assert.Equal(
                    "https://pixabay.com/api/?key=12345-test%2Bpixabay%26key&image_type=photo&orientation=horizontal" +
                    "&safesearch=true&per_page=20&page=2",
                    api.RequestUri!.AbsoluteUri);
                Assert.Equal("application/json", Assert.Single(api.Headers.Accept).MediaType);
            },
            img => Assert.Equal("https://pixabay.com/get/two_1280.jpg", img.RequestUri!.AbsoluteUri));
    }

    [Theory]
    [InlineData(1280, 720, "horizontal")]
    [InlineData(720, 1280, "vertical")]
    [InlineData(1000, 1000, "all")]
    public void SearchUrl_OrientationMatchesSize(int width, int height, string orientation) =>
        Assert.Contains($"&orientation={orientation}&", PixabayProvider.SearchUrl(Key, width, height, 1).AbsoluteUri);

    [Fact]
    public async Task FetchAsync_RandomPageIsWithinMaxPage()
    {
        var handler = Pixabay();

        await NewProvider(handler, next: int.MaxValue).FetchAsync(1280, 720, CancellationToken.None);

        Assert.EndsWith($"&page={PixabayProvider.MaxPage}", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task FetchAsync_ReturnsAttributionLinkingThePhotoPage()
    {
        var image = await NewProvider(Pixabay(), next: 0).FetchAsync(1280, 720, CancellationToken.None);

        Assert.Equal("Photo by first on Pixabay (https://pixabay.com/photos/one-1/)", image.Attribution);
    }

    [Fact]
    public void ParseHits_NoLargeImage_FallsBackToWebformatAndSkipsUnusableHits()
    {
        var photos = PixabayProvider.ParseHits(Encoding.UTF8.GetBytes("""
            {"hits":[
              {"largeImageURL":"https://evil.example/a.jpg"},
              {"webformatURL":"https://cdn.pixabay.com/photo/b_640.jpg","user":"b"},
              {"largeImageURL":"http://pixabay.com/get/c.jpg"}
            ]}
            """));

        var photo = Assert.Single(photos);
        Assert.Equal("https://cdn.pixabay.com/photo/b_640.jpg", photo.ImageUrl.AbsoluteUri);
        Assert.Equal("Photo by b on Pixabay (https://pixabay.com/)", photo.Attribution);
    }

    [Theory]
    [InlineData("""{"hits":[]}""")]
    [InlineData("""{"hits":[{"largeImageURL":"https://evil.example/a.jpg"}]}""")]
    public async Task FetchAsync_NoUsableHits_Throws(string json)
    {
        var handler = Pixabay(() => Json(json));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => NewProvider(handler).FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal("the site returned no photos", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest,
        "the request was rejected (HTTP 400); check that PIXABAY_API_KEY is a valid Pixabay API key")]
    [InlineData(HttpStatusCode.Forbidden,
        "the request was rejected (HTTP 403); check that PIXABAY_API_KEY is a valid Pixabay API key")]
    [InlineData(HttpStatusCode.TooManyRequests, "too many requests (HTTP 429); try again later")]
    [InlineData(HttpStatusCode.InternalServerError, "the site returned HTTP 500")]
    public async Task FetchAsync_ApiError_ThrowsClearMessageWithoutKey(HttpStatusCode status, string expected)
    {
        var handler = Pixabay(() => new HttpResponseMessage(status)
        {
            Content = new StringContent($"[ERROR {(int)status}] Invalid or missing API key ({Key})")
        });

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => NewProvider(handler).FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal(expected, ex.Message);
        Assert.DoesNotContain("12345", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchAsync_RateLimitRemainingZero_SaysTheLimitWasReached()
    {
        var handler = Pixabay(() =>
        {
            var response = Json("{}", HttpStatusCode.TooManyRequests);
            response.Headers.Add("X-RateLimit-Remaining", "0");
            return response;
        });

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => NewProvider(handler).FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal("the rate limit was reached (HTTP 429); try again in a minute", ex.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"total":0}""")]
    [InlineData("""{"hits":{}}""")]
    [InlineData("""{"hits":[{"largeImageURL":"https://pixabay.com/get/a.jpg","user":"a\uD800b"}]}""")]
    public async Task FetchAsync_UnexpectedApiResponse_Throws(string json)
    {
        var handler = Pixabay(() => Json(json));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => NewProvider(handler).FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal("the site returned an unexpected response", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchAsync_NetworkError_DoesNotExposeKeyThroughResolver()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            throw new HttpRequestException("No such host is known. (pixabay.com:443)"));
        var next = new StubRandomBackgroundProvider((_, _, _) =>
            Task.FromResult(new RandomBackgroundImage(JpegBytes, "image/jpeg")), "next.example");

        var resolved = await new RandomBackgroundResolver(NewProvider(handler), next) { TempDirectory = _tempDir }
            .ResolveToFileAsync(1280, 720);

        var failure = Assert.Single(resolved.Failures);
        Assert.Equal("pixabay.com", failure.ProviderName);
        Assert.DoesNotContain("12345", failure.Reason);
        Assert.DoesNotContain("12345", RandomBackgroundResolver.BuildFallbackWarning(resolved));
    }

    [Fact]
    public async Task FetchAsync_ApiDoesNotRespond_ThrowsAfterResponseTimeout()
    {
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException();
        });
        var provider = new PixabayProvider(new HttpClient(handler), Key) { ResponseTimeout = TimeSpan.FromMilliseconds(100) };

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal("the site did not respond within 0.1 seconds", ex.Message);
    }

    [Theory]
    [InlineData(null, null, "Photo from Pixabay (https://pixabay.com/)")]
    [InlineData("jane‮\u001b", null, "Photo by jane on Pixabay (https://pixabay.com/)")]
    [InlineData("jane", "https://pixabay.com/photos/x-1/", "Photo by jane on Pixabay (https://pixabay.com/photos/x-1/)")]
    public void BuildAttribution_CleansNameAndLinksPage(string? name, string? page, string expected) =>
        Assert.Equal(expected, PixabayProvider.BuildAttribution(name, page is null ? null : new Uri(page)));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyKey_Throws(string key) =>
        Assert.Throws<ArgumentException>(() => new PixabayProvider(new HttpClient(), key));

    [Fact]
    public async Task Resolver_PixabaySucceeds_CarriesAttribution()
    {
        var resolved = await new RandomBackgroundResolver(NewProvider(Pixabay())) { TempDirectory = _tempDir }
            .ResolveToFileAsync(1280, 720);

        Assert.Equal("pixabay.com", resolved.ProviderName);
        Assert.Equal("Photo by second on Pixabay (https://pixabay.com/photos/two-2/)", resolved.Attribution);
    }

    // ---- default order ----

    [Fact]
    public void DefaultProviders_AllKeys_PutsApiProvidersFirstInOrder() =>
        Assert.Collection(RandomBackgroundResolver.DefaultProviders("u", "pe", "pi"),
            p => Assert.IsType<UnsplashProvider>(p),
            p => Assert.IsType<PexelsProvider>(p),
            p => Assert.IsType<PixabayProvider>(p),
            p => Assert.IsType<PicsumProvider>(p),
            p => Assert.IsType<PlaceholderProvider>(p));

    [Fact]
    public void DefaultProviders_OnlyPixabayKey_SkipsOthers() =>
        Assert.Collection(RandomBackgroundResolver.DefaultProviders(null, " ", "pi"),
            p => Assert.IsType<PixabayProvider>(p),
            p => Assert.IsType<PicsumProvider>(p),
            p => Assert.IsType<PlaceholderProvider>(p));

    [Fact]
    public void DefaultProviders_OnlyPexelsKey_SkipsOthers() =>
        Assert.Collection(RandomBackgroundResolver.DefaultProviders(null, "pe", null),
            p => Assert.IsType<PexelsProvider>(p),
            p => Assert.IsType<PicsumProvider>(p),
            p => Assert.IsType<PlaceholderProvider>(p));

    // ---- --bg random:pixabay ----

    [Fact]
    public void Spec_RandomPixabayWithKey_CreatesPixabayProvider()
    {
        string? asked = null;
        var spec = RandomBackgroundSpec.Parse(" random:PIXABAY ", variable => { asked = variable; return Key; });

        Assert.Equal("PIXABAY_API_KEY", asked);
        Assert.Equal("--bg random:pixabay", spec.OptionText);
        Assert.DoesNotContain("12345", spec.ToString());
        Assert.Equal("pixabay.com", Assert.IsType<PixabayProvider>(spec.CreateProvider(new HttpClient())).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Spec_RandomPixabayWithoutKey_ThrowsExplainingTheVariable(string? key)
    {
        var ex = Assert.Throws<ArgumentException>(() => RandomBackgroundSpec.Parse("random:pixabay", _ => key));

        Assert.Equal(
            "Invalid --bg value \"random:pixabay\": Pixabay needs an API key. Set the PIXABAY_API_KEY " +
            "environment variable to your Pixabay API key (free at https://pixabay.com/api/docs/).",
            ex.Message);
    }

    [Fact]
    public void Spec_RandomPixabayWithOption_Throws() =>
        Assert.Equal("Invalid --bg value \"random:pixabay:x\": pixabay doesn't take an option. Use \"random:pixabay\".",
            Assert.Throws<ArgumentException>(() => RandomBackgroundSpec.Parse("random:pixabay:x", _ => Key)).Message);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);
}
