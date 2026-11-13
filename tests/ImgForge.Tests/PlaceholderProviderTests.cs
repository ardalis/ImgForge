using System.Net;
using System.Text;
using ImgForge.Core.RandomBackgrounds;

namespace ImgForge.Tests;

public class PlaceholderProviderTests : IDisposable
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    /// <summary>For tests that only build URLs or validate settings; it never sends a request.</summary>
    private static readonly HttpClient UnusedClient = new(FakeHttpMessageHandler.Returning(
        () => throw new InvalidOperationException("No request expected.")));

    private readonly string _tempDir = Directory.CreateTempSubdirectory("imgforge-test-").FullName;

    [Fact]
    public async Task FetchAsync_DefaultColor_RequestsSolidPngForSize()
    {
        var handler = FakeHttpMessageHandler.Returning(() => FakeHttpMessageHandler.Image(PngBytes, "image/png"));
        var provider = new PlaceholderProvider(new HttpClient(handler));

        await provider.FetchAsync(1280, 720, CancellationToken.None);

        Assert.Equal("https://placehold.co/1280x720/1e293b/1e293b/png",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
        Assert.Equal("placehold.co", provider.Name);
        Assert.Equal(PlaceholderProvider.DefaultColor, provider.Color);
    }

    [Theory]
    [InlineData("000000", "https://placehold.co/1000x420/000000/000000/png")]
    [InlineData("#FFAA00", "https://placehold.co/1000x420/ffaa00/ffaa00/png")]
    [InlineData("AbCdEf", "https://placehold.co/1000x420/abcdef/abcdef/png")]
    public void ImageUrl_UsesColorForBackgroundAndHiddenLabel(string color, string expected)
    {
        var provider = new PlaceholderProvider(UnusedClient) { Color = color };

        Assert.Equal(expected, provider.ImageUrl(1000, 420).AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("fff")]
    [InlineData("1e293b80")]
    [InlineData("gggggg")]
    [InlineData("##1e293b")]
    [InlineData("1e293b/x")]
    [InlineData("1e293b?text=hi")]
    [InlineData(" 1e293b")]
    public void Color_Invalid_IsRejected(string color)
    {
        var ex = Assert.Throws<ArgumentException>(() => new PlaceholderProvider(UnusedClient) { Color = color });

        Assert.Equal("Color", ex.ParamName);
        Assert.StartsWith($"Invalid placeholder color \"{color}\". Use a 6-digit hex color", ex.Message);
    }

    [Fact]
    public void Color_Null_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new PlaceholderProvider(UnusedClient) { Color = null! });
    }

    [Fact]
    public async Task FetchAsync_ImageResponse_ReturnsBytesAndMediaType()
    {
        var handler = FakeHttpMessageHandler.Returning(() => FakeHttpMessageHandler.Image(PngBytes, "image/png"));
        var provider = new PlaceholderProvider(new HttpClient(handler));

        var image = await provider.FetchAsync(100, 100, CancellationToken.None);

        Assert.Equal(PngBytes, image.Content);
        Assert.Equal("image/png", image.MediaType);
    }

    [Fact]
    public async Task FetchAsync_ErrorStatus_ThrowsWithStatus()
    {
        var handler = FakeHttpMessageHandler.Returning(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var provider = new PlaceholderProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.Equal("the site returned HTTP 503", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_SvgResponse_ThrowsUnsupportedType()
    {
        // placehold.co returns SVG when the URL isn't in the expected form.
        var handler = FakeHttpMessageHandler.Returning(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<svg/>", Encoding.UTF8, "image/svg+xml")
        });
        var provider = new PlaceholderProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.Equal("the site returned an unsupported image type (image/svg+xml)", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_NoResponseWithinResponseTimeout_ThrowsNoResponse()
    {
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        var provider = new PlaceholderProvider(new HttpClient(handler)) { ResponseTimeout = TimeSpan.FromMilliseconds(100) };

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.Equal("the site did not respond within 0.1 seconds", ex.Message);
    }

    [Fact]
    public async Task AsFallbackAfterFailingPicsum_SuppliesTheImage()
    {
        var picsum = new PicsumProvider(new HttpClient(
            FakeHttpMessageHandler.Returning(() => new HttpResponseMessage((HttpStatusCode)522))));
        var placeholder = new PlaceholderProvider(new HttpClient(
            FakeHttpMessageHandler.Returning(() => FakeHttpMessageHandler.Image(PngBytes, "image/png"))));
        var resolver = new RandomBackgroundResolver(picsum, placeholder) { TempDirectory = _tempDir };

        var resolved = await resolver.ResolveToFileAsync(1200, 630);

        Assert.Equal("placehold.co", resolved.ProviderName);
        Assert.EndsWith(".png", resolved.Path);
        Assert.Equal(PngBytes, await File.ReadAllBytesAsync(resolved.Path));
        Assert.Equal(new RandomBackgroundProviderFailure("picsum.photos", "the site returned HTTP 522"),
            Assert.Single(resolved.Failures));
        Assert.Equal(
            "Warning: picsum.photos failed (the site returned HTTP 522). Used placehold.co for --bg random instead " +
            "(a solid-color background, not a photo).",
            RandomBackgroundResolver.BuildFallbackWarning(resolved));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best effort */ }
        GC.SuppressFinalize(this);
    }
}
