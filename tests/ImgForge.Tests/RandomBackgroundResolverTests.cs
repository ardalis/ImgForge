using System.Diagnostics;
using ImgForge.Core;
using ImgForge.Core.RandomBackgrounds;

namespace ImgForge.Tests;

public class RandomBackgroundResolverTests
{
    private static readonly byte[] ImageBytes = [0xFF, 0xD8, 0xFF, 0xE0, 9, 8, 7];

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("image/webp", ".webp")]
    [InlineData("image/x-unknown", ".jpg")]
    public async Task ResolveToFileAsync_WritesImageToTempFileWithMatchingExtension(string mediaType, string extension)
    {
        var resolver = new RandomBackgroundResolver(new StubRandomBackgroundProvider((_, _, _) =>
            Task.FromResult(new RandomBackgroundImage(ImageBytes, mediaType))));

        var path = await resolver.ResolveToFileAsync(1200, 630);
        try
        {
            Assert.StartsWith(Path.GetTempPath(), path);
            Assert.EndsWith(extension, path);
            Assert.Equal(ImageBytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ResolveToFileAsync_PassesRequestedSizeToProvider()
    {
        (int W, int H)? requested = null;
        var resolver = new RandomBackgroundResolver(new StubRandomBackgroundProvider((w, h, _) =>
        {
            requested = (w, h);
            return Task.FromResult(new RandomBackgroundImage(ImageBytes, "image/jpeg"));
        }));

        File.Delete(await resolver.ResolveToFileAsync(1280, 720));

        Assert.Equal((1280, 720), requested);
    }

    [Fact]
    public async Task ResolveToFileAsync_ProviderError_ThrowsImageLoadExceptionNamingProviderAndReason()
    {
        var resolver = new RandomBackgroundResolver(new StubRandomBackgroundProvider((_, _, _) =>
            throw new RandomBackgroundProviderException("the site returned HTTP 522")));

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => resolver.ResolveToFileAsync(1200, 630));

        var lines = ex.Message.Split(Environment.NewLine);
        Assert.Equal(
            "Could not load a random background image from stub.example (used by --bg random): the site returned HTTP 522.",
            lines[0]);
        Assert.Contains("try again later", ex.Message);
        Assert.Contains("Pass a different background with --bg", ex.Message);
        Assert.Contains("omit --bg", ex.Message);
    }

    [Fact]
    public async Task ResolveToFileAsync_NetworkError_ReportsUnreachable()
    {
        var resolver = new RandomBackgroundResolver(new StubRandomBackgroundProvider((_, _, _) =>
            throw new HttpRequestException("No such host is known.")));

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => resolver.ResolveToFileAsync(1200, 630));

        Assert.StartsWith(
            "Could not load a random background image from stub.example (used by --bg random): " +
            "the site could not be reached (No such host is known).",
            ex.Message);
    }

    [Fact]
    public async Task ResolveToFileAsync_DownloadExceedsOverallTimeout_ReportsUnfinishedDownload()
    {
        var resolver = new RandomBackgroundResolver(new StubRandomBackgroundProvider(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        }))
        { DownloadTimeout = TimeSpan.FromMilliseconds(200) };

        var stopwatch = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => resolver.ResolveToFileAsync(1200, 630));
        stopwatch.Stop();

        Assert.Contains("stub.example (used by --bg random): the image did not finish downloading within 0.2 seconds.", ex.Message);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task ResolveToFileAsync_NoResponseThroughRealHandler_ReportsNoResponse()
    {
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        });
        var resolver = new RandomBackgroundResolver(
            new PicsumProvider(new HttpClient(handler)) { ResponseTimeout = TimeSpan.FromMilliseconds(200) });

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => resolver.ResolveToFileAsync(1200, 630));

        Assert.StartsWith(
            "Could not load a random background image from picsum.photos (used by --bg random): " +
            "the site did not respond within 0.2 seconds.",
            ex.Message);
    }

    [Fact]
    public async Task ResolveToFileAsync_CallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        var resolver = new RandomBackgroundResolver(new StubRandomBackgroundProvider(async (_, _, ct) =>
        {
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
            throw new UnreachableException();
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveToFileAsync(1200, 630, cts.Token));
    }

    [Fact]
    public async Task ResolveToFileAsync_ProviderFails_LeavesNoTempFile()
    {
        var dir = Directory.CreateTempSubdirectory("imgforge-test-").FullName;
        try
        {
            var resolver = new RandomBackgroundResolver(new StubRandomBackgroundProvider((_, _, _) =>
                throw new RandomBackgroundProviderException("the site returned HTTP 500")))
            { TempDirectory = dir };

            await Assert.ThrowsAsync<ImageLoadException>(() => resolver.ResolveToFileAsync(1200, 630));

            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DefaultResolver_UsesPicsumWithFiveSecondResponseAndThirtySecondDownloadTimeouts()
    {
        var resolver = new RandomBackgroundResolver();

        var picsum = Assert.IsType<PicsumProvider>(resolver.Provider);
        Assert.Equal(TimeSpan.FromSeconds(5), picsum.ResponseTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), resolver.DownloadTimeout);
        Assert.Equal(Path.GetTempPath(), resolver.TempDirectory);
    }
}
