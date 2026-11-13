using System.Net;
using System.Text;
using ImgForge.Core.RandomBackgrounds;

namespace ImgForge.Tests;

public class PexelsProviderTests : IDisposable
{
    private const string Key = "test-pexels-key-456";
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 4, 5, 6];

    private const string PageJson = """
        {
          "page": 42, "per_page": 1, "total_results": 8000,
          "photos": [{
            "id": 2014422,
            "photographer": "Joey Farina",
            "photographer_url": "https://www.pexels.com/@joey",
            "src": { "original": "https://images.pexels.com/photos/2014422/pexels-photo-2014422.jpeg", "large": "ignored" }
          }]
        }
        """;

    private readonly string _tempDir = Directory.CreateTempSubdirectory("imgforge-test-").FullName;

    /// <summary>Answers the API with <paramref name="apiResponse"/> and images.pexels.com with a JPEG.</summary>
    private static FakeHttpMessageHandler Pexels(Func<HttpRequestMessage, HttpResponseMessage>? apiResponse = null) =>
        new((request, _) => Task.FromResult(request.RequestUri!.Host == "images.pexels.com"
            ? FakeHttpMessageHandler.Image(JpegBytes)
            : apiResponse?.Invoke(request) ?? Json(PageJson)));

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>A provider whose "random" numbers are always <paramref name="next"/> (clamped to the range).</summary>
    private static PexelsProvider NewProvider(FakeHttpMessageHandler handler, int next = 41) =>
        new(new HttpClient(handler), Key) { NextRandom = max => Math.Min(next, max - 1) };

    [Fact]
    public async Task FetchAsync_CallsCuratedPageAndDownloadsSizedImage()
    {
        var handler = Pexels();
        var provider = NewProvider(handler);

        var image = await provider.FetchAsync(1280, 720, CancellationToken.None);

        Assert.Equal(JpegBytes, image.Content);
        Assert.Equal("image/jpeg", image.MediaType);
        Assert.Equal("pexels.com", provider.Name);
        Assert.Collection(handler.Requests,
            api =>
            {
                Assert.Equal("https://api.pexels.com/v1/curated?per_page=1&page=42", api.RequestUri!.AbsoluteUri);
                Assert.Equal(Key, Assert.Single(api.Headers.GetValues("Authorization")));
                Assert.Equal("application/json", Assert.Single(api.Headers.Accept).MediaType);
            },
            img =>
            {
                Assert.Equal(
                    "https://images.pexels.com/photos/2014422/pexels-photo-2014422.jpeg?auto=compress&cs=tinysrgb&fit=crop&w=1280&h=720",
                    img.RequestUri!.AbsoluteUri);
                Assert.False(img.Headers.Contains("Authorization"));
            });
    }

    [Fact]
    public async Task FetchAsync_ReturnsAttributionNamingPhotographerAndPexels()
    {
        var image = await NewProvider(Pexels()).FetchAsync(1280, 720, CancellationToken.None);

        Assert.Equal("Photo by Joey Farina (https://www.pexels.com/@joey) on Pexels (https://www.pexels.com)", image.Attribution);
    }

    [Fact]
    public async Task FetchAsync_RandomPageIsWithinMaxPage()
    {
        var handler = Pexels();
        var provider = NewProvider(handler, next: int.MaxValue);

        await provider.FetchAsync(1280, 720, CancellationToken.None);

        Assert.EndsWith($"&page={PexelsProvider.MaxPage}", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task FetchAsync_EmptyPageWithTotal_RetriesWithinTotal()
    {
        var handler = Pexels(request => request.RequestUri!.Query.Contains("page=42")
            ? Json("""{"photos":[],"total_results":30}""")
            : Json(PageJson));
        var provider = NewProvider(handler);

        var image = await provider.FetchAsync(1280, 720, CancellationToken.None);

        Assert.Equal(JpegBytes, image.Content);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("https://api.pexels.com/v1/curated?per_page=1&page=30", handler.Requests[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task FetchAsync_RetryPageAlsoEmpty_ThrowsAfterTwoApiCalls()
    {
        var handler = Pexels(_ => Json("""{"photos":[],"total_results":30}"""));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => NewProvider(handler).FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal("the site returned no photos", ex.Message);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void ParsePage_NonNumericTotal_StillReturnsPhoto()
    {
        var page = PexelsProvider.ParsePage(Encoding.UTF8.GetBytes(
            """{"total_results":"lots","photos":[{"src":{"original":"https://images.pexels.com/1.jpeg"}}]}"""));

        Assert.NotNull(page.Photo);
        Assert.Equal(0, page.TotalResults);
    }

    [Fact]
    public async Task Resolver_NetworkError_FallsThroughWithoutKey()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            throw new HttpRequestException("No such host is known. (api.pexels.com:443)"));
        var next = new StubRandomBackgroundProvider((_, _, _) =>
            Task.FromResult(new RandomBackgroundImage(JpegBytes, "image/jpeg")), "next.example");

        var resolved = await new RandomBackgroundResolver(NewProvider(handler), next) { TempDirectory = _tempDir }
            .ResolveToFileAsync(1280, 720);

        var failure = Assert.Single(resolved.Failures);
        Assert.Equal("pexels.com", failure.ProviderName);
        Assert.StartsWith("the site could not be reached", failure.Reason);
        Assert.DoesNotContain(Key, RandomBackgroundResolver.BuildFallbackWarning(resolved));
    }

    [Theory]
    [InlineData("""{"photos":[],"total_results":0}""")]
    [InlineData("""{"photos":[]}""")]
    public async Task FetchAsync_NoPhotos_Throws(string json)
    {
        var handler = Pexels(_ => Json(json));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => NewProvider(handler).FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal("the site returned no photos", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized,
        "the API key in PEXELS_API_KEY was rejected (HTTP 401); check that it is a valid Pexels API key")]
    [InlineData(HttpStatusCode.Forbidden,
        "the API key in PEXELS_API_KEY was rejected (HTTP 403); check that it is a valid Pexels API key")]
    [InlineData(HttpStatusCode.TooManyRequests, "too many requests (HTTP 429); try again later")]
    [InlineData(HttpStatusCode.InternalServerError, "the site returned HTTP 500")]
    public async Task FetchAsync_ApiError_ThrowsClearMessageWithoutKey(HttpStatusCode status, string expected)
    {
        var handler = Pexels(_ => Json("""{"error":"nope"}""", status));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => NewProvider(handler).FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal(expected, ex.Message);
        Assert.DoesNotContain(Key, ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchAsync_RateLimitRemainingZero_SaysTheLimitWasReached()
    {
        var handler = Pexels(_ =>
        {
            var response = Json("{}", HttpStatusCode.TooManyRequests);
            response.Headers.Add("X-Ratelimit-Remaining", "0");
            return response;
        });

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => NewProvider(handler).FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal("the rate limit was reached (HTTP 429); try again later", ex.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"photos":{}}""")]
    [InlineData("""{"photos":[{"src":{}}]}""")]
    [InlineData("""{"photos":[{"src":{"original":"http://images.pexels.com/photos/1.jpeg"}}]}""")]
    [InlineData("""{"photos":[{"src":{"original":"https://evil.example/1.jpeg"}}]}""")]
    [InlineData("""{"photos":[{"src":{"original":"https://images.pexels.com/1.jpeg"},"photographer":"a\uD800b"}]}""")]
    public async Task FetchAsync_UnexpectedApiResponse_Throws(string json)
    {
        var handler = Pexels(_ => Json(json));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => NewProvider(handler).FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal("the site returned an unexpected response", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchAsync_OversizedApiResponse_Throws()
    {
        var huge = "{\"pad\":\"" + new string('a', ApiProviderHelpers.MaxJsonBytes) + "\"}";

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => NewProvider(Pexels(_ => Json(huge))).FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal("the site returned an unexpected response", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_ApiDoesNotRespond_ThrowsAfterResponseTimeout()
    {
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException();
        });
        var provider = new PexelsProvider(new HttpClient(handler), Key) { ResponseTimeout = TimeSpan.FromMilliseconds(100) };

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(1280, 720, CancellationToken.None));

        Assert.Equal("the site did not respond within 0.1 seconds", ex.Message);
    }

    [Fact]
    public void ParsePage_ProfileOnOtherHost_IsLeftOut()
    {
        var page = PexelsProvider.ParsePage(Encoding.UTF8.GetBytes(
            """{"photos":[{"src":{"original":"https://images.pexels.com/1.jpeg"},"photographer":"Jane","photographer_url":"https://evil.example/jane"}]}"""));

        Assert.Equal("Photo by Jane on Pexels (https://www.pexels.com)", page.Photo!.Attribution);
    }

    [Theory]
    [InlineData(null, "Photo from Pexels (https://www.pexels.com)")]
    [InlineData("\u001b\n‮ ", "Photo from Pexels (https://www.pexels.com)")]
    [InlineData("Jane‮eoD\u001b", "Photo by JaneeoD on Pexels (https://www.pexels.com)")]
    public void BuildAttribution_CleansName(string? name, string expected) =>
        Assert.Equal(expected, PexelsProvider.BuildAttribution(name, null));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyKey_Throws(string key) =>
        Assert.Throws<ArgumentException>(() => new PexelsProvider(new HttpClient(), key));

    [Fact]
    public async Task Resolver_KeyRejected_FallsThroughToNextProvider()
    {
        var pexels = NewProvider(Pexels(_ => Json("{}", HttpStatusCode.Unauthorized)));
        var next = new StubRandomBackgroundProvider((_, _, _) =>
            Task.FromResult(new RandomBackgroundImage(JpegBytes, "image/jpeg")), "next.example");

        var resolved = await new RandomBackgroundResolver(pexels, next) { TempDirectory = _tempDir }
            .ResolveToFileAsync(1280, 720);

        Assert.Equal("next.example", resolved.ProviderName);
        var failure = Assert.Single(resolved.Failures);
        Assert.Equal("pexels.com", failure.ProviderName);
        Assert.StartsWith("the API key in PEXELS_API_KEY was rejected", failure.Reason);
    }

    [Fact]
    public async Task Resolver_PexelsSucceeds_CarriesAttribution()
    {
        var resolved = await new RandomBackgroundResolver(NewProvider(Pexels())) { TempDirectory = _tempDir }
            .ResolveToFileAsync(1280, 720);

        Assert.Equal("pexels.com", resolved.ProviderName);
        Assert.StartsWith("Photo by Joey Farina", resolved.Attribution);
    }

    // ---- --bg random:pexels ----

    [Fact]
    public void Spec_RandomPexelsWithKey_CreatesPexelsProvider()
    {
        string? asked = null;
        var spec = RandomBackgroundSpec.Parse("random:Pexels", variable => { asked = variable; return Key; });

        Assert.Equal("PEXELS_API_KEY", asked);
        Assert.Equal("pexels", spec.ProviderName);
        Assert.Equal("--bg random:pexels", spec.OptionText);
        Assert.DoesNotContain(Key, spec.ToString());
        Assert.Equal("pexels.com", Assert.IsType<PexelsProvider>(spec.CreateProvider(new HttpClient())).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Spec_RandomPexelsWithoutKey_ThrowsExplainingTheVariable(string? key)
    {
        var ex = Assert.Throws<ArgumentException>(() => RandomBackgroundSpec.Parse("random:pexels", _ => key));

        Assert.Equal(
            "Invalid --bg value \"random:pexels\": Pexels needs an API key. Set the PEXELS_API_KEY " +
            "environment variable to your Pexels API key (free at https://www.pexels.com/api/).",
            ex.Message);
    }

    [Fact]
    public void Spec_RandomPexelsWithOption_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => RandomBackgroundSpec.Parse("random:pexels:city", _ => Key));

        Assert.Equal("Invalid --bg value \"random:pexels:city\": pexels doesn't take an option. Use \"random:pexels\".",
            ex.Message);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);
}
