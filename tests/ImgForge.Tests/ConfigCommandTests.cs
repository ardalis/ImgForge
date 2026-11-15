using System.CommandLine.IO;
using System.CommandLine.Parsing;
using ImgForge.Core.Configuration;

namespace ImgForge.Tests;

public sealed class ConfigCommandTests : IDisposable
{
    private readonly TempSettingsDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private UserSettingsStore Store => _temp.Store;

    [Fact]
    public async Task Show_WithNoFile_ShowsPathAndEmptySettings()
    {
        var (exitCode, stdout, stderr) = await InvokeAsync("config", "show");

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        Assert.Contains($"Settings file: {Store.FilePath} (not created yet)", stdout);
        Assert.Contains("\"imageDirectories\": []", stdout);
        Assert.DoesNotContain("dropbox", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"templates\": []", stdout);
        Assert.False(Store.Exists);
    }

    [Fact]
    public async Task Show_WithSettings_ShowsValues()
    {
        Store.Save(new UserSettings { Templates = ["/my/template"] });

        var (exitCode, stdout, stderr) = await InvokeAsync("config", "show");

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        Assert.Contains($"Settings file: {Store.FilePath}{Environment.NewLine}", stdout);
        Assert.Contains("\"/my/template\"", stdout);
    }

    [Fact]
    public async Task Show_FileWithRemovedDropboxFolders_HidesValuesAndExplains()
    {
        _temp.WriteSettingsFile("""{ "dropboxFolders": ["https://www.dropbox.com/scl/fo/abc?rlkey=secret"], "templates": ["/t"] }""");

        var (exitCode, stdout, stderr) = await InvokeAsync("config", "show");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("dropbox", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"/t\"", stdout);
        Assert.Contains("no longer uses (such as dropboxFolders)", stderr);
        Assert.Contains("'imgforge config clear'", stderr);
    }

    [Theory]
    [InlineData("add", "dropbox-folders", "https://www.dropbox.com/scl/fo/abc")]
    [InlineData("remove", "dropbox-folders", "https://www.dropbox.com/scl/fo/abc")]
    [InlineData("clear", "dropbox-folders")]
    public async Task RemovedDropboxFoldersSetting_FailsPointingToImageDirectories(params string[] args)
    {
        _temp.WriteSettingsFile("""{ "dropboxFolders": ["https://www.dropbox.com/scl/fo/abc"] }""");
        var before = File.ReadAllText(Store.FilePath);

        var (exitCode, _, stderr) = await InvokeAsync(["config", .. args]);

        Assert.Equal(1, exitCode);
        Assert.Contains("The dropbox-folders setting was removed", stderr);
        Assert.Contains("imgforge config add image-directories", stderr);
        Assert.DoesNotContain("not recognized", stderr);
        Assert.Equal(before, File.ReadAllText(Store.FilePath));
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("clear")]
    public void SettingArgument_CompletesValidSettingNamesOnly(string subcommand)
    {
        var completions = Cli.BuildParser(Store).Parse($"config {subcommand} ")
            .GetCompletions($"config {subcommand} ".Length)
            .Select(c => c.Label)
            .ToList();

        Assert.Contains("image-directories", completions);
        Assert.Contains("templates", completions);
        Assert.DoesNotContain("dropbox-folders", completions);
    }

    [Fact]
    public async Task Add_AfterRemovedDropboxFolders_DropsThemFromFile()
    {
        _temp.WriteSettingsFile("""{ "dropboxFolders": ["https://www.dropbox.com/scl/fo/abc?rlkey=secret"] }""");

        var (exitCode, _, _) = await InvokeAsync("config", "add", "templates", "/t");

        Assert.Equal(0, exitCode);
        var json = File.ReadAllText(Store.FilePath);
        Assert.DoesNotContain("dropboxFolders", json);
        Assert.DoesNotContain("rlkey", json);
    }

    [Fact]
    public async Task Show_MalformedFile_FailsWithClearError()
    {
        _temp.WriteSettingsFile("{ oops");

        var (exitCode, _, stderr) = await InvokeAsync("config", "show");

        Assert.Equal(1, exitCode);
        Assert.StartsWith($"Error: The settings file '{Store.FilePath}' isn't valid", stderr);
    }

    [Fact]
    public async Task Path_PrintsSettingsFilePath()
    {
        var (exitCode, stdout, _) = await InvokeAsync("config", "path");

        Assert.Equal(0, exitCode);
        Assert.Equal(Store.FilePath, stdout.Trim());
    }

    [Fact]
    public async Task Add_ExistingDirectory_StoresFullPathWithoutWarning()
    {
        var images = Path.Combine(_temp.Root, "images");
        Directory.CreateDirectory(images);

        var (exitCode, stdout, stderr) = await InvokeAsync(
            "config", "add", "image-directories", Path.Combine(images, "sub", "..") + Path.DirectorySeparatorChar);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        Assert.Contains($"Added '{images}' to image-directories.", stdout);
        Assert.Equal([images], Store.Load().ImageDirectories);
    }

    [Fact]
    public async Task Add_MissingPath_StoresItAndWarns()
    {
        var missing = Path.Combine(_temp.Root, "not-there.html");

        var (exitCode, _, stderr) = await InvokeAsync("config", "add", "templates", missing);

        Assert.Equal(0, exitCode);
        Assert.Contains($"Warning: '{missing}' doesn't exist.", stderr);
        Assert.Equal([missing], Store.Load().Templates);
    }

    [Fact]
    public void Normalize_TildePath_ExpandsToUserProfile()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(Path.Combine(profile, "Pictures"), UserSetting.ImageDirectories.Normalize("~/Pictures"));
        Assert.Equal(Path.TrimEndingDirectorySeparator(profile), UserSetting.ImageDirectories.Normalize("~"));
    }

    [Fact]
    public async Task Add_Duplicate_DoesNotAddTwice()
    {
        var images = Path.Combine(_temp.Root, "images");
        await InvokeAsync("config", "add", "image-directories", images);

        var (exitCode, stdout, _) = await InvokeAsync("config", "add", "image-directories", images);

        Assert.Equal(0, exitCode);
        Assert.Contains($"image-directories already contains '{images}'.", stdout);
        Assert.Equal([images], Store.Load().ImageDirectories);
    }

    [Fact]
    public async Task Add_KeepsOtherSettingsAndUnknownProperties()
    {
        _temp.WriteSettingsFile("""{ "templates": ["/t"], "extra": true }""");

        await InvokeAsync("config", "add", "image-directories", "/images");

        var json = File.ReadAllText(Store.FilePath);
        Assert.Contains("\"extra\": true", json);
        Assert.Equal(["/t"], Store.Load().Templates);
    }

    [Fact]
    public async Task Add_UnknownSetting_IsParseError()
    {
        var (exitCode, _, stderr) = await InvokeAsync("config", "add", "colors", "red");

        Assert.Equal(1, exitCode);
        Assert.Contains("Argument 'colors' not recognized", stderr);
        Assert.False(Store.Exists);
    }

    [Fact]
    public async Task Remove_PresentValue_RemovesIt()
    {
        var images = Path.Combine(_temp.Root, "images");
        var other = Path.Combine(_temp.Root, "other");
        Store.Save(new UserSettings { ImageDirectories = [images, other] });

        var (exitCode, stdout, _) = await InvokeAsync(
            "config", "remove", "image-directories", images + Path.DirectorySeparatorChar);

        Assert.Equal(0, exitCode);
        Assert.Contains($"Removed '{images}' from image-directories.", stdout);
        Assert.Equal([other], Store.Load().ImageDirectories);
    }

    [Fact]
    public async Task Remove_MissingValue_Fails()
    {
        Store.Save(new UserSettings { Templates = ["/t/a"] });

        var (exitCode, _, stderr) = await InvokeAsync("config", "remove", "templates", "/t/b");

        Assert.Equal(1, exitCode);
        Assert.Contains("Error: templates doesn't contain '/t/b'.", stderr);
        Assert.Single(Store.Load().Templates);
    }

    [Fact]
    public async Task Clear_WithoutSetting_DeletesFile()
    {
        Store.Save(new UserSettings { Templates = ["/t"] });

        var (exitCode, stdout, _) = await InvokeAsync("config", "clear");

        Assert.Equal(0, exitCode);
        Assert.Contains($"Deleted all settings ({Store.FilePath}).", stdout);
        Assert.False(Store.Exists);
    }

    [Fact]
    public async Task Clear_WithoutSettingOrFile_SaysNothingToClear()
    {
        var (exitCode, stdout, _) = await InvokeAsync("config", "clear");

        Assert.Equal(0, exitCode);
        Assert.Contains("No settings to clear", stdout);
    }

    [Fact]
    public async Task Clear_MalformedFile_StillDeletesIt()
    {
        _temp.WriteSettingsFile("{ oops");

        var (exitCode, _, _) = await InvokeAsync("config", "clear");

        Assert.Equal(0, exitCode);
        Assert.False(Store.Exists);
    }

    [Fact]
    public async Task Clear_OneSetting_EmptiesOnlyThatSetting()
    {
        Store.Save(new UserSettings { ImageDirectories = ["/a"], Templates = ["/t"] });

        var (exitCode, stdout, _) = await InvokeAsync("config", "clear", "image-directories");

        Assert.Equal(0, exitCode);
        Assert.Contains("Cleared image-directories.", stdout);
        var settings = Store.Load();
        Assert.Empty(settings.ImageDirectories);
        Assert.Equal(["/t"], settings.Templates);
    }

    [Fact]
    public async Task Clear_EmptySetting_SaysAlreadyEmpty()
    {
        var (exitCode, stdout, _) = await InvokeAsync("config", "clear", "templates");

        Assert.Equal(0, exitCode);
        Assert.Contains("templates is already empty.", stdout);
        Assert.False(Store.Exists);
    }

    [Theory]
    [InlineData("/hand/edited/", "/hand/edited")]
    [InlineData("/hand/edited", "/hand/edited/")]
    [InlineData("~/hand-edited", "~/hand-edited")]
    public async Task Remove_HandEditedPath_MatchesAsTypedOrNormalized(string stored, string argument)
    {
        Store.Save(new UserSettings { ImageDirectories = [stored, "/keep"] });

        var (exitCode, stdout, _) = await InvokeAsync("config", "remove", "image-directories", argument);

        Assert.Equal(0, exitCode);
        Assert.Contains($"Removed '{stored}' from image-directories.", stdout);
        Assert.Equal(["/keep"], Store.Load().ImageDirectories);
    }

    [Fact]
    public void Find_RejectsUnknownNames()
    {
        Assert.Same(UserSetting.Templates, UserSetting.Find("templates"));

        var ex = Assert.Throws<ArgumentException>(() => UserSetting.Find("nope"));
        Assert.Contains("Valid settings: image-directories, templates.", ex.Message);

        var removed = Assert.Throws<ArgumentException>(() => UserSetting.Find("dropbox-folders"));
        Assert.Contains("image-directories", removed.Message);
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> InvokeAsync(params string[] args)
    {
        var console = new TestConsole();
        var exitCode = await Cli.BuildParser(Store).InvokeAsync(args, console);
        return (exitCode, console.Out.ToString()!, console.Error.ToString()!);
    }
}
