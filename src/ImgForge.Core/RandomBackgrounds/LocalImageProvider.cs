namespace ImgForge.Core.RandomBackgrounds;

/// <summary>
/// Random images from local folders (the <c>image-directories</c> setting). Every supported image in every folder,
/// searched recursively, is equally likely to be picked. The chosen image is cropped and scaled to the requested size
/// on this machine by an <see cref="IImageResizer"/>.
/// </summary>
internal sealed class LocalImageProvider : IRandomBackgroundProvider
{
    /// <summary>The provider's <see cref="Name"/>.</summary>
    public const string ProviderName = "local image folders";

    /// <summary>How to add a folder, shown when none is configured or none has images.</summary>
    internal const string AddFolderHint = "add one with: imgforge config add image-directories <folder>";

    /// <summary>File extensions of the raster formats Chromium can draw (the same formats remote providers may return).</summary>
    internal static IReadOnlyList<string> SupportedExtensions { get; } = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif"];

    private static readonly HashSet<string> SupportedExtensionSet = new(SupportedExtensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Recursive, skipping folders and files that can't be read, hidden or system entries, and absurdly deep trees.
    /// Symbolic links to folders aren't followed, so a link loop can't make the search run forever.
    /// </summary>
    private static readonly EnumerationOptions SearchOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        MaxRecursionDepth = 64,
        ReturnSpecialDirectories = false,
    };

    private readonly IReadOnlyList<string> _directories;
    private readonly IImageResizer _resizer;
    private readonly Random _random;

    /// <param name="directories">The folders to search (full paths). May be empty; fetching then fails with a hint.</param>
    /// <param name="resizer">Crops and scales the chosen image; defaults to <see cref="ChromiumImageResizer"/>.</param>
    /// <param name="random">The random source; defaults to <see cref="Random.Shared"/>.</param>
    public LocalImageProvider(IEnumerable<string> directories, IImageResizer? resizer = null, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(directories);
        _directories = directories.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).ToArray();
        _resizer = resizer ?? new ChromiumImageResizer();
        _random = random ?? Random.Shared;
    }

    public string Name => ProviderName;

    /// <summary>The folders searched, in order.</summary>
    internal IReadOnlyList<string> Directories => _directories;

    public async Task<RandomBackgroundImage> FetchAsync(int width, int height, CancellationToken cancellationToken)
    {
        if (_directories.Count == 0)
            throw new RandomBackgroundProviderException($"no image folders are configured; {AddFolderHint}");

        var images = FindImages(_directories, cancellationToken);
        if (images.Count == 0)
        {
            throw new RandomBackgroundProviderException(
                $"no supported images ({string.Join(", ", SupportedExtensions)}) were found in " +
                $"{string.Join(", ", _directories.Select(d => $"'{d}'"))}; add images there, or {AddFolderHint}");
        }

        var path = images[_random.Next(images.Count)];
        return await _resizer.ResizeAsync(path, width, height, cancellationToken);
    }

    /// <summary>
    /// Every supported, non-empty image of at most <see cref="HttpImageDownloader.MaxImageBytes"/> under
    /// <paramref name="directories"/> (recursively), without duplicates, in a stable order. Folders that don't exist or
    /// can't be read are skipped.
    /// </summary>
    internal static List<string> FindImages(IEnumerable<string> directories, CancellationToken cancellationToken = default)
    {
        var seen = new HashSet<string>(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var images = new List<string>();

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<FileInfo> files;
            try
            {
                var root = new DirectoryInfo(directory);
                if (!root.Exists)
                    continue;
                files = root.EnumerateFiles("*", SearchOptions);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
                continue;
            }

            try
            {
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!SupportedExtensionSet.Contains(file.Extension))
                        continue;
                    if (file.Length == 0 || file.Length > HttpImageDownloader.MaxImageBytes)
                        continue;
                    if (seen.Add(file.FullName))
                        images.Add(file.FullName);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // The folder went away or became unreadable mid-search; keep what was found so far.
            }
        }

        images.Sort(StringComparer.Ordinal);
        return images;
    }
}
