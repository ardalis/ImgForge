using System.IO.Compression;
using System.Net;
using ImgForge.Core.RandomBackgrounds;

namespace ImgForge.Tests;

public class DropboxProviderTests : IDisposable
{
    private const string LinkA = "https://www.dropbox.com/scl/fo/aaa/bbb?rlkey=secretA&dl=0";
    private const string LinkB = "https://www.dropbox.com/scl/fo/ccc/ddd?rlkey=secretB";

    private readonly string _root = Directory.CreateTempSubdirectory("imgforge-dropbox-").FullName;

    private string CacheDir => Path.Combine(_root, "cache");
    private string TempDir => Directory.CreateDirectory(Path.Combine(_root, "temp")).FullName;

    [Theory]
    [InlineData("https://www.dropbox.com/scl/fo/aaa/bbb?rlkey=k&dl=0", "https://www.dropbox.com/scl/fo/aaa/bbb?rlkey=k&dl=1")]
    [InlineData("https://www.dropbox.com/scl/fo/aaa/bbb?rlkey=k", "https://www.dropbox.com/scl/fo/aaa/bbb?rlkey=k&dl=1")]
    [InlineData("http://dropbox.com/sh/xyz/abc?DL=0&raw=1#frag", "https://dropbox.com/sh/xyz/abc?dl=1")]
    [InlineData(" https://WWW.DROPBOX.COM/scl/fo/a ", "https://www.dropbox.com/scl/fo/a?dl=1")]
    public void DownloadUrl_ForcesHttpsAndDl1AndKeepsOtherParameters(string link, string expected) =>
        Assert.Equal(expected, DropboxProvider.DownloadUrl(link)!.AbsoluteUri);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://www.dropbox.com/scl/fo/a")]
    [InlineData("https://example.com/scl/fo/a")]
    [InlineData("https://dropbox.com.evil.example/scl/fo/a")]
    [InlineData("https://evil.example/?u=https://www.dropbox.com/scl/fo/a")]
    [InlineData("https://user:pass@www.dropbox.com/scl/fo/a")]
    [InlineData("https://www.dropbox.com:8443/scl/fo/a")]
    [InlineData("https://www.dropbox.com/")]
    public void DownloadUrl_NotADropboxLink_ReturnsNull(string? link) =>
        Assert.Null(DropboxProvider.DownloadUrl(link));

    [Fact]
    public async Task FetchAsync_PicksFromEveryImageInEveryFolderRecursively()
    {
        var handler = Serve(new()
        {
            [LinkA] = Zip(("one.jpg", 1), ("nested/deeper/two.PNG", 2), ("notes.txt", 3), ("vector.svg", 4)),
            [LinkB] = Zip(("x/three.webp", 5), ("four.avif", 6), ("five.gif", 7), ("six.jpeg", 8)),
        });
        var resizer = new RecordingResizer();
        var provider = Provider([LinkA, LinkB], handler, resizer, new Random(42));

        for (var i = 0; i < 200; i++)
            await provider.FetchAsync(100, 100, CancellationToken.None);

        Assert.Equal([1, 2, 5, 6, 7, 8], resizer.Calls.Select(c => (int)c.Content[0]).Distinct().Order());
        // Each folder was downloaded once, then served from the cache.
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchAsync_SendsHttpsDl1RequestWithoutImageOnlyAcceptHeader()
    {
        var handler = Serve(new() { [LinkA] = Zip(("a.jpg", 1)) });

        await Provider([LinkA], handler).FetchAsync(100, 100, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://www.dropbox.com/scl/fo/aaa/bbb?rlkey=secretA&dl=1", request.RequestUri!.AbsoluteUri);
        Assert.Equal("*/*", request.Headers.Accept.ToString());
    }

    [Fact]
    public async Task FetchAsync_ExtractsChosenImageWithItsExtensionAndResizesToRequestedSize_ThenDeletesIt()
    {
        var handler = Serve(new() { [LinkA] = Zip(("photos/beach.PNG", 9)) });
        var resizer = new RecordingResizer();

        var image = await Provider([LinkA], handler, resizer).FetchAsync(1280, 720, CancellationToken.None);

        var call = Assert.Single(resizer.Calls);
        Assert.Equal((1280, 720), (call.Width, call.Height));
        Assert.Equal(EntryContent(9), call.Content);
        Assert.Equal(".png", Path.GetExtension(call.Path));
        Assert.Equal(TempDir, Path.GetDirectoryName(call.Path));
        Assert.False(File.Exists(call.Path));
        Assert.Empty(Directory.GetFiles(TempDir));
        Assert.Equal(RecordingResizer.Output, image.Content);
        Assert.Equal(DropboxProvider.ProviderName, new DropboxProvider([]).Name);
    }

    [Fact]
    public void FindImages_SkipsHiddenMacMetadataAndEmptyEntries()
    {
        var zip = Zip(("ok.jpg", 1), (".hidden.jpg", 2), ("__MACOSX/ok.jpg", 3), ("photos/._ok.jpg", 4),
            (".git/inner.jpg", 5), ("empty.jpg", -1));
        File.WriteAllBytes(Path.Combine(_root, "folder.zip"), zip);

        Assert.Equal(["ok.jpg"], DropboxProvider.FindImages(Path.Combine(_root, "folder.zip")));
    }

    [Fact]
    public async Task FetchAsync_WithinCacheLifetime_DoesNotDownloadAgain()
    {
        var handler = Serve(new() { [LinkA] = Zip(("a.jpg", 1)) });

        await Provider([LinkA], handler).FetchAsync(10, 10, CancellationToken.None);
        await Provider([LinkA], handler).FetchAsync(10, 10, CancellationToken.None);

        Assert.Single(handler.Requests);
        var cached = Assert.Single(Directory.GetFiles(CacheDir));
        Assert.DoesNotContain("secretA", cached);
    }

    [Fact]
    public async Task FetchAsync_CacheExpired_DownloadsAgainAndUsesNewContent()
    {
        var current = Zip(("a.jpg", 1));
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(ZipResponse(current)));
        await Provider([LinkA], handler).FetchAsync(10, 10, CancellationToken.None);
        Age(Assert.Single(Directory.GetFiles(CacheDir)), TimeSpan.FromHours(25));
        current = Zip(("b.jpg", 2));
        var resizer = new RecordingResizer();

        await Provider([LinkA], handler, resizer).FetchAsync(10, 10, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(EntryContent(2), Assert.Single(resizer.Calls).Content);
    }

    [Fact]
    public async Task FetchAsync_RefreshFails_UsesOlderCachedCopy()
    {
        await Provider([LinkA], Serve(new() { [LinkA] = Zip(("a.jpg", 1)) })).FetchAsync(10, 10, CancellationToken.None);
        Age(Assert.Single(Directory.GetFiles(CacheDir)), TimeSpan.FromDays(3));
        var offline = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("No such host is known."));
        var resizer = new RecordingResizer();

        await Provider([LinkA], offline, resizer).FetchAsync(10, 10, CancellationToken.None);

        Assert.Equal(EntryContent(1), Assert.Single(resizer.Calls).Content);
    }

    [Fact]
    public async Task FetchAsync_RefreshTooSlow_UsesOlderCachedCopy()
    {
        await Provider([LinkA], Serve(new() { [LinkA] = Zip(("a.jpg", 1)) })).FetchAsync(10, 10, CancellationToken.None);
        Age(Assert.Single(Directory.GetFiles(CacheDir)), TimeSpan.FromDays(3));
        var hanging = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        var provider = new DropboxProvider([LinkA], new HttpClient(hanging), new RecordingResizer())
        {
            CacheDirectory = CacheDir,
            TempDirectory = TempDir,
            ResponseTimeout = TimeSpan.FromMilliseconds(50),
        };

        var image = await provider.FetchAsync(10, 10, CancellationToken.None);

        Assert.Equal(RecordingResizer.Output, image.Content);
    }

    [Fact]
    public async Task FetchAsync_RefreshFails_BacksOffInsteadOfRetryingEveryRun()
    {
        await Provider([LinkA], Serve(new() { [LinkA] = Zip(("a.jpg", 1)) })).FetchAsync(10, 10, CancellationToken.None);
        Age(Assert.Single(Directory.GetFiles(CacheDir)), TimeSpan.FromDays(3));
        var offline = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("No such host is known."));

        await Provider([LinkA], offline).FetchAsync(10, 10, CancellationToken.None);
        await Provider([LinkA], offline).FetchAsync(10, 10, CancellationToken.None);

        Assert.Single(offline.Requests);
    }

    [Fact]
    public async Task FetchAsync_CacheFolderNotWritable_ThrowsProviderExceptionSoFallbackContinues()
    {
        // A file where the cache folder should be: creating the folder (and the download) fails.
        File.WriteAllText(CacheDir, "in the way");
        var handler = Serve(new() { [LinkA] = Zip(("a.jpg", 1)) });

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => Provider([LinkA], handler).FetchAsync(10, 10, CancellationToken.None));

        Assert.StartsWith($"the Dropbox folder: the download couldn't be saved in '{CacheDir}'", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_FileLink_ThrowsWithoutDownloading()
    {
        var handler = Serve(new());

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => Provider(["https://www.dropbox.com/scl/fi/abc/photo.jpg?rlkey=k"], handler).FetchAsync(10, 10, CancellationToken.None));

        Assert.Contains("the link is to a single file, not a folder", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FetchAsync_RedirectedToAnotherSite_ThrowsAndCachesNothing()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = ZipResponse(Zip(("a.jpg", 1)));
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example/folder.zip");
            return Task.FromResult(response);
        });

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => Provider([LinkA], handler).FetchAsync(10, 10, CancellationToken.None));

        Assert.Equal("the Dropbox folder: the link redirected to a site other than Dropbox", ex.Message);
        Assert.False(Directory.Exists(CacheDir));
    }

    [Fact]
    public async Task FetchAsync_RedirectedToDropboxDownloadServer_IsAccepted()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = ZipResponse(Zip(("a.jpg", 1)));
            response.RequestMessage = new HttpRequestMessage(
                HttpMethod.Get, "https://uc1234.dl.dropboxusercontent.com/zip_download_get/abc");
            return Task.FromResult(response);
        });

        var image = await Provider([LinkA], handler).FetchAsync(10, 10, CancellationToken.None);

        Assert.Equal(RecordingResizer.Output, image.Content);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "the shared link wasn't found (HTTP 404); check that the folder is still shared")]
    [InlineData(HttpStatusCode.Forbidden, "access was denied (HTTP 403); check that anyone with the link can view the folder")]
    [InlineData(HttpStatusCode.TooManyRequests, "too many requests (HTTP 429); try again later")]
    [InlineData(HttpStatusCode.BadGateway, "the site returned HTTP 502")]
    public async Task FetchAsync_HttpError_ThrowsReasonWithoutTheLink(HttpStatusCode status, string reason)
    {
        var handler = FakeHttpMessageHandler.Returning(() => new HttpResponseMessage(status));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => Provider([LinkA], handler).FetchAsync(10, 10, CancellationToken.None));

        Assert.Equal($"the Dropbox folder: {reason}", ex.Message);
        Assert.DoesNotContain("secretA", ex.Message);
        Assert.False(Directory.Exists(CacheDir) && Directory.GetFiles(CacheDir).Length > 0);
    }

    [Fact]
    public async Task FetchAsync_WebPageInsteadOfZip_ThrowsNotAFolderDownload()
    {
        var handler = FakeHttpMessageHandler.Returning(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>sign in</html>", System.Text.Encoding.UTF8, "text/html")
        });

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => Provider([LinkA], handler).FetchAsync(10, 10, CancellationToken.None));

        Assert.StartsWith("the Dropbox folder: the link didn't download a folder; check that it's a Dropbox shared-folder link", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_SingleFileLink_ThrowsShareTheFolderInstead()
    {
        var handler = FakeHttpMessageHandler.Returning(() => FakeHttpMessageHandler.Image([1, 2, 3]));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => Provider([LinkA], handler).FetchAsync(10, 10, CancellationToken.None));

        Assert.Contains("the link is to a single file, not a folder", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_DownloadIsNotAZip_ThrowsAndCachesNothing()
    {
        var handler = FakeHttpMessageHandler.Returning(() => ZipResponse([1, 2, 3, 4, 5]));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => Provider([LinkA], handler).FetchAsync(10, 10, CancellationToken.None));

        Assert.Contains("the link didn't download a folder; check that it's a Dropbox shared-folder link", ex.Message);
        Assert.Empty(Directory.GetFiles(CacheDir));
    }

    [Fact]
    public async Task FetchAsync_DownloadLargerThanLimit_ThrowsAndCachesNothing()
    {
        var handler = Serve(new() { [LinkA] = Zip(("a.jpg", 1), ("b.jpg", 2)) });
        var provider = new DropboxProvider([LinkA], new HttpClient(handler), new RecordingResizer())
        {
            CacheDirectory = CacheDir,
            TempDirectory = TempDir,
            MaxArchiveBytes = 10,
        };

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(10, 10, CancellationToken.None));

        Assert.Contains("the folder download is larger than", ex.Message);
        Assert.False(Directory.Exists(CacheDir) && Directory.GetFiles(CacheDir).Length > 0);
    }

    [Fact]
    public async Task FetchAsync_DownloadWithoutLengthGrowsPastLimit_ThrowsAndLeavesNoPartialFile()
    {
        var zip = Zip(("a.jpg", 1), ("b.jpg", 2));
        // A stream with no Content-Length, so the limit is enforced while reading.
        var handler = FakeHttpMessageHandler.Returning(() =>
        {
            var content = new StreamContent(new NonSeekableStream(zip));
            content.Headers.ContentType = new("application/zip");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var provider = new DropboxProvider([LinkA], new HttpClient(handler), new RecordingResizer())
        {
            CacheDirectory = CacheDir,
            TempDirectory = TempDir,
            MaxArchiveBytes = 10,
        };

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(10, 10, CancellationToken.None));

        Assert.Contains("the folder download is larger than", ex.Message);
        Assert.Empty(Directory.GetFiles(CacheDir));
    }

    [Fact]
    public async Task FetchAsync_OneFolderFails_UsesTheOthers()
    {
        var handler = Serve(new() { [LinkB] = Zip(("b.jpg", 2)) }); // LinkA → 404
        var resizer = new RecordingResizer();

        await Provider([LinkA, "https://example.com/not-dropbox", LinkB], handler, resizer)
            .FetchAsync(10, 10, CancellationToken.None);

        Assert.Equal(EntryContent(2), Assert.Single(resizer.Calls).Content);
    }

    [Fact]
    public async Task FetchAsync_EveryFolderFails_NamesEachByPositionWithoutLinks()
    {
        var handler = FakeHttpMessageHandler.Returning(() => new HttpResponseMessage(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => Provider([LinkA, "https://example.com/x?key=secretC"], handler).FetchAsync(10, 10, CancellationToken.None));

        Assert.Equal(
            "Dropbox folder 1: the shared link wasn't found (HTTP 404); check that the folder is still shared; " +
            "Dropbox folder 2: the link isn't a Dropbox shared-folder link (https://www.dropbox.com/...)",
            ex.Message);
        Assert.DoesNotContain("secret", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_NoImagesInFolders_ThrowsWithHint()
    {
        var handler = Serve(new() { [LinkA] = Zip(("readme.txt", 1)) });

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => Provider([LinkA], handler).FetchAsync(10, 10, CancellationToken.None));

        Assert.StartsWith("no supported images (.jpg, .jpeg, .png, .webp, .gif, .avif) were found in the Dropbox folder;", ex.Message);
        Assert.EndsWith(DropboxProvider.AddFolderHint, ex.Message);
    }

    [Fact]
    public async Task FetchAsync_NoFoldersConfigured_ThrowsWithHowToAddOne()
    {
        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => new DropboxProvider([" "]).FetchAsync(10, 10, CancellationToken.None));

        Assert.Equal($"no Dropbox folders are configured; {DropboxProvider.AddFolderHint}", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_ChosenImageCannotBeResized_TriesAnotherAndNamesTheEntryNotTheTempFile()
    {
        var handler = Serve(new() { [LinkA] = Zip(("bad1.jpg", 1), ("bad2.jpg", 2), ("bad3.jpg", 3), ("bad4.jpg", 4)) });
        var resizer = new RecordingResizer { Fails = _ => true };

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => Provider([LinkA], handler, resizer).FetchAsync(10, 10, CancellationToken.None));

        Assert.Equal(DropboxProvider.MaxAttempts, resizer.Calls.Select(c => c.Path).Distinct().Count());
        Assert.Matches(@"^'bad\d\.jpg' in the Dropbox folder couldn't be read as an image$", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_DamagedCachedCopy_IsDeletedSoItIsDownloadedAgain()
    {
        var handler = Serve(new() { [LinkA] = Zip(("a.jpg", 1)) });
        await Provider([LinkA], handler).FetchAsync(10, 10, CancellationToken.None);
        var cached = Assert.Single(Directory.GetFiles(CacheDir));
        File.WriteAllBytes(cached, [1, 2, 3]);

        await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => Provider([LinkA], handler).FetchAsync(10, 10, CancellationToken.None));
        await Provider([LinkA], handler).FetchAsync(10, 10, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void DefaultTimeLimit_IsLongerThanTheResolversUsualLimit() =>
        Assert.True(((IRandomBackgroundProvider)new DropboxProvider([])).TimeLimit > new RandomBackgroundResolver(
            new PlaceholderProvider()).DownloadTimeout);

    private DropboxProvider Provider(
        IEnumerable<string> folders, FakeHttpMessageHandler handler, RecordingResizer? resizer = null, Random? random = null) =>
        new(folders, new HttpClient(handler), resizer ?? new RecordingResizer(), random)
        {
            CacheDirectory = CacheDir,
            TempDirectory = TempDir,
        };

    /// <summary>Answers each link's dl=1 URL with its zip, and anything else with HTTP 404.</summary>
    private static FakeHttpMessageHandler Serve(Dictionary<string, byte[]> zips)
    {
        var byUrl = zips.ToDictionary(z => DropboxProvider.DownloadUrl(z.Key)!.AbsoluteUri, z => z.Value);
        return new FakeHttpMessageHandler((request, _) => Task.FromResult(
            byUrl.TryGetValue(request.RequestUri!.AbsoluteUri, out var zip)
                ? ZipResponse(zip)
                : new HttpResponseMessage(HttpStatusCode.NotFound)));
    }

    private static HttpResponseMessage ZipResponse(byte[] zip)
    {
        var content = new ByteArrayContent(zip);
        content.Headers.ContentType = new("application/zip");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>A zip of the given entries; each entry's content starts with its marker byte (-1 = empty entry).</summary>
    private static byte[] Zip(params (string Name, int Marker)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, marker) in entries)
            {
                using var stream = archive.CreateEntry(name).Open();
                if (marker >= 0)
                    stream.Write(EntryContent(marker));
            }
        }
        return buffer.ToArray();
    }

    private static byte[] EntryContent(int marker) => [(byte)marker, 0xAB, 0xCD];

    private static void Age(string path, TimeSpan age) => File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>A read-only stream with no known length, like a response body sent without Content-Length.</summary>
    private sealed class NonSeekableStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class RecordingResizer : IImageResizer
    {
        public static readonly byte[] Output = [0xFF, 0xD8, 0xFF, 0xE0, 7];

        public List<(string Path, byte[] Content, int Width, int Height)> Calls { get; } = [];

        public Func<string, bool> Fails { get; init; } = _ => false;

        public Task<RandomBackgroundImage> ResizeAsync(string path, int width, int height, CancellationToken cancellationToken)
        {
            Calls.Add((path, File.ReadAllBytes(path), width, height));
            if (Fails(path))
                throw new RandomBackgroundProviderException($"'{Path.GetFullPath(path)}' couldn't be read as an image");
            return Task.FromResult(new RandomBackgroundImage(Output, "image/jpeg"));
        }
    }
}
