using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImgForge.Core.Configuration;

/// <summary>
/// Per-user ImgForge settings, stored as JSON in the user's profile (see <see cref="UserSettingsStore"/>).
/// </summary>
public sealed class UserSettings
{
    /// <summary>Local folders to pick images from (full paths).</summary>
    public List<string> ImageDirectories { get; set; } = [];

    /// <summary>Dropbox shared-folder HTTP(S) URLs to pick images from.</summary>
    public List<string> DropboxFolders { get; set; } = [];

    /// <summary>Template files or folders (full paths).</summary>
    public List<string> Templates { get; set; } = [];

    /// <summary>JSON properties ImgForge doesn't know about, kept so they aren't lost when the file is rewritten.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
