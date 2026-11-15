using ImgForge.Core;
using ImgForge.Core.RandomBackgrounds;

namespace ImgForge.Tests;

[Trait("Category", "Integration")]
public class ChromiumImageResizerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("imgforge-resize-").FullName;

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(300, 600)]
    public async Task ResizeAsync_Png_ReturnsPngOfExactlyTheRequestedSize(int width, int height)
    {
        var source = await RenderSourceImageAsync("wide.png", 800, 200);

        var image = await NewResizer().ResizeAsync(source, width, height, CancellationToken.None);

        Assert.Equal("image/png", image.MediaType);
        var output = Path.Combine(_root, $"out-{width}x{height}.png");
        await File.WriteAllBytesAsync(output, image.Content);
        Assert.Equal((width, height), PngHeader.ReadDimensions(output));
    }

    [Fact]
    public async Task ResizeAsync_JpegExtension_ReturnsJpeg()
    {
        // Chromium decodes by content, so a PNG saved as .jpg is still an image; the output format follows the name.
        var source = await RenderSourceImageAsync("photo.jpg", 400, 400);

        var image = await NewResizer().ResizeAsync(source, 200, 100, CancellationToken.None);

        Assert.Equal("image/jpeg", image.MediaType);
        Assert.Equal([0xFF, 0xD8, 0xFF], image.Content[..3]);
    }

    [Fact]
    public async Task ResizeAsync_PathWithSpacesAndSpecialCharacters_Works()
    {
        var source = await RenderSourceImageAsync("my photos/#1 & best 100%.png", 300, 300);

        var image = await NewResizer().ResizeAsync(source, 120, 80, CancellationToken.None);

        var output = Path.Combine(_root, "special.png");
        await File.WriteAllBytesAsync(output, image.Content);
        Assert.Equal((120, 80), PngHeader.ReadDimensions(output));
    }

    [Fact]
    public async Task ResizeAsync_NotAnImage_ThrowsProviderException()
    {
        var source = Path.Combine(_root, "broken.png");
        await File.WriteAllTextAsync(source, "this is not an image");

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => NewResizer().ResizeAsync(source, 100, 100, CancellationToken.None));

        Assert.Equal($"'{source}' couldn't be read as an image", ex.Message);
        Assert.Empty(Directory.GetFiles(_root, "imgforge-resize-*.html"));
    }

    [Fact]
    public async Task GenerateAsync_RandomLocal_RendersWithAResizedLocalImage()
    {
        await RenderSourceImageAsync(Path.Combine("images", "nested", "bg.png"), 500, 500);
        var template = Path.Combine(_root, "template.html");
        await File.WriteAllTextAsync(template,
            "<html><body style=\"margin:0\"><div style=\"width:640px;height:360px;background-image:url('{{ bg }}');background-size:cover\"></div></body></html>");
        var outPath = Path.Combine(_root, "out.png");
        using var warnings = new StringWriter();
        var generator = new ImageGenerator(new TemplateRenderer())
        {
            ImageDirectories = [Path.Combine(_root, "images")],
            Warnings = warnings
        };

        var result = await generator.GenerateAsync(
            new GenerateOptions(template, "Title", null, "random:local", [], outPath, 640, 360));

        Assert.Equal(outPath, result);
        Assert.Equal((640, 360), PngHeader.ReadDimensions(outPath));
        Assert.Equal("", warnings.ToString());
    }

    private ChromiumImageResizer NewResizer() => new() { TempDirectory = _root };

    /// <summary>Renders a solid-color PNG of the given size with ImgForge itself, to use as a source image.</summary>
    private async Task<string> RenderSourceImageAsync(string relativePath, int width, int height)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var template = Path.Combine(_root, $"source-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(template, "<html><body style=\"margin:0;background:#3366cc\"></body></html>");
        var png = Path.Combine(_root, $"source-{Guid.NewGuid():N}.png");
        await new ImageGenerator(new TemplateRenderer()).GenerateAsync(
            new GenerateOptions(template, "Source", null, null, [], png, width, height));
        File.Move(png, path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
