using System.Net;
using System.Net.Sockets;
using System.Text;
using ImgForge.Core;
using ImgForge.Core.RandomBackgrounds;

namespace ImgForge.Tests;

[Trait("Category", "Integration")]
public class ImageGeneratorLoadFailureTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private readonly List<string> _tempDirs = [];

    [Fact]
    public async Task GenerateAsync_BackgroundConnectionRefused_ThrowsAndWritesNoImage()
    {
        var port = GetClosedPort();
        var (opts, outPath) = CreateOptions(
            "<html><body style=\"background-image:url('{{ bg }}')\"></body></html>",
            background: $"http://127.0.0.1:{port}/background.jpg");

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => NewGenerator().GenerateAsync(opts));

        Assert.StartsWith("Could not load the --bg background image from 127.0.0.1: the site could not be reached (net::ERR_", ex.Message);
        Assert.Contains("--bg", ex.Message);
        Assert.False(File.Exists(outPath), "No output should be written when a requested image fails to load.");
    }

    [Fact]
    public async Task GenerateAsync_BackgroundReturns404_ThrowsWithHttpStatus()
    {
        await using var server = StatusServer.Start(StatusServer.Response(404, "Not Found"));
        var (opts, outPath) = CreateOptions(
            "<html><body><img src='{{ bg }}'></body></html>",
            background: $"http://127.0.0.1:{server.Port}/missing.jpg");

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => NewGenerator().GenerateAsync(opts));

        Assert.StartsWith("Could not load the --bg background image from 127.0.0.1: the site returned HTTP 404.", ex.Message);
        Assert.False(File.Exists(outPath), "No output should be written when a requested image fails to load.");
    }

    [Fact]
    public async Task GenerateAsync_BackgroundReturns404HtmlPage_SaysSiteRespondedWithoutImage()
    {
        // Real servers send an HTML error page; Chromium blocks it for an <img> request (ERR_BLOCKED_BY_ORB).
        await using var server = StatusServer.Start(StatusServer.Response(404, "Not Found",
            "text/html", "<html><body><h1>Not Found</h1></body></html>"));
        var (opts, _) = CreateOptions(
            "<html><body><img src='{{ bg }}'></body></html>",
            background: $"http://127.0.0.1:{server.Port}/missing.jpg");

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => NewGenerator().GenerateAsync(opts));

        Assert.StartsWith(
            "Could not load the --bg background image from 127.0.0.1: the site responded, but not with an image; " +
            "it may have returned an error page (net::ERR_BLOCKED_BY_ORB).",
            ex.Message);
        Assert.DoesNotContain("could not be reached", ex.Message);
    }

    [Fact]
    public async Task GenerateAsync_BackgroundRedirectsToUnreachableHost_AttributedToOriginalUrl()
    {
        // Like picsum.photos, which redirects to a CDN: the failure happens on the redirect target,
        // but it must still be reported as the --bg image.
        var closedPort = GetClosedPort();
        await using var server = StatusServer.Start(
            $"HTTP/1.1 302 Found\r\nLocation: http://localhost:{closedPort}/cdn/image.jpg\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        var (opts, outPath) = CreateOptions(
            "<html><body><img src='{{ bg }}'></body></html>",
            background: $"http://127.0.0.1:{server.Port}/random.jpg");

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => NewGenerator().GenerateAsync(opts));

        Assert.StartsWith("Could not load the --bg background image from 127.0.0.1: the site could not be reached (net::ERR_", ex.Message);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public async Task GenerateAsync_RequestedAndTemplateResourcesFail_WarnsThenThrows()
    {
        var port = GetClosedPort();
        var (opts, _) = CreateOptions(
            $"<html><body><img src='{{{{ bg }}}}'><img src='http://127.0.0.1:{port}/decoration.png'></body></html>",
            background: $"http://127.0.0.1:{port}/background.jpg");
        var warnings = new StringWriter();
        var generator = NewGenerator();
        generator.Warnings = warnings;

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => generator.GenerateAsync(opts));

        Assert.Contains("--bg", ex.Message);
        Assert.DoesNotContain("decoration.png", ex.Message);
        Assert.Contains($"http://127.0.0.1:{port}/decoration.png referenced by the template", warnings.ToString());
    }

    [Fact]
    public async Task GenerateAsync_TemplateResourceFails_WarnsAndStillWritesImage()
    {
        var port = GetClosedPort();
        var (opts, outPath) = CreateOptions(
            $"<html><body style='background:red'><img src='http://127.0.0.1:{port}/decoration.png?token=secret'></body></html>",
            background: null);
        var warnings = new StringWriter();
        var generator = NewGenerator();
        generator.Warnings = warnings;

        var result = await generator.GenerateAsync(opts);

        Assert.Equal(outPath, result);
        Assert.True(File.Exists(outPath), "Output PNG should still be written when only a template resource fails.");
        var warning = warnings.ToString();
        Assert.StartsWith($"Warning: Could not load http://127.0.0.1:{port}/decoration.png referenced by the template", warning);
        Assert.DoesNotContain("secret", warning);
    }

    [Fact]
    public async Task GenerateAsync_NoRemoteResources_WritesNoWarnings()
    {
        var (opts, outPath) = CreateOptions("<html><body style='background:blue'></body></html>", background: null);
        var warnings = new StringWriter();
        var generator = NewGenerator();
        generator.Warnings = warnings;

        await generator.GenerateAsync(opts);

        Assert.True(File.Exists(outPath));
        Assert.Equal(string.Empty, warnings.ToString());
    }

    [Fact]
    public async Task GenerateAsync_RandomBackground_RendersDownloadedImageAndDeletesTempFile()
    {
        var provider = new StubRandomBackgroundProvider((_, _, _) =>
            Task.FromResult(new RandomBackgroundImage(Convert.FromBase64String(RedPixelPng), "image/png")));
        // If the downloaded background fails to load, onerror requests a closed port, which shows up as a
        // template-resource warning. So "no warnings" proves the local file was rendered.
        var closedPort = GetClosedPort();
        var (opts, outPath) = CreateOptions(
            "<html><body style='margin:0'><img src='{{ bg }}' " +
            $"onerror=\"var i = new Image(); i.src = 'http://127.0.0.1:{closedPort}/bg-failed.png'; document.body.appendChild(i)\">" +
            "</body></html>",
            background: "random");
        var warnings = new StringWriter();
        var generator = NewGenerator();
        generator.Warnings = warnings;
        // A folder name with characters that break naive file URIs in url('...') / src='...'.
        var tempDir = NewTempDirectory("O'Brien #1 50%");
        generator.RandomBackgrounds = new RandomBackgroundResolver(provider) { TempDirectory = tempDir };

        await generator.GenerateAsync(opts);

        Assert.Equal(1, provider.Calls);
        Assert.True(File.Exists(outPath));
        Assert.Equal(string.Empty, warnings.ToString());
        Assert.Empty(Directory.GetFiles(tempDir));
    }

    [Fact]
    public async Task GenerateAsync_RandomBackgroundProviderFails_ThrowsAndWritesNoImage()
    {
        var provider = new StubRandomBackgroundProvider((_, _, _) =>
            throw new RandomBackgroundProviderException("the site returned HTTP 522"));
        var (opts, outPath) = CreateOptions("<html><body><img src='{{ bg }}'></body></html>", background: "random");
        var generator = NewGenerator();
        generator.RandomBackgrounds = new RandomBackgroundResolver(provider);

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => generator.GenerateAsync(opts));

        Assert.StartsWith(
            "Could not load a random background image from stub.example (used by --bg random): the site returned HTTP 522.",
            ex.Message);
        Assert.False(File.Exists(outPath), "No output should be written when the random background fails.");
    }

    [Fact]
    public async Task GenerateAsync_RandomBackgroundThenRenderFails_DeletesDownloadedImage()
    {
        var provider = new StubRandomBackgroundProvider((_, _, _) =>
            Task.FromResult(new RandomBackgroundImage(Convert.FromBase64String(RedPixelPng), "image/png")));
        var (opts, _) = CreateOptions("<html>{{ bg | no_such_filter }}</html>", background: "random");
        var tempDir = NewTempDirectory("bg");
        var generator = NewGenerator();
        generator.RandomBackgrounds = new RandomBackgroundResolver(provider) { TempDirectory = tempDir };

        await Assert.ThrowsAnyAsync<Exception>(() => generator.GenerateAsync(opts));

        Assert.Equal(1, provider.Calls);
        Assert.Empty(Directory.GetFiles(tempDir));
    }

    [Fact]
    public async Task GenerateAsync_RandomBackgroundWithMissingTemplate_FailsWithoutDownloading()
    {
        var provider = new StubRandomBackgroundProvider((_, _, _) =>
            Task.FromResult(new RandomBackgroundImage(Convert.FromBase64String(RedPixelPng), "image/png")));
        var generator = NewGenerator();
        generator.RandomBackgrounds = new RandomBackgroundResolver(provider);
        var opts = new GenerateOptions(
            Template: Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.html"),
            Title: "Test", Subtitle: null, Background: "random", Overlays: [], Out: "unused.png");

        await Assert.ThrowsAsync<FileNotFoundException>(() => generator.GenerateAsync(opts));

        Assert.Equal(0, provider.Calls);
    }

    /// <summary>A 1×1 red PNG.</summary>
    private const string RedPixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==";

    private string NewTempDirectory(string name)
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory("imgforge-test-").FullName, name);
        Directory.CreateDirectory(dir);
        _tempDirs.Add(Path.GetDirectoryName(dir)!);
        return dir;
    }

    private static ImageGenerator NewGenerator() =>
        new(new TemplateRenderer()) { LoadTimeout = TimeSpan.FromSeconds(15) };

    private (GenerateOptions Options, string OutPath) CreateOptions(string templateHtml, string? background)
    {
        var outPath = Path.GetTempFileName() + ".png";
        _tempFiles.Add(outPath);

        var templateFile = Path.GetTempFileName() + ".html";
        _tempFiles.Add(templateFile);
        File.WriteAllText(templateFile, templateHtml);

        return (new GenerateOptions(
            Template: templateFile,
            Title: "Test",
            Subtitle: null,
            Background: background,
            Overlays: [],
            Out: outPath,
            Width: 400,
            Height: 300), outPath);
    }

    /// <summary>Returns a loopback port with nothing listening on it, so connections are refused.</summary>
    private static int GetClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch { /* best-effort cleanup */ }
        }
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>A minimal loopback HTTP server that answers every request with the same raw HTTP response.</summary>
    private sealed class StatusServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        private StatusServer(string rawResponse)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var response = Encoding.ASCII.GetBytes(rawResponse);
            _loop = Task.Run(async () =>
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                        try
                        {
                            var stream = client.GetStream();
                            await ReadRequestHeadersAsync(stream, _cts.Token);
                            await stream.WriteAsync(response, _cts.Token);
                        }
                        catch (IOException) { /* client hung up; keep serving */ }
                    }
                }
                catch (Exception) when (_cts.IsCancellationRequested) { /* server stopped */ }
            });
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static StatusServer Start(string rawResponse) => new(rawResponse);

        public static string Response(int status, string reason, string? contentType = null, string body = "") =>
            $"HTTP/1.1 {status} {reason}\r\n" +
            (contentType is null ? "" : $"Content-Type: {contentType}\r\n") +
            $"Content-Length: {Encoding.ASCII.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";

        private static async Task ReadRequestHeadersAsync(NetworkStream stream, CancellationToken ct)
        {
            var buffer = new byte[4096];
            var received = new StringBuilder();
            while (!received.ToString().Contains("\r\n\r\n"))
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read == 0) return;
                received.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            await _loop;
            _cts.Dispose();
        }
    }
}
