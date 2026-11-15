using ImgForge.Core.Configuration;

namespace ImgForge.Tests;

/// <summary>A temp folder for a <see cref="UserSettingsStore"/>, deleted on dispose.</summary>
internal sealed class TempSettingsDirectory : IDisposable
{
    public TempSettingsDirectory()
    {
        // A subfolder that doesn't exist yet, so tests also cover creating it.
        Root = Path.Combine(Path.GetTempPath(), "imgforge-settings-" + Guid.NewGuid().ToString("N"));
        Store = new UserSettingsStore(Path.Combine(Root, "config"));
    }

    public string Root { get; }

    public UserSettingsStore Store { get; }

    public void WriteSettingsFile(string json)
    {
        Directory.CreateDirectory(Store.Directory);
        File.WriteAllText(Store.FilePath, json);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}
