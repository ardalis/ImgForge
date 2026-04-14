using ImgForge.Core;

namespace ImgForge.Tests;

public class TemplatePathResolverTests
{
    [Fact]
    public void ResolveTemplate_WhenProvided_ReturnsProvidedTemplate()
    {
        var result = TemplatePathResolver.ResolveTemplate("blog");

        Assert.Equal("blog", result.Template);
        Assert.False(result.UsedDefault);
    }

    [Fact]
    public void ResolveTemplate_WhenMissingAndDefaultExists_ReturnsDefaultTemplate()
    {
        using var cwd = new WorkingDirectoryScope();
        Directory.CreateDirectory(Path.Combine(cwd.Path, ".imgforge"));
        File.WriteAllText(Path.Combine(cwd.Path, ".imgforge", "template.html"), "<html></html>");

        var result = TemplatePathResolver.ResolveTemplate(null);

        Assert.Equal(".imgforge/template.html", result.Template);
        Assert.True(result.UsedDefault);
    }

    [Fact]
    public void ResolveTemplate_WhenMissingAndNoDefault_ThrowsHelpfulMessage()
    {
        using var _ = new WorkingDirectoryScope();

        var ex = Assert.Throws<ArgumentException>(() => TemplatePathResolver.ResolveTemplate(null));

        Assert.Equal(
            "No template was provided on the command line and no default template exists at '.imgforge/template.html'.",
            ex.Message);
    }

    private sealed class WorkingDirectoryScope : IDisposable
    {
        private readonly string _originalPath = Directory.GetCurrentDirectory();
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public WorkingDirectoryScope()
        {
            Directory.CreateDirectory(Path);
            Directory.SetCurrentDirectory(Path);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalPath);
            Directory.Delete(Path, recursive: true);
        }
    }
}
