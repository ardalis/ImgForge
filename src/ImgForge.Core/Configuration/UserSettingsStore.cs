using System.Text.Json;

namespace ImgForge.Core.Configuration;

/// <summary>
/// Reads and writes <see cref="UserSettings"/> as <c>settings.json</c> in a per-user folder: by default
/// <c>~/.imgforge</c>, or the folder named by the <c>IMGFORGE_CONFIG_DIR</c> environment variable.
/// </summary>
public sealed class UserSettingsStore
{
    public const string FileName = "settings.json";
    public const string DirectoryEnvironmentVariable = "IMGFORGE_CONFIG_DIR";
    public const string DefaultDirectoryName = ".imgforge";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public UserSettingsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = Path.GetFullPath(directory);
        FilePath = Path.Combine(Directory, FileName);
    }

    /// <summary>The folder holding the settings file.</summary>
    public string Directory { get; }

    /// <summary>The full path of the settings file (which may not exist yet).</summary>
    public string FilePath { get; }

    /// <summary>A store in <see cref="GetDefaultDirectory"/>.</summary>
    public static UserSettingsStore CreateDefault() => new(GetDefaultDirectory());

    /// <summary>
    /// <c>IMGFORGE_CONFIG_DIR</c> if set, otherwise <c>.imgforge</c> in the user's profile folder.
    /// </summary>
    /// <param name="getEnvironmentVariable">Reads an environment variable; tests pass a fake.</param>
    public static string GetDefaultDirectory(Func<string, string?>? getEnvironmentVariable = null)
    {
        var overrideDirectory = (getEnvironmentVariable ?? Environment.GetEnvironmentVariable)(DirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
            return Path.GetFullPath(overrideDirectory.Trim());

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile))
        {
            throw new InvalidOperationException(
                $"Can't find your user profile folder to store settings in. Set {DirectoryEnvironmentVariable} to the folder to use.");
        }
        return Path.Combine(profile, DefaultDirectoryName);
    }

    /// <summary>True when the settings file exists.</summary>
    public bool Exists => File.Exists(FilePath);

    /// <summary>Loads the settings, or returns empty settings when the file doesn't exist.</summary>
    /// <exception cref="InvalidDataException">The file isn't valid settings JSON.</exception>
    public UserSettings Load()
    {
        if (!File.Exists(FilePath))
            return new UserSettings();

        UserSettings? settings;
        try
        {
            using var stream = File.OpenRead(FilePath);
            settings = JsonSerializer.Deserialize<UserSettings>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"The settings file '{FilePath}' isn't valid: {ex.Message} Fix it, or run 'imgforge config clear' to start over.", ex);
        }

        settings ??= new UserSettings();
        // A null list in the file (e.g. "templates": null) reads as null; treat it as empty.
        foreach (var setting in UserSetting.All)
            _ = setting.GetValues(settings);
        return settings;
    }

    /// <summary>Writes the settings, creating the folder if needed. The file is replaced in one step.</summary>
    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        System.IO.Directory.CreateDirectory(Directory);

        var tempPath = Path.Combine(Directory, $"{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = File.Create(tempPath))
                JsonSerializer.Serialize(stream, settings, JsonOptions);
            File.Move(tempPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>Serializes settings the same way they're saved, for display.</summary>
    public static string ToJson(UserSettings settings) => JsonSerializer.Serialize(settings, JsonOptions);

    /// <summary>Deletes the settings file. Returns false when there was no file.</summary>
    public bool Delete()
    {
        if (!File.Exists(FilePath))
            return false;
        File.Delete(FilePath);
        return true;
    }
}
