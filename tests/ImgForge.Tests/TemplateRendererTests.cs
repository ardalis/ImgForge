using ImgForge.Core;

namespace ImgForge.Tests;

public class TemplateRendererTests
{
    private readonly TemplateRenderer _renderer = new();

    [Fact]
    public void Render_InlineTemplate_SubstitutesAllVariables()
    {
        const string templateHtml = "<h1>{{ title }}</h1><p>{{ width }}x{{ height }}</p>";
        var opts = new GenerateOptions(
            Template: templateHtml,
            Title: "Hello World",
            Background: null,
            Overlays: [],
            Out: "out.png",
            Width: 1200,
            Height: 630
        );

        // Treat as inline by passing with path separator so it's not treated as built-in,
        // but we actually need to test inline: use a temp file path approach instead.
        // For inline we write a temp file and pass its path.
        var tempFile = Path.GetTempFileName() + ".html";
        try
        {
            File.WriteAllText(tempFile, templateHtml);
            var optsWithFile = opts with { Template = tempFile };
            var result = _renderer.Render(optsWithFile);
            Assert.Contains("Hello World", result);
            Assert.Contains("1200", result);
            Assert.Contains("630", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Render_BuiltInBlogTemplate_DoesNotThrow()
    {
        var opts = new GenerateOptions(
            Template: "blog",
            Title: "Test Title",
            Background: null,
            Overlays: [],
            Out: "out.png"
        );

        var result = _renderer.Render(opts);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Render_BuiltInYoutubeTemplate_DoesNotThrow()
    {
        var opts = new GenerateOptions(
            Template: "youtube",
            Title: "Test Title",
            Background: null,
            Overlays: [],
            Out: "out.png",
            Width: 1280,
            Height: 720
        );

        var result = _renderer.Render(opts);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Render_LocalFileBackground_ConvertsToFileUri()
    {
        var tempBg = Path.GetTempFileName() + ".jpg";
        var tempTemplate = Path.GetTempFileName() + ".html";
        try
        {
            File.WriteAllText(tempBg, "fake");
            File.WriteAllText(tempTemplate, "{{ bg }}");

            var opts = new GenerateOptions(
                Template: tempTemplate,
                Title: "T",
                Background: tempBg,
                Overlays: [],
                Out: "out.png"
            );

            var result = _renderer.Render(opts);
            Assert.Contains("file:///", result);
        }
        finally
        {
            File.Delete(tempBg);
            File.Delete(tempTemplate);
        }
    }

    [Theory]
    [InlineData("https://example.com/bg.jpg")]
    [InlineData("http://example.com/bg.jpg")]
    public void Render_HttpBackground_PassesThroughUnchanged(string url)
    {
        var tempTemplate = Path.GetTempFileName() + ".html";
        try
        {
            File.WriteAllText(tempTemplate, "{{ bg }}");

            var opts = new GenerateOptions(
                Template: tempTemplate,
                Title: "T",
                Background: url,
                Overlays: [],
                Out: "out.png"
            );

            var result = _renderer.Render(opts);
            Assert.Contains(url, result);
        }
        finally
        {
            File.Delete(tempTemplate);
        }
    }

    [Fact]
    public void Render_MissingVariable_DoesNotThrow()
    {
        var tempTemplate = Path.GetTempFileName() + ".html";
        try
        {
            File.WriteAllText(tempTemplate, "{{ undefined_variable }}");

            var opts = new GenerateOptions(
                Template: tempTemplate,
                Title: "T",
                Background: null,
                Overlays: [],
                Out: "out.png"
            );

            var result = _renderer.Render(opts);
            Assert.NotNull(result);
        }
        finally
        {
            File.Delete(tempTemplate);
        }
    }

    [Fact]
    public void Render_FileTemplate_InjectsBaseTagPointingToTemplateDir()
    {
        var tempTemplate = Path.GetTempFileName() + ".html";
        try
        {
            File.WriteAllText(tempTemplate, "<html><head></head><body>{{ title }}</body></html>");
            var expectedDir = Path.GetDirectoryName(Path.GetFullPath(tempTemplate))!
                .Replace('\\', '/');

            var opts = new GenerateOptions(
                Template: tempTemplate,
                Title: "Test",
                Background: null,
                Overlays: [],
                Out: "out.png"
            );

            var result = _renderer.Render(opts);
            Assert.Contains("<base href=\"file:///" + expectedDir + "/\"", result);
        }
        finally
        {
            File.Delete(tempTemplate);
        }
    }

    [Fact]
    public void Render_TemplateFolder_LoadsTemplateHtmlAndInjectsBaseTag()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var templateFile = Path.Combine(tempDir, "template.html");
            File.WriteAllText(templateFile, "<html><head></head><body>{{ title }}</body></html>");

            var opts = new GenerateOptions(
                Template: tempDir,
                Title: "Folder Template",
                Background: null,
                Overlays: [],
                Out: "out.png"
            );

            var result = _renderer.Render(opts);
            Assert.Contains("Folder Template", result);
            var expectedDir = tempDir.Replace('\\', '/');
            Assert.Contains("<base href=\"file:///" + expectedDir + "/\"", result);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Render_TemplateFolder_MissingTemplateHtml_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var opts = new GenerateOptions(
                Template: tempDir,
                Title: "T",
                Background: null,
                Overlays: [],
                Out: "out.png"
            );

            Assert.Throws<FileNotFoundException>(() => _renderer.Render(opts));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Render_BuiltInTemplate_DoesNotInjectBaseTag()
    {
        var opts = new GenerateOptions(
            Template: "blog",
            Title: "No Base Tag",
            Background: null,
            Overlays: [],
            Out: "out.png"
        );

        var result = _renderer.Render(opts);
        Assert.DoesNotContain("<base href=", result);
    }
}
