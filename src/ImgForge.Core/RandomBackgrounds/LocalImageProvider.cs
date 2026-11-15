using System.Buffers;
using System.IO.Enumeration;

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

    /// <summary>How many different images to try when one can't be resized (e.g. a corrupt or misnamed file).</summary>
    internal const int MaxAttempts = 3;

    /// <summary>The default <see cref="ReadTimeout"/>.</summary>
    internal static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Recursive, skipping folders and files that can't be read, hidden or system entries, and absurdly deep trees.
    /// Symbolic links (and junctions) to folders aren't followed (see <see cref="ShouldRecurseInto"/>), so a link loop
    /// can't repeat images or make the search run forever. Online-only cloud files and folders (Dropbox, OneDrive,
    /// iCloud) aren't hidden or system entries, so they're searched; the search reads only metadata (names, sizes,
    /// attributes), so it doesn't download them.
    /// </summary>
    internal static readonly EnumerationOptions SearchOptions = new()
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

    /// <summary>
    /// How long reading the chosen file may take. The file is read once before it's resized, so an online-only file in a
    /// cloud folder (e.g. Dropbox) is downloaded on demand here, with its own time limit, instead of inside Chromium.
    /// </summary>
    public TimeSpan ReadTimeout
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxReadTimeout);
            field = value;
        }
    } = DefaultReadTimeout;

    /// <summary>The longest allowed <see cref="ReadTimeout"/>.</summary>
    internal static readonly TimeSpan MaxReadTimeout = TimeSpan.FromHours(1);

    /// <summary>Reads the whole file at the given path; replaceable in tests. See <see cref="ReadWholeFileAsync"/>.</summary>
    internal Func<string, CancellationToken, Task> ReadFile { get; init; } = ReadWholeFileAsync;

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

        // A corrupt or misnamed file shouldn't fail the run when other images are there: try a few others.
        RandomBackgroundProviderException? lastFailure = null;
        for (var attempt = 0; attempt < MaxAttempts && images.Count > 0; attempt++)
        {
            var index = _random.Next(images.Count);
            var path = images[index];
            try
            {
                await ReadChosenFileAsync(path, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // e.g. the file went away, or the sync app couldn't download it: try another.
                lastFailure = new RandomBackgroundProviderException(
                    $"'{path}' couldn't be read ({ex.Message.TrimEnd('.', ' ')})");
                images.RemoveAt(index);
                continue;
            }

            try
            {
                return await _resizer.ResizeAsync(path, width, height, cancellationToken);
            }
            catch (RandomBackgroundProviderException ex)
            {
                lastFailure = ex;
                images.RemoveAt(index);
            }
        }

        throw lastFailure!;
    }

    /// <summary>
    /// Reads <paramref name="path"/> within <see cref="ReadTimeout"/>, so an online-only file is downloaded now.
    /// A timeout fails the provider (without trying other images, which would likely be just as slow).
    /// </summary>
    private async Task ReadChosenFileAsync(string path, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);
        try
        {
            // WaitAsync: a read blocked on a download may not notice cancellation (e.g. on macOS), so stop waiting for it.
            await ReadFile(path, timeout.Token).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RandomBackgroundProviderException(
                $"'{path}' could not be read within {HttpImageDownloader.FormatSeconds(ReadTimeout)}; if it's an " +
                "online-only file in a cloud folder (e.g. Dropbox), it may still be downloading: make the folder " +
                "available offline, or try again on a faster connection");
        }
    }

    /// <summary>
    /// Reads the file from start to end (at most <see cref="HttpImageDownloader.MaxImageBytes"/>) and discards the
    /// data. For an online-only cloud file, this is what downloads it.
    /// </summary>
    internal static async Task ReadWholeFileAsync(string path, CancellationToken cancellationToken)
    {
        const int chunkSize = 81920;
        var buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 0, FileOptions.Asynchronous | FileOptions.SequentialScan);
            long total = 0;
            int read;
            while (total < HttpImageDownloader.MaxImageBytes &&
                (read = await stream.ReadAsync(buffer.AsMemory(0, chunkSize), cancellationToken)) > 0)
            {
                total += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Every supported, non-empty image of at most <see cref="HttpImageDownloader.MaxImageBytes"/> under
    /// <paramref name="directories"/> (recursively), without duplicates, in a stable order. Folders that don't exist or
    /// can't be read are skipped.
    /// </summary>
    internal static List<string> FindImages(IEnumerable<string> directories, CancellationToken cancellationToken = default)
    {
        // Exact match only: the same folder listed twice (or a folder and its parent) yields identical paths, and
        // case-insensitive matching would wrongly drop files on case-sensitive volumes.
        var seen = new HashSet<string>(StringComparer.Ordinal);
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
                files = Enumerate(root.FullName);
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
                    if (!IsUsableSize(file))
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

    /// <summary>Files under <paramref name="directory"/>, recursively, without following links to folders.</summary>
    private static FileSystemEnumerable<FileInfo> Enumerate(string directory) =>
        new(directory, (ref FileSystemEntry entry) => (FileInfo)entry.ToFileSystemInfo(), SearchOptions)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory,
            ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                (entry.Attributes & FileAttributes.ReparsePoint) == 0 || ShouldRecurseInto(entry.Attributes, entry.ToFullPath()),
        };

    /// <summary>
    /// True for a folder that should be searched: any folder except a link to another folder (a symbolic link,
    /// junction, or WSL symlink). Windows cloud folders (Dropbox, OneDrive online-only placeholders) can be reparse
    /// points that aren't links, so they're searched. Only metadata is read, so nothing is downloaded. A reparse point
    /// whose kind can't be read isn't searched.
    /// </summary>
    internal static bool ShouldRecurseInto(FileAttributes attributes, string fullPath)
    {
        if ((attributes & FileAttributes.ReparsePoint) == 0)
            return true;
        if (OperatingSystem.IsWindows())
            return WindowsReparsePoints.GetReparseTag(fullPath) is { } tag && !WindowsReparsePoints.IsNameSurrogate(tag);
        try
        {
            // On Linux and macOS a reparse point is a symbolic link.
            return new DirectoryInfo(fullPath).LinkTarget is null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when the file (or, for a link, the file it points to) exists, isn't empty, and is at most
    /// <see cref="HttpImageDownloader.MaxImageBytes"/>. Broken links are skipped.
    /// </summary>
    private static bool IsUsableSize(FileInfo file)
    {
        try
        {
            // Only reparse points can be links; skipping the check for other files avoids opening each one. (On Windows,
            // online-only cloud files are reparse points, so each costs one metadata-only open.) Length comes from the
            // folder listing, and online-only cloud files report their real size, so nothing is downloaded.
            var isLink = (file.Attributes & FileAttributes.ReparsePoint) != 0 && file.LinkTarget is not null;
            var target = isLink ? file.ResolveLinkTarget(returnFinalTarget: true) as FileInfo : file;
            return target is { Exists: true, Length: > 0 and <= HttpImageDownloader.MaxImageBytes };
        }
        catch (IOException)
        {
            return false; // e.g. a link loop
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
