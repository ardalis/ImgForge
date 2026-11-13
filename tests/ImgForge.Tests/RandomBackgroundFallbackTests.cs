using System.Diagnostics;
using System.Net;
using ImgForge.Core;
using ImgForge.Core.RandomBackgrounds;

namespace ImgForge.Tests;

/// <summary>How <see cref="RandomBackgroundResolver"/> falls back through several providers in order.</summary>
public class RandomBackgroundFallbackTests : IDisposable
{
    private static readonly byte[] FirstImage = [0xFF, 0xD8, 0xFF, 0xE0, 1];
    private static readonly byte[] SecondImage = [0xFF, 0xD8, 0xFF, 0xE0, 2];

    private readonly string _tempDir = Directory.CreateTempSubdirectory("imgforge-test-").FullName;

    [Fact]
    public async Task FirstProviderSucceeds_UsesItAndNeverCallsTheRest()
    {
        var first = Succeeding("first.example", FirstImage);
        var second = Succeeding("second.example", SecondImage);

        var resolved = await NewResolver(first, second).ResolveToFileAsync(1200, 630);

        Assert.Equal("first.example", resolved.ProviderName);
        Assert.Empty(resolved.Failures);
        Assert.Equal(FirstImage, await File.ReadAllBytesAsync(resolved.Path));
        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public async Task FirstProviderReturns522_FallsThroughToSecond()
    {
        // A real PicsumProvider over a fake handler, so the HTTP 522 path is the production one.
        var picsum = new PicsumProvider(new HttpClient(
            FakeHttpMessageHandler.Returning(() => new HttpResponseMessage((HttpStatusCode)522))));
        var second = Succeeding("second.example", SecondImage);

        var resolved = await NewResolver(picsum, second).ResolveToFileAsync(1200, 630);

        Assert.Equal("second.example", resolved.ProviderName);
        Assert.Equal(SecondImage, await File.ReadAllBytesAsync(resolved.Path));
        var failure = Assert.Single(resolved.Failures);
        Assert.Equal(new RandomBackgroundProviderFailure("picsum.photos", "the site returned HTTP 522"), failure);
    }

    [Fact]
    public async Task FirstProviderDoesNotRespond_FallsThroughToSecondAfterItsResponseTimeout()
    {
        var hanging = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        });
        var picsum = new PicsumProvider(new HttpClient(hanging)) { ResponseTimeout = TimeSpan.FromMilliseconds(200) };
        var second = Succeeding("second.example", SecondImage);

        var stopwatch = Stopwatch.StartNew();
        var resolved = await NewResolver(picsum, second).ResolveToFileAsync(1200, 630);
        stopwatch.Stop();

        Assert.Equal("second.example", resolved.ProviderName);
        Assert.Equal("the site did not respond within 0.2 seconds", Assert.Single(resolved.Failures).Reason);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task DownloadTimeoutIsPerProvider_SecondProviderGetsItsOwnFullBudget()
    {
        var timeout = TimeSpan.FromMilliseconds(500);
        var slowToFail = new StubRandomBackgroundProvider(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        }, "slow.example");
        // If the budget were shared, the first provider used all of it and this token would already be cancelled.
        var second = new StubRandomBackgroundProvider(async (_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromMilliseconds(10), ct);
            return new RandomBackgroundImage(SecondImage, "image/jpeg");
        }, "second.example");
        var resolver = new RandomBackgroundResolver(slowToFail, second) { DownloadTimeout = timeout, TempDirectory = _tempDir };

        var resolved = await resolver.ResolveToFileAsync(1200, 630);

        Assert.Equal("second.example", resolved.ProviderName);
        Assert.Equal(
            new RandomBackgroundProviderFailure("slow.example", "the image did not finish downloading within 0.5 seconds"),
            Assert.Single(resolved.Failures));
    }

    [Fact]
    public async Task FirstProviderUnreachable_FallsThroughAndRecordsReason()
    {
        var unreachable = new StubRandomBackgroundProvider(
            (_, _, _) => throw new HttpRequestException("No such host is known."), "down.example");
        var second = Succeeding("second.example", SecondImage);

        var resolved = await NewResolver(unreachable, second).ResolveToFileAsync(1200, 630);

        Assert.Equal(
            new RandomBackgroundProviderFailure("down.example", "the site could not be reached (No such host is known)"),
            Assert.Single(resolved.Failures));
    }

    [Fact]
    public async Task FirstProviderConnectionDropsMidDownload_FallsThroughToSecond()
    {
        // A real PicsumProvider whose response body fails partway through, as when the connection drops.
        var dropping = FakeHttpMessageHandler.Returning(() =>
        {
            var body = new StreamContent(new FailingStream(new HttpIOException(
                HttpRequestError.ResponseEnded, "The response ended prematurely.")));
            body.Headers.ContentType = new("image/jpeg");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = body };
        });
        var picsum = new PicsumProvider(new HttpClient(dropping));
        var second = Succeeding("second.example", SecondImage);

        var resolved = await NewResolver(picsum, second).ResolveToFileAsync(1200, 630);

        Assert.Equal("second.example", resolved.ProviderName);
        var failure = Assert.Single(resolved.Failures);
        Assert.Equal("picsum.photos", failure.ProviderName);
        // The runtime appends the error code to HttpIOException's message, e.g. "... prematurely. (ResponseEnded)".
        Assert.StartsWith("the connection was lost while downloading the image (The response ended prematurely", failure.Reason);
    }

    [Fact]
    public async Task ProviderCancelsForItsOwnReasons_ReportedAsTimeoutNotOurDownloadLimit()
    {
        var selfCancelling = new StubRandomBackgroundProvider(
            (_, _, _) => throw new TaskCanceledException("HttpClient.Timeout elapsed"), "own-timeout.example");
        var second = Succeeding("second.example", SecondImage);

        var resolved = await NewResolver(selfCancelling, second).ResolveToFileAsync(1200, 630);

        Assert.Equal(
            new RandomBackgroundProviderFailure("own-timeout.example", "the request timed out"),
            Assert.Single(resolved.Failures));
    }

    [Fact]
    public async Task AllProvidersFail_ErrorNamesEveryProviderAndReasonInOrder()
    {
        var first = Failing("first.example", "the site returned HTTP 522");
        var second = new StubRandomBackgroundProvider(
            (_, _, _) => throw new HttpRequestException("Connection refused"), "second.example");
        var third = Failing("third.example", "the site responded, but not with an image (content type: text/html)");

        var ex = await Assert.ThrowsAsync<ImageLoadException>(
            () => NewResolver(first, second, third).ResolveToFileAsync(1200, 630));

        var lines = ex.Message.Split(Environment.NewLine);
        Assert.Equal(
            [
                "Could not load a random background image for --bg random. Every provider failed:",
                "  - first.example: the site returned HTTP 522",
                "  - second.example: the site could not be reached (Connection refused)",
                "  - third.example: the site responded, but not with an image (content type: text/html)",
                "You can:",
            ],
            lines[..5]);
        Assert.Contains("Pass a different background with --bg", ex.Message);
        Assert.Equal((1, 1, 1), (first.Calls, second.Calls, third.Calls));
        Assert.Empty(Directory.GetFiles(_tempDir));
    }

    [Fact]
    public async Task CallerCancels_StopsWithoutTryingLaterProviders()
    {
        using var cts = new CancellationTokenSource();
        var first = new StubRandomBackgroundProvider(async (_, _, ct) =>
        {
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
            throw new UnreachableException();
        }, "first.example");
        var second = Succeeding("second.example", SecondImage);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => NewResolver(first, second).ResolveToFileAsync(1200, 630, cts.Token));

        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public async Task UnexpectedProviderException_Propagates()
    {
        var buggy = new StubRandomBackgroundProvider((_, _, _) => throw new InvalidOperationException("bug"), "buggy.example");
        var second = Succeeding("second.example", SecondImage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => NewResolver(buggy, second).ResolveToFileAsync(1200, 630));

        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public void Constructor_NoProviders_Throws() =>
        Assert.Throws<ArgumentException>(() => new RandomBackgroundResolver(Array.Empty<IRandomBackgroundProvider>()));

    [Fact]
    public void Constructor_NullProvider_Throws() =>
        Assert.Throws<ArgumentException>(() => new RandomBackgroundResolver(Succeeding("a.example", FirstImage), null!));

    [Fact]
    public void Constructor_KeepsProviderOrder()
    {
        var a = Succeeding("a.example", FirstImage);
        var b = Succeeding("b.example", SecondImage);

        Assert.Equal([a, b], new RandomBackgroundResolver(a, b).Providers);
    }

    [Fact]
    public void BuildFallbackWarning_NamesFailedProvidersAndTheOneUsed()
    {
        var resolved = new ResolvedRandomBackground("x.jpg", "third.example",
        [
            new("first.example", "the site returned HTTP 522"),
            new("second.example", "the site did not respond within 5 seconds"),
        ]);

        Assert.Equal(
            "Warning: first.example failed (the site returned HTTP 522); " +
            "second.example failed (the site did not respond within 5 seconds). " +
            "Used third.example for --bg random instead.",
            RandomBackgroundResolver.BuildFallbackWarning(resolved));
    }

    private RandomBackgroundResolver NewResolver(params IRandomBackgroundProvider[] providers) =>
        new(providers) { TempDirectory = _tempDir };

    private static StubRandomBackgroundProvider Succeeding(string name, byte[] content) =>
        new((_, _, _) => Task.FromResult(new RandomBackgroundImage(content, "image/jpeg")), name);

    private static StubRandomBackgroundProvider Failing(string name, string reason) =>
        new((_, _, _) => throw new RandomBackgroundProviderException(reason), name);

    /// <summary>A readable stream that fails on the first read.</summary>
    private sealed class FailingStream(Exception error) : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw error;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(error);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException<int>(error);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort cleanup */ }
        GC.SuppressFinalize(this);
    }
}
