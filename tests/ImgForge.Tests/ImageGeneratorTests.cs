using System.Net;
using System.Net.Sockets;
using ImgForge.Core;
using PuppeteerSharp;

namespace ImgForge.Tests;

[Trait("Category", "Integration")]
public class ImageGeneratorTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    [Fact]
    public async Task GenerateAsync_MinimalHtml_CreatesOutputFile()
    {
        var outPath = Path.GetTempFileName() + ".png";
        _tempFiles.Add(outPath);

        var templateFile = Path.GetTempFileName() + ".html";
        _tempFiles.Add(templateFile);
        File.WriteAllText(templateFile, "<html><body style='background:red;width:1200px;height:630px;'></body></html>");

        var opts = new GenerateOptions(
            Template: templateFile,
            Title: "Test",
            Subtitle: null,
            Background: null,
            Overlays: [],
            Out: outPath,
            Width: 1200,
            Height: 630
        );

        var renderer = new TemplateRenderer();
        var generator = new ImageGenerator(renderer);

        var result = await generator.GenerateAsync(opts);

        Assert.Equal(outPath, result);
        Assert.True(File.Exists(outPath), "Output PNG file was not created.");
    }

    [Fact]
    public async Task GenerateAsync_MinimalHtml_OutputHasCorrectDimensions()
    {
        var outPath = Path.GetTempFileName() + ".png";
        _tempFiles.Add(outPath);

        var templateFile = Path.GetTempFileName() + ".html";
        _tempFiles.Add(templateFile);
        File.WriteAllText(templateFile, "<html><body style='background:blue;width:1200px;height:630px;margin:0;padding:0;'></body></html>");

        var opts = new GenerateOptions(
            Template: templateFile,
            Title: "Test",
            Subtitle: null,
            Background: null,
            Overlays: [],
            Out: outPath,
            Width: 1200,
            Height: 630
        );

        var renderer = new TemplateRenderer();
        var generator = new ImageGenerator(renderer);

        await generator.GenerateAsync(opts);

        var (width, height) = PngHeader.ReadDimensions(outPath);
        Assert.Equal(1200, width);
        Assert.Equal(630, height);
    }

    [Fact]
    public async Task GenerateAsync_BackgroundServerNeverResponds_ThrowsFriendlyTimeoutException()
    {
        // A server that accepts connections but never sends a response, so the
        // background image request hangs until the load timeout expires.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var heldConnections = new List<TcpClient>();
        using var cts = new CancellationTokenSource();
        var acceptLoop = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                    heldConnections.Add(await listener.AcceptTcpClientAsync(cts.Token));
            }
            catch (Exception) when (cts.IsCancellationRequested) { /* listener stopped during cleanup */ }
        });

        var outPath = Path.GetTempFileName() + ".png";
        _tempFiles.Add(outPath);

        var templateFile = Path.GetTempFileName() + ".html";
        _tempFiles.Add(templateFile);
        File.WriteAllText(templateFile, "<html><body><img src='{{ bg }}'></body></html>");

        var opts = new GenerateOptions(
            Template: templateFile,
            Title: "Test",
            Subtitle: null,
            Background: $"http://127.0.0.1:{port}/background.jpg",
            Overlays: [],
            Out: outPath,
            Width: 1200,
            Height: 630
        );

        var generator = new ImageGenerator(new TemplateRenderer())
        {
            LoadTimeout = TimeSpan.FromSeconds(3)
        };

        try
        {
            var ex = await Assert.ThrowsAsync<ImageGenerationTimeoutException>(() => generator.GenerateAsync(opts));

            Assert.Contains("127.0.0.1", ex.Message);
            Assert.Contains("3 seconds", ex.Message);
            Assert.DoesNotContain("Navigation", ex.Message);
            Assert.IsType<NavigationException>(ex.InnerException);
            Assert.False(File.Exists(outPath), "No output should be written when loading times out.");
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            await acceptLoop;
            foreach (var client in heldConnections) client.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch { /* best-effort cleanup */ }
        }
        GC.SuppressFinalize(this);
    }
}
