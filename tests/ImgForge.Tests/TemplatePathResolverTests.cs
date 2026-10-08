using ImgForge.Core;

namespace ImgForge.Tests;

public class TemplatePathResolverTests
{
    [Fact]
    public void ResolveTemplate_WhenProvided_ReturnsProvidedTemplate()
    {
        using var cwd = new WorkingDirectoryScope();
        cwd.CreateFile(".tools/imgforge/template.html");

        var result = TemplatePathResolver.ResolveTemplate("blog");

        Assert.Equal("blog", result.Template);
        Assert.False(result.UsedDefault);
    }

    [Fact]
    public void ResolveTemplate_WhenMissingAndToolsDirDefaultExists_ReturnsToolsDirTemplate()
    {
        using var cwd = new WorkingDirectoryScope();
        cwd.CreateFile(".tools/imgforge/template.html");

        var result = TemplatePathResolver.ResolveTemplate(null);

        Assert.Equal(".tools/imgforge/template.html", result.Template);
        Assert.True(result.UsedDefault);
    }

    [Fact]
    public void ResolveTemplate_WhenMissingAndLegacyDefaultExists_ReturnsLegacyTemplate()
    {
        using var cwd = new WorkingDirectoryScope();
        cwd.CreateFile(".imgforge/template.html");

        var result = TemplatePathResolver.ResolveTemplate(null);

        Assert.Equal(".imgforge/template.html", result.Template);
        Assert.True(result.UsedDefault);
    }

    [Fact]
    public void ResolveTemplate_WhenMissingAndBothDefaultsExist_PrefersToolsDirTemplate()
    {
        using var cwd = new WorkingDirectoryScope();
        cwd.CreateFile(".tools/imgforge/template.html");
        cwd.CreateFile(".imgforge/template.html");

        var result = TemplatePathResolver.ResolveTemplate(null);

        Assert.Equal(".tools/imgforge/template.html", result.Template);
        Assert.True(result.UsedDefault);
    }

    [Fact]
    public void ResolveTemplate_WhenMissingAndNoDefault_ThrowsHelpfulMessage()
    {
        using var _ = new WorkingDirectoryScope();

        var ex = Assert.Throws<ArgumentException>(() => TemplatePathResolver.ResolveTemplate(null));

        Assert.Equal(
            "No template was provided on the command line and no default template exists at '.tools/imgforge/template.html' or '.imgforge/template.html' " +
            "in the current directory or any parent directory up to the repository root.",
            ex.Message);
    }

    [Fact]
    public void ResolveTemplate_WhenRunFromRepoSubdirectory_FindsTemplateAtRepoRoot()
    {
        using var cwd = new WorkingDirectoryScope();
        cwd.CreateDirectory(".git");
        cwd.CreateFile(".tools/imgforge/template.html");
        cwd.ChangeTo("src/nested");

        var result = TemplatePathResolver.ResolveTemplate(null);

        Assert.Equal(cwd.FullPathOf(".tools/imgforge/template.html"), result.Template);
        Assert.True(result.UsedDefault);
    }

    [Fact]
    public void ResolveTemplate_WhenRepoRootIsWorktree_FindsTemplateAtRepoRoot()
    {
        using var cwd = new WorkingDirectoryScope();
        cwd.CreateFile(".git"); // worktrees and submodules use a .git file
        cwd.CreateFile(".tools/imgforge/template.html");
        cwd.ChangeTo("src");

        var result = TemplatePathResolver.ResolveTemplate(null);

        Assert.Equal(cwd.FullPathOf(".tools/imgforge/template.html"), result.Template);
    }

    [Fact]
    public void ResolveTemplate_WhenLegacyTemplateInParent_ReturnsLegacyTemplate()
    {
        using var cwd = new WorkingDirectoryScope();
        cwd.CreateDirectory(".git");
        cwd.CreateFile(".imgforge/template.html");
        cwd.ChangeTo("src");

        var result = TemplatePathResolver.ResolveTemplate(null);

        Assert.Equal(cwd.FullPathOf(".imgforge/template.html"), result.Template);
        Assert.True(TemplatePathResolver.IsLegacyTemplatePath(result.Template));
    }

    [Fact]
    public void ResolveTemplate_WhenTemplatesAtMultipleLevels_PrefersNearestDirectory()
    {
        using var cwd = new WorkingDirectoryScope();
        cwd.CreateDirectory(".git");
        cwd.CreateFile(".tools/imgforge/template.html");
        cwd.CreateFile("src/.imgforge/template.html");
        cwd.ChangeTo("src/nested");

        var result = TemplatePathResolver.ResolveTemplate(null);

        Assert.Equal(cwd.FullPathOf("src/.imgforge/template.html"), result.Template);
    }

    [Fact]
    public void ResolveTemplate_WhenTemplateIsAboveRepoRoot_Throws()
    {
        using var cwd = new WorkingDirectoryScope();
        cwd.CreateFile(".tools/imgforge/template.html");
        cwd.CreateDirectory("repo/.git");
        cwd.ChangeTo("repo/src");

        Assert.Throws<ArgumentException>(() => TemplatePathResolver.ResolveTemplate(null));
    }

    [Fact]
    public void ResolveTemplate_WhenNotInRepository_DoesNotSearchParentDirectories()
    {
        using var cwd = new WorkingDirectoryScope();
        cwd.CreateFile(".tools/imgforge/template.html");
        cwd.ChangeTo("src");

        Assert.Throws<ArgumentException>(() => TemplatePathResolver.ResolveTemplate(null));
    }

    [Theory]
    [InlineData(".imgforge/template.html", true)]
    [InlineData("/repo/.imgforge/template.html", true)]
    [InlineData(@"C:\repo\.imgforge\template.html", true)]
    [InlineData(".tools/imgforge/template.html", false)]
    [InlineData("/repo/my.imgforge/template.html", false)]
    public void IsLegacyTemplatePath_DetectsLegacyLocation(string path, bool expected)
    {
        Assert.Equal(expected, TemplatePathResolver.IsLegacyTemplatePath(path));
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

        public void CreateFile(string relativePath)
        {
            var fullPath = Path.Combine(TempPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, "<html></html>");
        }

        public void CreateDirectory(string relativePath) =>
            Directory.CreateDirectory(Path.Combine(TempPath, relativePath));

        public void ChangeTo(string relativePath)
        {
            var fullPath = Path.Combine(TempPath, relativePath);
            Directory.CreateDirectory(fullPath);
            Directory.SetCurrentDirectory(fullPath);
        }

        public string FullPathOf(string relativePath) =>
            Path.GetFullPath(Path.Combine(TempPath, relativePath));

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
