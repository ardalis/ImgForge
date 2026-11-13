using System.Net;
using System.Text;
using ImgForge.Core.RandomBackgrounds;

namespace ImgForge.Tests;

public class LoremFlickrProviderTests : IDisposable
{
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    /// <summary>For tests that only build URLs or validate settings; it never sends a request.</summary>
    private static readonly HttpClient UnusedClient = new(FakeHttpMessageHandler.Returning(
        () => throw new InvalidOperationException("No request expected.")));

    private readonly string _tempDir = Directory.CreateTempSubdirectory("imgforge-test-").FullName;

    [Fact]
    public async Task FetchAsync_DefaultKeyword_RequestsAbstractImageForSize()
    {
        var handler = FakeHttpMessageHandler.Returning(() => FakeHttpMessageHandler.Image(JpegBytes));
        var provider = new LoremFlickrProvider(new HttpClient(handler));

        await provider.FetchAsync(1280, 720, CancellationToken.None);

        Assert.Equal("https://loremflickr.com/1280/720/abstract", Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
        Assert.Equal("loremflickr.com", provider.Name);
    }

    [Theory]
    [InlineData("technology", null, "https://loremflickr.com/1000/420/technology")]
    [InlineData("City,Night", null, "https://loremflickr.com/1000/420/city,night")]
    [InlineData("sci-fi", null, "https://loremflickr.com/1000/420/sci-fi")]
    [InlineData("abstract", 42, "https://loremflickr.com/1000/420/abstract?lock=42")]
    [InlineData("city", 0, "https://loremflickr.com/1000/420/city?lock=0")]
    public void ImageUrl_IncludesKeywordAndOptionalLock(string keyword, int? lockValue, string expected)
    {
        var provider = new LoremFlickrProvider(UnusedClient) { Keyword = keyword, Lock = lockValue };

        Assert.Equal(expected, provider.ImageUrl(1000, 420).AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("city night")]
    [InlineData("city/../../other")]
    [InlineData("city?lock=1")]
    [InlineData("city,")]
    [InlineData(",city")]
    [InlineData("city,,night")]
    [InlineData("café")]
    public void Keyword_Invalid_Throws(string keyword)
    {
        var ex = Assert.Throws<ArgumentException>(() => new LoremFlickrProvider(UnusedClient) { Keyword = keyword });

        Assert.Equal(nameof(LoremFlickrProvider.Keyword), ex.ParamName);
        Assert.Equal(
            $"Invalid LoremFlickr keyword \"{keyword}\". Use one or more keywords separated by commas, each made of " +
            "letters, digits, '-' or '_', up to 100 characters in total (for example: abstract or city,night). " +
            "(Parameter 'Keyword')",
            ex.Message);
    }

    [Fact]
    public void Keyword_AtMaxLength_IsAccepted()
    {
        var keyword = new string('a', LoremFlickrProvider.MaxKeywordLength);

        var provider = new LoremFlickrProvider(UnusedClient) { Keyword = keyword };

        Assert.Equal(keyword, provider.Keyword);
    }

    [Fact]
    public void Keyword_OverMaxLength_Throws() =>
        Assert.Throws<ArgumentException>(() => new LoremFlickrProvider(UnusedClient)
        {
            Keyword = new string('a', LoremFlickrProvider.MaxKeywordLength + 1)
        });

    [Fact]
    public void Keyword_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new LoremFlickrProvider(UnusedClient) { Keyword = null! });

    [Fact]
    public void Lock_Negative_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new LoremFlickrProvider(UnusedClient) { Lock = -1 });

        Assert.Equal(nameof(LoremFlickrProvider.Lock), ex.ParamName);
    }

    [Fact]
    public async Task FetchAsync_ImageResponse_ReturnsBytesAndMediaType()
    {
        var handler = FakeHttpMessageHandler.Returning(() => FakeHttpMessageHandler.Image(JpegBytes));
        var provider = new LoremFlickrProvider(new HttpClient(handler));

        var image = await provider.FetchAsync(100, 100, CancellationToken.None);

        Assert.Equal(JpegBytes, image.Content);
        Assert.Equal("image/jpeg", image.MediaType);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(522)]
    [InlineData(404)]
    public async Task FetchAsync_ErrorStatus_ThrowsWithStatus(int status)
    {
        var handler = FakeHttpMessageHandler.Returning(() => new HttpResponseMessage((HttpStatusCode)status));
        var provider = new LoremFlickrProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.Equal($"the site returned HTTP {status}", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_HtmlResponse_ThrowsNotAnImage()
    {
        var handler = FakeHttpMessageHandler.Returning(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>Error</html>", Encoding.UTF8, "text/html")
        });
        var provider = new LoremFlickrProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.Equal("the site responded, but not with an image (content type: text/html)", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_NoResponseWithinResponseTimeout_ThrowsNoResponse()
    {
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        var provider = new LoremFlickrProvider(new HttpClient(handler)) { ResponseTimeout = TimeSpan.FromMilliseconds(100) };

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.Equal("the site did not respond within 0.1 seconds", ex.Message);
    }

    [Fact]
    public async Task AsFallbackAfterFailingPicsum_SuppliesTheImage()
    {
        var picsum = new PicsumProvider(new HttpClient(
            FakeHttpMessageHandler.Returning(() => new HttpResponseMessage((HttpStatusCode)522))));
        var loremFlickr = new LoremFlickrProvider(new HttpClient(
            FakeHttpMessageHandler.Returning(() => FakeHttpMessageHandler.Image(JpegBytes))));
        var resolver = new RandomBackgroundResolver(picsum, loremFlickr) { TempDirectory = _tempDir };

        var resolved = await resolver.ResolveToFileAsync(1200, 630);

        Assert.Equal("loremflickr.com", resolved.ProviderName);
        Assert.Equal(JpegBytes, await File.ReadAllBytesAsync(resolved.Path));
        Assert.Equal(new RandomBackgroundProviderFailure("picsum.photos", "the site returned HTTP 522"),
            Assert.Single(resolved.Failures));
    }

    [Fact]
    public async Task AllFailIncludingLoremFlickr_ErrorNamesLoremFlickrAndItsReason()
    {
        var picsum = new PicsumProvider(new HttpClient(
            FakeHttpMessageHandler.Returning(() => new HttpResponseMessage((HttpStatusCode)522))));
        var loremFlickr = new LoremFlickrProvider(new HttpClient(
            FakeHttpMessageHandler.Returning(() => new HttpResponseMessage(HttpStatusCode.InternalServerError))));
        var resolver = new RandomBackgroundResolver(picsum, loremFlickr) { TempDirectory = _tempDir };

        var ex = await Assert.ThrowsAsync<ImgForge.Core.ImageLoadException>(() => resolver.ResolveToFileAsync(1200, 630));

        Assert.Contains("  - picsum.photos: the site returned HTTP 522", ex.Message);
        Assert.Contains("  - loremflickr.com: the site returned HTTP 500", ex.Message);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best effort */ }
        GC.SuppressFinalize(this);
    }
}
