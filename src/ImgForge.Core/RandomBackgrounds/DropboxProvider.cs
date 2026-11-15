using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ImgForge.Core.Configuration;

namespace ImgForge.Core.RandomBackgrounds;

/// <summary>
/// Random images from Dropbox shared folders (the <c>dropbox-folders</c> setting), with no sign-in or API key. Every
/// supported image in every folder, including subfolders, is equally likely to be picked. The chosen image is cropped
/// and scaled to the requested size on this machine by an <see cref="IImageResizer"/>.
/// </summary>
/// <remarks>
/// A Dropbox shared-folder link with <c>dl=1</c> downloads the whole folder as a zip. Each folder's zip is cached in
/// <see cref="CacheDirectory"/> for <see cref="CacheLifetime"/>, so most runs don't download anything; if a refresh fails
/// and an older copy is cached, the older copy is used. Only the chosen image is extracted, to a temp file ImgForge
/// names itself, so entry names in the zip can't write anywhere else. Shared links can carry access keys
/// (<c>rlkey=...</c>), so messages never include a link: folders are named by their position in the setting.
/// </remarks>
internal sealed class DropboxProvider : IRandomBackgroundProvider
{
    /// <summary>The provider's <see cref="Name"/>.</summary>
    public const string ProviderName = "Dropbox folders";

    /// <summary>How to add a folder, shown when none is configured or none has images.</summary>
    internal const string AddFolderHint = "add one with: imgforge config add dropbox-folders <shared folder link>";

    /// <summary>How many different images to try when one can't be extracted or resized.</summary>
    internal const int MaxAttempts = 3;

    /// <summary>Hosts a shared-folder link may use.</summary>
    private static readonly string[] Hosts = ["www.dropbox.com", "dropbox.com"];

    private static readonly HashSet<string> SupportedExtensionSet =
        new(LocalImageProvider.SupportedExtensions, StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyList<string> _folders;
    private readonly HttpClient _httpClient;
    private readonly IImageResizer _resizer;
    private readonly Random _random;

    /// <param name="folders">The shared-folder links, in order. May be empty; fetching then fails with a hint.</param>
    /// <param name="httpClient">The client to use; defaults to <see cref="HttpImageDownloader.SharedClient"/>.</param>
    /// <param name="resizer">Crops and scales the chosen image; defaults to <see cref="ChromiumImageResizer"/>.</param>
    /// <param name="random">The random source; defaults to <see cref="Random.Shared"/>.</param>
    public DropboxProvider(
        IEnumerable<string> folders, HttpClient? httpClient = null, IImageResizer? resizer = null, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(folders);
        _folders = folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToArray();
        _httpClient = httpClient ?? HttpImageDownloader.SharedClient;
        _resizer = resizer ?? new ChromiumImageResizer();
        _random = random ?? Random.Shared;
    }

    public string Name => ProviderName;

    /// <summary>The shared-folder links, in order.</summary>
    internal IReadOnlyList<string> Folders => _folders;

    /// <summary>
    /// The first download of a big folder can take a while, so this provider gets longer than the resolver's usual
    /// per-provider limit. A site that doesn't respond still fails within <see cref="ResponseTimeout"/>.
    /// </summary>
    public TimeSpan? TimeLimit { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>How long Dropbox has to start responding.</summary>
    public TimeSpan ResponseTimeout { get; init; } = HttpImageDownloader.DefaultResponseTimeout;

    /// <summary>
    /// When an older copy of a folder is cached, how long a refresh may take before the older copy is used instead.
    /// </summary>
    public TimeSpan RefreshTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a downloaded folder is used before it is downloaded again.</summary>
    public TimeSpan CacheLifetime { get; init; } = TimeSpan.FromHours(24);

    /// <summary>The largest folder download (zip) accepted.</summary>
    public long MaxArchiveBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Where downloaded folders are kept (see <see cref="DefaultCacheDirectory"/>).</summary>
    public string CacheDirectory { get; init; } = DefaultCacheDirectory();

    /// <summary>Where the chosen image is extracted before it is resized (the system temp folder by default).</summary>
    internal string TempDirectory { get; init; } = Path.GetTempPath();

    /// <summary>
    /// <c>cache/dropbox</c> in the settings folder (<see cref="UserSettingsStore.GetDefaultDirectory"/>), or in the
    /// system temp folder when there's no user profile.
    /// </summary>
    public static string DefaultCacheDirectory()
    {
        string root;
        try
        {
            root = UserSettingsStore.GetDefaultDirectory();
        }
        catch (InvalidOperationException)
        {
            root = Path.Combine(Path.GetTempPath(), "imgforge");
        }
        return Path.Combine(root, "cache", "dropbox");
    }

    /// <summary>
    /// The URL that downloads <paramref name="link"/> as a zip: the same https Dropbox link with <c>dl=1</c> (other
    /// query parameters such as <c>rlkey</c> are kept). <c>null</c> when it isn't a dropbox.com link.
    /// </summary>
    internal static Uri? DownloadUrl(string? link)
    {
        if (!Uri.TryCreate(link?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !Hosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase)) ||
            uri.UserInfo.Length > 0 || !uri.IsDefaultPort || uri.AbsolutePath.Length <= 1)
            return null;

        var parameters = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Split('=', 2)[0].ToLowerInvariant() is not ("dl" or "raw"))
            .Append("dl=1");
        return new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = -1,
            Query = string.Join('&', parameters),
            Fragment = ""
        }.Uri;
    }

    /// <summary>The cache file for a download URL: a hash of it, so the link (and its key) isn't in the file name.</summary>
    internal string CachePath(Uri downloadUrl) =>
        Path.Combine(CacheDirectory,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(downloadUrl.AbsoluteUri))) + ".zip");

    public async Task<RandomBackgroundImage> FetchAsync(int width, int height, CancellationToken cancellationToken)
    {
        if (_folders.Count == 0)
            throw new RandomBackgroundProviderException($"no Dropbox folders are configured; {AddFolderHint}");

        var failures = new List<string>();
        var images = new List<(string Archive, string Entry, string Folder)>();
        for (var i = 0; i < _folders.Count; i++)
        {
            var folder = FolderLabel(i);
            try
            {
                var url = DownloadUrl(_folders[i]) ?? throw new RandomBackgroundProviderException(
                    "the link isn't a Dropbox shared-folder link (https://www.dropbox.com/...)");
                var archive = await GetArchiveAsync(url, cancellationToken);
                images.AddRange(FindImages(archive).Select(entry => (archive, entry, folder)));
            }
            catch (RandomBackgroundProviderException ex)
            {
                failures.Add(Describe(folder, ex.Message));
            }
            catch (HttpRequestException ex)
            {
                failures.Add(Describe(folder,
                    $"the site could not be reached ({ex.GetBaseException().Message.TrimEnd('.', ' ')})"));
            }
        }

        if (images.Count == 0)
        {
            if (failures.Count == _folders.Count)
                throw new RandomBackgroundProviderException(string.Join("; ", failures));
            throw new RandomBackgroundProviderException(
                $"no supported images ({string.Join(", ", LocalImageProvider.SupportedExtensions)}) were found in " +
                (_folders.Count == 1 ? "the Dropbox folder" : "your Dropbox folders") +
                (failures.Count > 0 ? $" ({string.Join("; ", failures)})" : "") +
                $"; add images there, or {AddFolderHint}");
        }

        // A corrupt or misnamed file shouldn't fail the run when other images are there: try a few others.
        RandomBackgroundProviderException? lastFailure = null;
        for (var attempt = 0; attempt < MaxAttempts && images.Count > 0; attempt++)
        {
            var index = _random.Next(images.Count);
            var (archive, entry, folder) = images[index];
            try
            {
                return await ExtractAndResizeAsync(archive, entry, folder, width, height, cancellationToken);
            }
            catch (RandomBackgroundProviderException ex)
            {
                lastFailure = ex;
                images.RemoveAt(index);
            }
        }

        throw lastFailure!;
    }

    private string FolderLabel(int index) => _folders.Count == 1 ? "the Dropbox folder" : $"Dropbox folder {index + 1}";

    private static string Describe(string folder, string reason) => $"{folder}: {reason}";

    /// <summary>The cached zip for <paramref name="url"/>, downloading it first when missing or older than <see cref="CacheLifetime"/>.</summary>
    private async Task<string> GetArchiveAsync(Uri url, CancellationToken cancellationToken)
    {
        var cachePath = CachePath(url);
        var cached = new FileInfo(cachePath);
        if (!cached.Exists)
        {
            await DownloadAsync(url, cachePath, cancellationToken);
            return cachePath;
        }

        if (DateTime.UtcNow - cached.LastWriteTimeUtc < CacheLifetime)
            return cachePath;

        // An older copy is there: refresh it, but fall back to it (e.g. offline) rather than fail.
        using var refresh = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        refresh.CancelAfter(RefreshTimeout);
        try
        {
            await DownloadAsync(url, cachePath, refresh.Token);
        }
        catch (Exception ex) when (ex is RandomBackgroundProviderException or HttpRequestException ||
            (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Keep using the older copy.
        }
        return cachePath;
    }

    /// <summary>Downloads the folder's zip to <paramref name="cachePath"/>, replacing it only once the download is complete and valid.</summary>
    private async Task DownloadAsync(Uri url, string cachePath, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // Replace the shared client's default "Accept: image/*": the folder comes back as a zip.
        request.Headers.Accept.ParseAdd("*/*");
        using var response = await ApiProviderHelpers.SendAsync(_httpClient, request, ResponseTimeout, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new RandomBackgroundProviderException(DescribeFailure(response.StatusCode));

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null && mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new RandomBackgroundProviderException(
                "the link is to a single file, not a folder; share the folder and add that link instead");
        if (mediaType is not null && mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            throw new RandomBackgroundProviderException(NotAFolderDownload);
        if (response.Content.Headers.ContentLength > MaxArchiveBytes)
            throw new RandomBackgroundProviderException(TooLarge);

        try
        {
            Directory.CreateDirectory(CacheDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RandomBackgroundProviderException(
                $"the download couldn't be saved in '{CacheDirectory}' ({ex.Message.TrimEnd('.', ' ')})");
        }

        var tempPath = Path.Combine(CacheDirectory, $"download-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                try
                {
                    await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await CopyBoundedAsync(body, file, MaxArchiveBytes, TooLarge, cancellationToken);
                }
                catch (IOException ex) when (ex is not FileNotFoundException)
                {
                    throw new RandomBackgroundProviderException(
                        $"the connection was lost while downloading the folder ({ex.GetBaseException().Message.TrimEnd('.', ' ')})");
                }
            }

            EnsureZip(tempPath);
            try
            {
                File.Move(tempPath, cachePath, overwrite: true);
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && File.Exists(cachePath))
            {
                // e.g. another ImgForge run has the cached copy open (Windows); that copy is just as good.
            }
        }
        finally
        {
            RandomBackgroundResolver.TryDelete(tempPath);
        }
    }

    internal static string DescribeFailure(HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound or HttpStatusCode.Gone =>
            $"the shared link wasn't found (HTTP {(int)status}); check that the folder is still shared",
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            $"access was denied (HTTP {(int)status}); check that anyone with the link can view the folder",
        HttpStatusCode.TooManyRequests => "too many requests (HTTP 429); try again later",
        _ => $"the site returned HTTP {(int)status}"
    };

    private const string NotAFolderDownload =
        "the link didn't download a folder; check that it's a Dropbox shared-folder link";

    private string TooLarge => $"the folder download is larger than {MaxArchiveBytes / (1024 * 1024)} MB";

    private static void EnsureZip(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            _ = archive.Entries.Count;
        }
        catch (InvalidDataException)
        {
            throw new RandomBackgroundProviderException(NotAFolderDownload);
        }
    }

    /// <summary>
    /// The full names of every supported, non-empty image of at most <see cref="HttpImageDownloader.MaxImageBytes"/> in the
    /// zip at <paramref name="archivePath"/> (any folder depth), in a stable order. Hidden entries (names starting with
    /// <c>.</c>, including macOS <c>._*</c> files) and <c>__MACOSX</c> metadata are skipped.
    /// </summary>
    internal static List<string> FindImages(string archivePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var images = archive.Entries
                .Where(IsImage)
                .Select(e => e.FullName)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            images.Sort(StringComparer.Ordinal);
            return images;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // Probably damaged: download it again next time.
            RandomBackgroundResolver.TryDelete(archivePath);
            throw new RandomBackgroundProviderException(
                $"the downloaded copy couldn't be read ({ex.Message.TrimEnd('.', ' ')})");
        }
    }

    private static bool IsImage(ZipArchiveEntry entry)
    {
        var segments = entry.FullName.Replace('\\', '/').Split('/');
        return segments[^1].Length > 0 &&
            !segments.Any(s => s.StartsWith('.') || s.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)) &&
            SupportedExtensionSet.Contains(Path.GetExtension(segments[^1])) &&
            entry.Length is > 0 and <= HttpImageDownloader.MaxImageBytes;
    }

    private async Task<RandomBackgroundImage> ExtractAndResizeAsync(
        string archivePath, string entryName, string folder, int width, int height, CancellationToken cancellationToken)
    {
        var description = $"'{entryName}' in {folder}";
        // ImgForge picks the file name; only the (checked) extension comes from the zip.
        var tempPath = Path.GetFullPath(Path.Combine(TempDirectory,
            $"imgforge-dropbox-{Guid.NewGuid():N}{Path.GetExtension(entryName).ToLowerInvariant()}"));
        try
        {
            try
            {
                using var archive = ZipFile.OpenRead(archivePath);
                var entry = archive.GetEntry(entryName)
                    ?? throw new RandomBackgroundProviderException($"{description} is no longer in the downloaded copy");
                await using var source = entry.Open();
                await using var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await CopyBoundedAsync(source, target, HttpImageDownloader.MaxImageBytes,
                    $"{description} is larger than {HttpImageDownloader.MaxImageBytes / (1024 * 1024)} MB", cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                throw new RandomBackgroundProviderException(
                    $"{description} couldn't be extracted ({ex.Message.TrimEnd('.', ' ')})");
            }

            try
            {
                return await _resizer.ResizeAsync(tempPath, width, height, cancellationToken);
            }
            catch (RandomBackgroundProviderException ex)
            {
                // Name the image in the folder, not the temp file.
                throw new RandomBackgroundProviderException(ex.Message.Replace($"'{tempPath}'", description));
            }
        }
        finally
        {
            RandomBackgroundResolver.TryDelete(tempPath);
        }
    }

    private static async Task CopyBoundedAsync(
        Stream source, Stream target, long maxBytes, string tooLargeReason, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new RandomBackgroundProviderException(tooLargeReason);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
