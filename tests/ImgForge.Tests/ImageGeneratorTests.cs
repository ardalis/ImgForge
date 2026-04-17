using ImgForge.Core;
using SixLabors.ImageSharp;

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

        using var image = await Image.LoadAsync(outPath);
        Assert.Equal(1200, image.Width);
        Assert.Equal(630, image.Height);
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
