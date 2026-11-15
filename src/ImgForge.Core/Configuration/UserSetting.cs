namespace ImgForge.Core.Configuration;

/// <summary>
/// One list-valued setting in <see cref="UserSettings"/>: its command-line name, how to reach its values, and how values
/// are checked and normalized before they're stored.
/// </summary>
public sealed class UserSetting
{
    private readonly Func<UserSettings, List<string>> _values;
    private readonly Action<UserSettings, List<string>> _setValues;
    private readonly Func<string, string> _normalize;

    private UserSetting(
        string name,
        string description,
        bool isPath,
        Func<UserSettings, List<string>> values,
        Action<UserSettings, List<string>> setValues,
        Func<string, string> normalize)
    {
        Name = name;
        Description = description;
        IsPath = isPath;
        _values = values;
        _setValues = setValues;
        _normalize = normalize;
    }

    public static UserSetting ImageDirectories { get; } = new(
        "image-directories",
        "local folders to pick images from",
        isPath: true,
        s => s.ImageDirectories,
        (s, v) => s.ImageDirectories = v,
        NormalizePath);

    public static UserSetting DropboxFolders { get; } = new(
        "dropbox-folders",
        "Dropbox shared-folder URLs to pick images from",
        isPath: false,
        s => s.DropboxFolders,
        (s, v) => s.DropboxFolders = v,
        NormalizeHttpUrl);

    public static UserSetting Templates { get; } = new(
        "templates",
        "template files or folders",
        isPath: true,
        s => s.Templates,
        (s, v) => s.Templates = v,
        NormalizePath);

    /// <summary>Every setting, in the order shown to users.</summary>
    public static IReadOnlyList<UserSetting> All { get; } = [ImageDirectories, DropboxFolders, Templates];

    /// <summary>The command-line name, e.g. <c>image-directories</c>.</summary>
    public string Name { get; }

    public string Description { get; }

    /// <summary>True when values are file system paths (stored as full paths).</summary>
    public bool IsPath { get; }

    /// <summary>Finds a setting by its command-line name.</summary>
    /// <exception cref="ArgumentException">No setting has that name.</exception>
    public static UserSetting Find(string name) =>
        All.FirstOrDefault(s => s.Name == name)
        ?? throw new ArgumentException(
            $"Unknown setting \"{name}\". Valid settings: {string.Join(", ", All.Select(s => s.Name))}.");

    /// <summary>The setting's values in <paramref name="settings"/> (never null; a missing list is created).</summary>
    public List<string> GetValues(UserSettings settings)
    {
        var values = _values(settings);
        if (values is null)
        {
            values = [];
            _setValues(settings, values);
        }
        return values;
    }

    /// <summary>Checks a value and puts it in its stored form (full path, trimmed URL).</summary>
    /// <exception cref="ArgumentException">The value isn't valid for this setting.</exception>
    public string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"A value for {Name} can't be empty.");
        return _normalize(value.Trim());
    }

    /// <summary>
    /// True when <paramref name="value"/> (as typed by the user) refers to the stored value <paramref name="stored"/>:
    /// the two match as typed, or after normalizing both. Values edited into the file by hand (e.g. <c>~/x</c>, a
    /// trailing slash, or a value that's no longer valid) can still be matched.
    /// </summary>
    public bool Matches(string stored, string value)
    {
        value = value.Trim();
        if (Comparer.Equals(stored.Trim(), value))
            return true;
        var normalized = TryNormalize(value);
        return normalized is not null && Comparer.Equals(TryNormalize(stored) ?? stored, normalized);
    }

    private string? TryNormalize(string value)
    {
        try
        {
            return Normalize(value);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Compares stored values: paths case-insensitively on Windows and macOS, everything else exactly.</summary>
    public StringComparer Comparer =>
        IsPath && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static string NormalizePath(string path)
    {
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length > 2 ? path[2..] : "");

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException($"\"{path}\" isn't a valid path: {ex.Message}", ex);
        }
    }

    private static string NormalizeHttpUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException($"\"{url}\" isn't an HTTP(S) URL. Use a Dropbox shared-folder link such as https://www.dropbox.com/scl/fo/....");
        if (RandomBackgrounds.DropboxProvider.DownloadUrl(url) is null)
            throw new ArgumentException($"\"{url}\" isn't a Dropbox link. Use a Dropbox shared-folder link such as https://www.dropbox.com/scl/fo/....");
        return url;
    }
}
