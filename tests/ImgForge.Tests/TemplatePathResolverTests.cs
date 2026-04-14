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
        Directory.CreateDirectory(Path.Combine(cwd.TempPath, ".imgforge"));
        File.WriteAllText(Path.Combine(cwd.TempPath, ".imgforge", "template.html"), "<html></html>");

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
        public string TempPath { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public WorkingDirectoryScope()
        {
            Directory.CreateDirectory(TempPath);
            Directory.SetCurrentDirectory(TempPath);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalPath);
            try
            {
                Directory.Delete(TempPath, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
