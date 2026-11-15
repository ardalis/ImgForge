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
        Assert.Contains("\"dropboxFolders\": []", stdout);
        Assert.Contains("\"templates\": []", stdout);
        Assert.False(Store.Exists);
    }

    [Fact]
    public async Task Show_WithSettings_ShowsValues()
    {
        Store.Save(new UserSettings { DropboxFolders = ["https://www.dropbox.com/scl/fo/abc"] });

        var (exitCode, stdout, _) = await InvokeAsync("config", "show");

        Assert.Equal(0, exitCode);
        Assert.Contains($"Settings file: {Store.FilePath}{Environment.NewLine}", stdout);
        Assert.Contains("\"https://www.dropbox.com/scl/fo/abc\"", stdout);
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
    public async Task Add_DropboxUrl_StoresUrlAsGiven()
    {
        const string url = "https://www.dropbox.com/scl/fo/abc123/xyz?rlkey=k&dl=0";

        var (exitCode, _, _) = await InvokeAsync("config", "add", "dropbox-folders", url);

        Assert.Equal(0, exitCode);
        Assert.Equal([url], Store.Load().DropboxFolders);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/folder")]
    [InlineData("/local/path")]
    public async Task Add_DropboxNonHttpValue_FailsWithoutSaving(string value)
    {
        var (exitCode, _, stderr) = await InvokeAsync("config", "add", "dropbox-folders", value);

        Assert.Equal(1, exitCode);
        Assert.Contains("isn't an HTTP(S) URL", stderr);
        Assert.False(Store.Exists);
    }

    [Theory]
    [InlineData("https://example.com/scl/fo/abc")]
    [InlineData("https://dropbox.com.evil.example/scl/fo/abc")]
    [InlineData("https://www.dropbox.com/")]
    public async Task Add_DropboxNonDropboxUrl_FailsWithoutSaving(string value)
    {
        var (exitCode, _, stderr) = await InvokeAsync("config", "add", "dropbox-folders", value);

        Assert.Equal(1, exitCode);
        Assert.Contains("isn't a Dropbox link", stderr);
        Assert.False(Store.Exists);
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

        await InvokeAsync("config", "add", "dropbox-folders", "https://www.dropbox.com/scl/fo/a");

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
        Store.Save(new UserSettings { DropboxFolders = ["https://www.dropbox.com/scl/fo/a"] });

        var (exitCode, _, stderr) = await InvokeAsync("config", "remove", "dropbox-folders", "https://www.dropbox.com/scl/fo/b");

        Assert.Equal(1, exitCode);
        Assert.Contains("Error: dropbox-folders doesn't contain 'https://www.dropbox.com/scl/fo/b'.", stderr);
        Assert.Single(Store.Load().DropboxFolders);
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
    public async Task Remove_HandEditedInvalidUrl_CanStillBeRemoved()
    {
        Store.Save(new UserSettings { DropboxFolders = ["junk"] });

        var (exitCode, _, _) = await InvokeAsync("config", "remove", "dropbox-folders", "junk");

        Assert.Equal(0, exitCode);
        Assert.Empty(Store.Load().DropboxFolders);
    }

    [Fact]
    public void Find_RejectsUnknownNames()
    {
        Assert.Same(UserSetting.DropboxFolders, UserSetting.Find("dropbox-folders"));

        var ex = Assert.Throws<ArgumentException>(() => UserSetting.Find("nope"));
        Assert.Contains("image-directories, dropbox-folders, templates", ex.Message);
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> InvokeAsync(params string[] args)
    {
        var console = new TestConsole();
        var exitCode = await Cli.BuildParser(Store).InvokeAsync(args, console);
        return (exitCode, console.Out.ToString()!, console.Error.ToString()!);
    }
}
