using System.CommandLine;
using ImgForge.Commands;

namespace ImgForge.Tests;

/// <summary>Tests that swap the process-wide <see cref="Console"/> streams, so they must not run in parallel.</summary>
[CollectionDefinition(nameof(ConsoleCollection), DisableParallelization = true)]
public sealed class ConsoleCollection;

[Collection(nameof(ConsoleCollection))]
public class GenerateCommandBackgroundTests
{
    [Fact]
    public async Task Invoke_InvalidRandomProvider_FailsBeforeResolvingTemplateOrPromptingForFormat()
    {
        // No --format/--width/--height, so a later check would prompt first; the template doesn't exist, so a later
        // check would report that instead. "1" on stdin keeps an unwanted prompt from waiting forever.
        var (exitCode, stdout, stderr) = await InvokeAsync(
            "1" + Environment.NewLine,
            "generate", "--title", "Hello", "--template", "no-such-template.html", "--bg", "random:bogus");

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "Error: Invalid --bg value \"random:bogus\": unknown provider \"bogus\". " +
            "Valid providers: picsum, loremflickr, placeholder, unsplash, pexels, pixabay, dropbox, local (for example: --bg random:picsum).",
            stderr.Trim());
        Assert.DoesNotContain("Choose a destination format", stdout);
    }

    [Fact]
    public void BgOptionHelp_DescribesProviderSyntax()
    {
        var bg = GenerateCommand.Build().Options.Single(o => o.Name == "bg");

        Assert.Contains("random:<provider>", bg.Description);
        Assert.Contains("picsum, loremflickr", bg.Description);
        Assert.Contains("placeholder", bg.Description);
    }

    [Fact]
    public async Task Invoke_RandomLocalWithNoImageFolders_FailsWithHowToAddOne()
    {
        using var settings = new TempSettingsDirectory();

        var (exitCode, stdout, stderr) = await InvokeAsync(
            settings.Store, "1" + Environment.NewLine,
            "generate", "--title", "Hello", "--template", "no-such-template.html", "--bg", "random:local");

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "Error: Invalid --bg value \"random:local\": no image folders are configured. " +
            "Add one with 'imgforge config add image-directories <folder>'.",
            stderr.Trim());
        Assert.DoesNotContain("Choose a destination format", stdout);
    }

    [Fact]
    public void BgOptionHelp_DescribesLocalImageFolders()
    {
        var bg = GenerateCommand.Build().Options.Single(o => o.Name == "bg");

        Assert.Contains("local image folders", bg.Description);
        Assert.Contains("image-directories", bg.Description);
        Assert.Contains("or local", bg.Description);
    }

    [Fact]
    public void LoadImageSources_RandomBackground_ReturnsConfiguredFolders()
    {
        using var settings = new TempSettingsDirectory();
        settings.WriteSettingsFile(
            """{ "imageDirectories": ["/pics/a", "/pics/b"], "dropboxFolders": ["https://www.dropbox.com/scl/fo/a"] }""");

        foreach (var background in new[] { "random", "random:local", "random:dropbox" })
        {
            var sources = GenerateCommand.LoadImageSources(background, () => settings.Store, TextWriter.Null);
            Assert.Equal(["/pics/a", "/pics/b"], sources.Directories);
            Assert.Equal(["https://www.dropbox.com/scl/fo/a"], sources.DropboxFolders);
            Assert.Equal(Path.Combine(settings.Store.Directory, "cache", "dropbox"), sources.DropboxCacheDirectory);
        }
    }

    [Fact]
    public async Task Invoke_RandomDropboxWithNoDropboxFolders_FailsWithHowToAddOne()
    {
        using var settings = new TempSettingsDirectory();
        settings.WriteSettingsFile("""{ "imageDirectories": ["/pics/a"] }""");

        var (exitCode, stdout, stderr) = await InvokeAsync(
            settings.Store, "1" + Environment.NewLine,
            "generate", "--title", "Hello", "--template", "no-such-template.html", "--bg", "random:dropbox");

        Assert.Equal(1, exitCode);
        Assert.Equal(
            "Error: Invalid --bg value \"random:dropbox\": no Dropbox folders are configured. " +
            "Add one with 'imgforge config add dropbox-folders <shared folder link>'.",
            stderr.Trim());
        Assert.DoesNotContain("Choose a destination format", stdout);
    }

    [Fact]
    public void BgOptionHelp_DescribesDropboxFolders()
    {
        var bg = GenerateCommand.Build().Options.Single(o => o.Name == "bg");

        Assert.Contains("dropbox-folders", bg.Description);
        Assert.Contains("dropbox (", bg.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("./bg.jpg")]
    [InlineData("https://example.com/bg.jpg")]
    [InlineData("random:picsum")]
    [InlineData("random:placeholder:0f172a")]
    public void LoadImageSources_NotRandomLocalOrDropbox_DoesNotReadSettings(string? background)
    {
        var sources = GenerateCommand.LoadImageSources(
            background, () => throw new InvalidOperationException("settings should not be read"), TextWriter.Null);

        Assert.Empty(sources.Directories);
        Assert.Empty(sources.DropboxFolders);
    }

    [Fact]
    public void LoadImageSources_PlainRandomWithBrokenSettings_WarnsAndUsesNoFolders()
    {
        using var settings = new TempSettingsDirectory();
        settings.WriteSettingsFile("{ not json");
        using var warnings = new StringWriter();

        var sources = GenerateCommand.LoadImageSources("random", () => settings.Store, warnings);

        Assert.Empty(sources.Directories);
        Assert.Empty(sources.DropboxFolders);
        Assert.StartsWith("Warning: couldn't read your image folders, so they won't be used:", warnings.ToString());
    }

    [Theory]
    [InlineData("random:local")]
    [InlineData("random:dropbox")]
    public void LoadImageSources_SingleProviderWithBrokenSettings_Throws(string background)
    {
        using var settings = new TempSettingsDirectory();
        settings.WriteSettingsFile("{ not json");

        Assert.Throws<InvalidDataException>(() =>
            GenerateCommand.LoadImageSources(background, () => settings.Store, TextWriter.Null));
    }

    private static Task<(int ExitCode, string Stdout, string Stderr)> InvokeAsync(string stdin, params string[] args) =>
        InvokeAsync(null, stdin, args);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> InvokeAsync(
        ImgForge.Core.Configuration.UserSettingsStore? store, string stdin, params string[] args)
    {
        var (originalIn, originalOut, originalError) = (Console.In, Console.Out, Console.Error);
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetIn(new StringReader(stdin));
            Console.SetOut(output);
            Console.SetError(error);

            var root = new RootCommand { store is null ? GenerateCommand.Build() : GenerateCommand.Build(() => store) };
            var exitCode = await root.InvokeAsync(args);
            return (exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
