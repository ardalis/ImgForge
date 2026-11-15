using System.Text.Json;
using ImgForge.Core.Configuration;

namespace ImgForge.Tests;

public class UserSettingsStoreTests
{
    [Fact]
    public void GetDefaultDirectory_WithoutOverride_IsImgForgeFolderInUserProfile()
    {
        var directory = UserSettingsStore.GetDefaultDirectory(_ => null);

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".imgforge"),
            directory);
    }

    [Fact]
    public void GetDefaultDirectory_WithOverride_UsesEnvironmentVariable()
    {
        var overrideDirectory = Path.Combine(Path.GetTempPath(), "custom-imgforge");

        var directory = UserSettingsStore.GetDefaultDirectory(
            name => name == UserSettingsStore.DirectoryEnvironmentVariable ? $"  {overrideDirectory}  " : null);

        Assert.Equal(overrideDirectory, directory);
    }

    [Fact]
    public void GetDefaultDirectory_WithBlankOverride_UsesUserProfile()
    {
        var directory = UserSettingsStore.GetDefaultDirectory(_ => "   ");

        Assert.Equal(UserSettingsStore.GetDefaultDirectory(_ => null), directory);
    }

    [Fact]
    public void FilePath_IsSettingsJsonInDirectory()
    {
        using var temp = new TempSettingsDirectory();

        Assert.Equal(Path.Combine(temp.Store.Directory, "settings.json"), temp.Store.FilePath);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsEmptySettings()
    {
        using var temp = new TempSettingsDirectory();

        var settings = temp.Store.Load();

        Assert.False(temp.Store.Exists);
        Assert.Empty(settings.ImageDirectories);
        Assert.Empty(settings.Templates);
        Assert.False(settings.HadRemovedSettings);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsAndCreatesFolder()
    {
        using var temp = new TempSettingsDirectory();
        var saved = new UserSettings
        {
            ImageDirectories = ["/images/a", "/images/b"],
            Templates = ["/templates/podcast.html"],
        };

        temp.Store.Save(saved);
        var loaded = temp.Store.Load();

        Assert.True(temp.Store.Exists);
        Assert.Equal(saved.ImageDirectories, loaded.ImageDirectories);
        Assert.Equal(saved.Templates, loaded.Templates);
        Assert.Equal([temp.Store.FilePath], Directory.GetFiles(temp.Store.Directory));
    }

    [Fact]
    public void Save_WritesIndentedCamelCaseJson()
    {
        using var temp = new TempSettingsDirectory();

        temp.Store.Save(new UserSettings { ImageDirectories = ["/images"] });

        using var json = JsonDocument.Parse(File.ReadAllText(temp.Store.FilePath));
        Assert.Equal("/images", json.RootElement.GetProperty("imageDirectories")[0].GetString());
        Assert.False(json.RootElement.TryGetProperty("dropboxFolders", out _));
        Assert.Equal(0, json.RootElement.GetProperty("templates").GetArrayLength());
        Assert.Contains(Environment.NewLine + "  \"imageDirectories\"", File.ReadAllText(temp.Store.FilePath));
    }

    [Fact]
    public void Save_KeepsUnknownProperties()
    {
        using var temp = new TempSettingsDirectory();
        temp.WriteSettingsFile("""{ "imageDirectories": ["/a"], "futureSetting": { "x": 1 } }""");

        var settings = temp.Store.Load();
        settings.Templates.Add("/t");
        temp.Store.Save(settings);

        using var json = JsonDocument.Parse(File.ReadAllText(temp.Store.FilePath));
        Assert.Equal(1, json.RootElement.GetProperty("futureSetting").GetProperty("x").GetInt32());
        Assert.Equal("/t", json.RootElement.GetProperty("templates")[0].GetString());
    }

    [Fact]
    public void Load_AcceptsCommentsTrailingCommasNullsAndAnyCase()
    {
        using var temp = new TempSettingsDirectory();
        temp.WriteSettingsFile("""
            {
              // hand-edited
              "ImageDirectories": ["/a",],
              "templates": null,
            }
            """);

        var settings = temp.Store.Load();

        Assert.Equal(["/a"], settings.ImageDirectories);
        Assert.Empty(settings.Templates);
    }

    [Theory]
    [InlineData("dropboxFolders")]
    [InlineData("DropboxFolders")]
    public void Load_RemovedDropboxFoldersSetting_LoadsAndDropsIt(string propertyName)
    {
        using var temp = new TempSettingsDirectory();
        temp.WriteSettingsFile($$"""
            {
              "imageDirectories": ["/a"],
              "{{propertyName}}": ["https://www.dropbox.com/scl/fo/abc?rlkey=secret"],
              "futureSetting": 1
            }
            """);

        var settings = temp.Store.Load();

        Assert.True(settings.HadRemovedSettings);
        Assert.Equal(["/a"], settings.ImageDirectories);
        Assert.DoesNotContain(propertyName, settings.AdditionalProperties!.Keys);
        Assert.Contains("futureSetting", settings.AdditionalProperties.Keys);
        Assert.DoesNotContain("rlkey", UserSettingsStore.ToJson(settings));
    }

    [Fact]
    public void Save_AfterLoadingRemovedSetting_RemovesItFromFile()
    {
        using var temp = new TempSettingsDirectory();
        temp.WriteSettingsFile("""{ "dropboxFolders": ["https://www.dropbox.com/scl/fo/abc?rlkey=secret"], "templates": ["/t"] }""");

        temp.Store.Save(temp.Store.Load());

        var json = File.ReadAllText(temp.Store.FilePath);
        Assert.DoesNotContain("dropboxFolders", json);
        Assert.DoesNotContain("rlkey", json);
        Assert.Equal(["/t"], temp.Store.Load().Templates);
        Assert.False(temp.Store.Load().HadRemovedSettings);
    }

    [Fact]
    public void Load_JsonNull_ReturnsEmptySettings()
    {
        using var temp = new TempSettingsDirectory();
        temp.WriteSettingsFile("null");

        Assert.Empty(temp.Store.Load().ImageDirectories);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{ "imageDirectories": "/not-a-list" }""")]
    public void Load_InvalidFile_ThrowsWithPathAndHowToRecover(string contents)
    {
        using var temp = new TempSettingsDirectory();
        temp.WriteSettingsFile(contents);

        var ex = Assert.Throws<InvalidDataException>(() => temp.Store.Load());

        Assert.Contains(temp.Store.FilePath, ex.Message);
        Assert.Contains("imgforge config clear", ex.Message);
    }

    [Fact]
    public void Delete_RemovesFileAndReportsWhetherOneExisted()
    {
        using var temp = new TempSettingsDirectory();
        temp.Store.Save(new UserSettings());

        Assert.True(temp.Store.Delete());
        Assert.False(temp.Store.Exists);
        Assert.False(temp.Store.Delete());
    }
}
