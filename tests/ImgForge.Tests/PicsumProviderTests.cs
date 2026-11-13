using System.Net;
using System.Text;
using ImgForge.Core.RandomBackgrounds;

namespace ImgForge.Tests;

public class PicsumProviderTests
{
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    [Fact]
    public async Task FetchAsync_RequestsPicsumUrlForSize()
    {
        var handler = FakeHttpMessageHandler.Returning(() => FakeHttpMessageHandler.Image(JpegBytes));
        var provider = new PicsumProvider(new HttpClient(handler));

        await provider.FetchAsync(1280, 720, CancellationToken.None);

        Assert.Equal("https://picsum.photos/1280/720", Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
        Assert.Equal("picsum.photos", provider.Name);
    }

    [Fact]
    public async Task FetchAsync_ImageResponse_ReturnsBytesAndMediaType()
    {
        var handler = FakeHttpMessageHandler.Returning(() => FakeHttpMessageHandler.Image(JpegBytes, "IMAGE/JPEG"));
        var provider = new PicsumProvider(new HttpClient(handler));

        var image = await provider.FetchAsync(100, 100, CancellationToken.None);

        Assert.Equal(JpegBytes, image.Content);
        Assert.Equal("image/jpeg", image.MediaType);
    }

    [Theory]
    [InlineData(522)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task FetchAsync_ErrorStatus_ThrowsWithStatus(int status)
    {
        var handler = FakeHttpMessageHandler.Returning(() => new HttpResponseMessage((HttpStatusCode)status));
        var provider = new PicsumProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.Equal($"the site returned HTTP {status}", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_HtmlResponse_ThrowsNotAnImage()
    {
        var handler = FakeHttpMessageHandler.Returning(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>Origin down</html>", Encoding.UTF8, "text/html")
        });
        var provider = new PicsumProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.Equal("the site responded, but not with an image (content type: text/html)", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_EmptyImage_Throws()
    {
        var handler = FakeHttpMessageHandler.Returning(() => FakeHttpMessageHandler.Image([]));
        var provider = new PicsumProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.Equal("the site returned an empty image", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_DeclaredLengthTooLarge_ThrowsWithoutReadingBody()
    {
        var handler = FakeHttpMessageHandler.Returning(() =>
        {
            var response = FakeHttpMessageHandler.Image(JpegBytes);
            response.Content.Headers.ContentLength = HttpImageDownloader.MaxImageBytes + 1;
            return response;
        });
        var provider = new PicsumProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.StartsWith("the image is larger than", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_StreamedBodyTooLarge_Throws()
    {
        // No Content-Length, so the limit must be enforced while reading.
        var handler = FakeHttpMessageHandler.Returning(() =>
        {
            var body = new StreamContent(new EndlessStream());
            body.Headers.ContentType = new("image/jpeg");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = body };
        });
        var provider = new PicsumProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.StartsWith("the image is larger than", ex.Message);
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("image/tiff")]
    public async Task FetchAsync_UnsupportedImageType_Throws(string mediaType)
    {
        var handler = FakeHttpMessageHandler.Returning(() => FakeHttpMessageHandler.Image(JpegBytes, mediaType));
        var provider = new PicsumProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.Equal($"the site returned an unsupported image type ({mediaType})", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_NoResponseWithinResponseTimeout_ThrowsNoResponse()
    {
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        var provider = new PicsumProvider(new HttpClient(handler)) { ResponseTimeout = TimeSpan.FromMilliseconds(100) };

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.Equal("the site did not respond within 0.1 seconds", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_SlowBodyAfterPromptHeaders_IsNotCutOffByResponseTimeout()
    {
        // Headers arrive immediately; the body trickles in for longer than the response timeout.
        var handler = FakeHttpMessageHandler.Returning(() =>
        {
            var body = new StreamContent(new SlowStream(JpegBytes, TimeSpan.FromMilliseconds(300)));
            body.Headers.ContentType = new("image/jpeg");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = body };
        });
        var provider = new PicsumProvider(new HttpClient(handler)) { ResponseTimeout = TimeSpan.FromMilliseconds(100) };

        var image = await provider.FetchAsync(100, 100, CancellationToken.None);

        Assert.Equal(JpegBytes, image.Content);
    }

    [Fact]
    public async Task FetchAsync_CallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        var provider = new PicsumProvider(new HttpClient(handler));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.FetchAsync(100, 100, cts.Token));
    }

    /// <summary>Returns <paramref name="content"/> only after <paramref name="delay"/>.</summary>
    private sealed class SlowStream(byte[] content, TimeSpan delay) : Stream
    {
        private readonly MemoryStream _inner = new(content);
        private bool _waited;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_waited)
            {
                await Task.Delay(delay, cancellationToken);
                _waited = true;
            }
            return await _inner.ReadAsync(buffer, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A non-seekable stream of zeros that never ends.</summary>
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { Array.Clear(buffer, offset, count); return count; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
