using ImgForge.Core;
using ImgForge.Core.RandomBackgrounds;

namespace ImgForge.Tests;

public class LocalImageProviderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("imgforge-local-").FullName;

    [Fact]
    public void FindImages_SearchesEveryFolderRecursivelyForSupportedExtensions()
    {
        var a = Folder("a");
        var b = Folder("b");
        var expected = new[]
        {
            Image(a, "one.jpg"),
            Image(a, "nested/deeper/two.PNG"),
            Image(a, "three.jpeg"),
            Image(b, "four.webp"),
            Image(b, "x/five.gif"),
            Image(b, "six.avif"),
        };
        Image(a, "notes.txt");
        Image(b, "vector.svg");
        Image(b, "x/raw.heic");

        var found = LocalImageProvider.FindImages([a, b]);

        Assert.Equal(expected.Order(StringComparer.Ordinal), found);
    }

    [Fact]
    public void FindImages_SkipsMissingFoldersEmptyFilesAndDuplicates()
    {
        var a = Folder("a");
        var image = Image(a, "sub/one.jpg");
        File.WriteAllBytes(Path.Combine(a, "empty.jpg"), []);

        // The same folder twice, a parent that contains it, and one that doesn't exist.
        var found = LocalImageProvider.FindImages([a, a, _root, Path.Combine(_root, "missing")]);

        Assert.Equal([image], found);
    }

    [Fact]
    public async Task FetchAsync_ResizesARandomlyChosenImageToTheRequestedSize()
    {
        var a = Folder("a");
        var b = Folder("b");
        var images = new[] { Image(a, "1.jpg"), Image(a, "deep/2.png"), Image(b, "3.webp") };
        var resizer = new RecordingResizer();
        var provider = new LocalImageProvider([a, b], resizer, new Random(1234));

        var image = await provider.FetchAsync(1280, 720, CancellationToken.None);

        var call = Assert.Single(resizer.Calls);
        Assert.Contains(call.Path, images);
        Assert.Equal((1280, 720), (call.Width, call.Height));
        Assert.Equal(RecordingResizer.Output, image.Content);
        Assert.Equal("image/jpeg", image.MediaType);
        Assert.Equal(LocalImageProvider.ProviderName, provider.Name);
    }

    [Fact]
    public async Task FetchAsync_OverManyCalls_PicksFromEveryImageInEveryFolder()
    {
        var a = Folder("a");
        var b = Folder("b");
        var images = new[] { Image(a, "1.jpg"), Image(a, "x/2.jpg"), Image(b, "3.jpg"), Image(b, "y/z/4.jpg") };
        var resizer = new RecordingResizer();
        var provider = new LocalImageProvider([a, b], resizer, new Random(42));

        for (var i = 0; i < 200; i++)
            await provider.FetchAsync(100, 100, CancellationToken.None);

        Assert.Equal(images.Order(StringComparer.Ordinal), resizer.Calls.Select(c => c.Path).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void FindImages_DoesNotFollowFolderLinks_SoALoopDoesNotRepeatImages()
    {
        var a = Folder("a");
        var image = Image(a, "sub/one.jpg");
        if (!TryCreateLink(() => Directory.CreateSymbolicLink(Path.Combine(a, "sub", "loop"), a)))
            return; // Creating symbolic links needs extra rights on some Windows machines.

        Assert.Equal([image], LocalImageProvider.FindImages([a]));
    }

    [Fact]
    public void FindImages_UsesLinksToImageFilesButSkipsBrokenOnes()
    {
        var a = Folder("a");
        var real = Image(Folder("elsewhere"), "real.jpg");
        var link = Path.Combine(a, "linked.jpg");
        var broken = Path.Combine(a, "broken.jpg");
        if (!TryCreateLink(() => File.CreateSymbolicLink(link, real)) ||
            !TryCreateLink(() => File.CreateSymbolicLink(broken, Path.Combine(_root, "gone.jpg"))))
            return;

        Assert.Equal([link], LocalImageProvider.FindImages([a]));
    }

    [Fact]
    public async Task FetchAsync_ChosenImageCannotBeResized_TriesAnother()
    {
        var a = Folder("a");
        Image(a, "bad1.jpg");
        Image(a, "bad2.jpg");
        var good = Image(a, "good.jpg");
        var resizer = new RecordingResizer { Fails = path => path != good };
        var provider = new LocalImageProvider([a], resizer, new Random(7));

        var image = await provider.FetchAsync(100, 100, CancellationToken.None);

        Assert.Equal(RecordingResizer.Output, image.Content);
        Assert.Equal(good, resizer.Calls[^1].Path);
        Assert.Equal(resizer.Calls.Count, resizer.Calls.Select(c => c.Path).Distinct().Count());
    }

    [Fact]
    public async Task FetchAsync_EveryAttemptFails_ThrowsLastReasonAfterMaxAttempts()
    {
        var a = Folder("a");
        for (var i = 0; i < 5; i++)
            Image(a, $"bad{i}.jpg");
        var resizer = new RecordingResizer { Fails = _ => true };
        var provider = new LocalImageProvider([a], resizer);

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.EndsWith("couldn't be read as an image", ex.Message);
        Assert.Equal(LocalImageProvider.MaxAttempts, resizer.Calls.Count);
    }

    [Fact]
    public async Task FetchAsync_NoFoldersConfigured_ThrowsWithHowToAddOne()
    {
        var provider = new LocalImageProvider([], new RecordingResizer());

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.Equal("no image folders are configured; add one with: imgforge config add image-directories <folder>", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_NoImagesInFolders_ThrowsNamingFoldersAndFormats()
    {
        var a = Folder("a");
        Image(a, "readme.md");
        var missing = Path.Combine(_root, "missing");
        var resizer = new RecordingResizer();
        var provider = new LocalImageProvider([a, missing], resizer);

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.Equal(
            "no supported images (.jpg, .jpeg, .png, .webp, .gif, .avif) were found in " +
            $"'{a}', '{missing}'; add images there, or add one with: imgforge config add image-directories <folder>",
            ex.Message);
        Assert.Empty(resizer.Calls);
    }

    [Fact]
    public async Task ResolverWithLocalThenFallback_NoLocalImages_FallsBackWithWarning()
    {
        var provider = new LocalImageProvider([Folder("empty")], new RecordingResizer());
        var fallback = new StubRandomBackgroundProvider((_, _, _) =>
            Task.FromResult(new RandomBackgroundImage([1, 2, 3], "image/png")), "placehold.co");
        var resolver = new RandomBackgroundResolver(provider, fallback) { TempDirectory = _root };

        var resolved = await resolver.ResolveToFileAsync(100, 100);

        Assert.Equal("placehold.co", resolved.ProviderName);
        Assert.Equal(LocalImageProvider.ProviderName, Assert.Single(resolved.Failures).ProviderName);
    }

    [Fact]
    public async Task Resolver_LocalImage_SavesResizedImageToTempFile()
    {
        Image(Folder("a"), "photo.jpg");
        var resolver = new RandomBackgroundResolver(new LocalImageProvider([_root], new RecordingResizer()))
        {
            TempDirectory = Folder("out")
        };

        var resolved = await resolver.ResolveToFileAsync(640, 480);

        Assert.Equal(LocalImageProvider.ProviderName, resolved.ProviderName);
        Assert.EndsWith(".jpg", resolved.Path);
        Assert.Equal(RecordingResizer.Output, await File.ReadAllBytesAsync(resolved.Path));
    }

    [Fact]
    [Trait("Category", "Integration")] // GenerateAsync makes sure Chromium is installed before resizing local images.
    public async Task GenerateAsync_RandomLocalWithNoImages_FailsWithoutRenderingOrNetwork()
    {
        var template = Path.Combine(_root, "template.html");
        File.WriteAllText(template, "<html><body></body></html>");
        var generator = new ImageGenerator(new TemplateRenderer()) { ImageDirectories = [Folder("empty")] };
        var opts = new GenerateOptions(template, "Title", null, "random:local", [], Path.Combine(_root, "out.png"), 100, 100);

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => generator.GenerateAsync(opts));

        Assert.StartsWith(
            "Could not load a random background image from local image folders (used by --bg random:local): no supported images",
            ex.Message);
        Assert.False(File.Exists(opts.Out));
    }

    [Fact]
    public void ShouldRecurseInto_PlainFolder_IsSearched()
    {
        var a = Folder("a");

        Assert.True(LocalImageProvider.ShouldRecurseInto(FileAttributes.Directory, a));
    }

    [Fact]
    public void ShouldRecurseInto_ReparsePointFolderThatIsNotALink_IsSearched()
    {
        // Windows cloud placeholder folders (Dropbox, OneDrive online-only) are reparse points without a link target.
        var a = Folder("cloud");

        Assert.True(LocalImageProvider.ShouldRecurseInto(FileAttributes.Directory | FileAttributes.ReparsePoint, a));
    }

    [Fact]
    public void ShouldRecurseInto_LinkToFolder_IsNotFollowed()
    {
        var target = Folder("target");
        var link = Path.Combine(_root, "link");
        if (!TryCreateLink(() => Directory.CreateSymbolicLink(link, target)))
            return;

        Assert.False(LocalImageProvider.ShouldRecurseInto(new DirectoryInfo(link).Attributes, link));
    }

    [Theory]
    [InlineData(FileAttributes.Offline)]
    [InlineData((FileAttributes)0x00400000)] // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS (online-only cloud file)
    [InlineData((FileAttributes)0x00040000)] // FILE_ATTRIBUTE_RECALL_ON_OPEN
    [InlineData((FileAttributes)0x00100000)] // FILE_ATTRIBUTE_UNPINNED ("free up space")
    [InlineData(FileAttributes.SparseFile)]
    [InlineData(FileAttributes.ReparsePoint)]
    public void SearchOptions_DoNotSkipOnlineOnlyCloudFileAttributes(FileAttributes attribute)
    {
        Assert.Equal(0, (int)(LocalImageProvider.SearchOptions.AttributesToSkip & attribute));
    }

    [Fact]
    public async Task FetchAsync_ReadsOnlyTheChosenFileBeforeResizingIt()
    {
        var a = Folder("a");
        for (var i = 0; i < 10; i++)
            Image(a, $"{i}.jpg");
        var events = new List<string>();
        var resizer = new RecordingResizer { OnCall = path => events.Add($"resize {path}") };
        var provider = new LocalImageProvider([a], resizer, new Random(3))
        {
            ReadFile = (path, _) =>
            {
                events.Add($"read {path}");
                return Task.CompletedTask;
            }
        };

        await provider.FetchAsync(100, 100, CancellationToken.None);

        var chosen = Assert.Single(resizer.Calls).Path;
        Assert.Equal([$"read {chosen}", $"resize {chosen}"], events);
    }

    [Fact]
    public async Task FetchAsync_ReadTimesOut_FailsWithOnlineOnlyHintWithoutResizingOrRetrying()
    {
        var a = Folder("a");
        Image(a, "1.jpg");
        Image(a, "2.jpg");
        var reads = 0;
        var resizer = new RecordingResizer();
        var provider = new LocalImageProvider([a], resizer)
        {
            ReadTimeout = TimeSpan.FromMilliseconds(100),
            // Ignores cancellation, like a read blocked on a download: the provider must stop waiting anyway.
            ReadFile = (_, _) =>
            {
                Interlocked.Increment(ref reads);
                return Task.Delay(Timeout.Infinite, CancellationToken.None);
            }
        };

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.Contains("could not be read within 0.1 seconds", ex.Message);
        Assert.Contains("online-only file", ex.Message);
        Assert.Equal(1, reads);
        Assert.Empty(resizer.Calls);
    }

    [Fact]
    public async Task FetchAsync_CallerCancelsDuringRead_ThrowsOperationCanceled()
    {
        var a = Folder("a");
        Image(a, "1.jpg");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var provider = new LocalImageProvider([a], new RecordingResizer())
        {
            ReadFile = (_, token) => Task.Delay(Timeout.Infinite, token)
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.FetchAsync(100, 100, cts.Token));
    }

    [Fact]
    public async Task FetchAsync_ChosenFileCannotBeRead_TriesAnother()
    {
        var a = Folder("a");
        var bad = Image(a, "bad.jpg");
        var good = Image(a, "good.jpg");
        var reads = new List<string>();
        var resizer = new RecordingResizer();
        var provider = new LocalImageProvider([a], resizer, new FixedRandom(0))
        {
            ReadFile = (path, _) =>
            {
                reads.Add(path);
                return path == bad
                    ? Task.FromException(new IOException("The cloud operation was unsuccessful."))
                    : Task.CompletedTask;
            }
        };

        await provider.FetchAsync(100, 100, CancellationToken.None);

        Assert.Equal([bad, good], reads); // "bad.jpg" sorts first, so index 0 picks it, then the only one left.
        Assert.Equal(good, Assert.Single(resizer.Calls).Path);
    }

    [Fact]
    public async Task FetchAsync_NoFileCanBeRead_ThrowsReadReason()
    {
        var a = Folder("a");
        Image(a, "1.jpg");
        var provider = new LocalImageProvider([a], new RecordingResizer())
        {
            ReadFile = (_, _) => Task.FromException(new IOException("The cloud operation was unsuccessful."))
        };

        var ex = await Assert.ThrowsAsync<RandomBackgroundProviderException>(
            () => provider.FetchAsync(100, 100, CancellationToken.None));

        Assert.EndsWith("couldn't be read (The cloud operation was unsuccessful)", ex.Message);
    }

    [Fact]
    public async Task ReadWholeFileAsync_ReadsARealFile()
    {
        var path = Path.Combine(Folder("a"), "photo.jpg");
        File.WriteAllBytes(path, new byte[200_000]);

        await LocalImageProvider.ReadWholeFileAsync(path, CancellationToken.None);
    }

    [Fact]
    public async Task ReadWholeFileAsync_MissingFile_ThrowsIOException()
    {
        await Assert.ThrowsAnyAsync<IOException>(
            () => LocalImageProvider.ReadWholeFileAsync(Path.Combine(_root, "gone.jpg"), CancellationToken.None));
    }

    [Fact]
    public async Task Resolver_SlowReadWithinReadTimeout_IsNotCutOffByDownloadTimeout()
    {
        Image(Folder("a"), "photo.jpg");
        var provider = new LocalImageProvider([_root], new RecordingResizer())
        {
            ReadTimeout = TimeSpan.FromSeconds(10),
            ReadFile = (_, token) => Task.Delay(TimeSpan.FromMilliseconds(500), token) // a slow on-demand download
        };
        var resolver = new RandomBackgroundResolver(provider)
        {
            DownloadTimeout = TimeSpan.FromMilliseconds(100),
            TempDirectory = Folder("out")
        };

        var resolved = await resolver.ResolveToFileAsync(100, 100);

        Assert.Equal(LocalImageProvider.ProviderName, resolved.ProviderName);
    }

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private static string Image(string folder, string relativePath)
    {
        var path = Path.Combine(folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1]);
        return path;
    }

    private static bool TryCreateLink(Action create)
    {
        try
        {
            create();
            return true;
        }
        catch (Exception ex) when (OperatingSystem.IsWindows() && ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>Always returns <c>value</c> (clamped to the range).</summary>
    private sealed class FixedRandom(int value) : Random
    {
        public override int Next(int maxValue) => Math.Min(value, maxValue - 1);
    }

    private sealed class RecordingResizer : IImageResizer
    {
        public static readonly byte[] Output = [0xFF, 0xD8, 0xFF, 0xE0, 7];

        public List<(string Path, int Width, int Height)> Calls { get; } = [];

        public Func<string, bool> Fails { get; init; } = _ => false;

        public Action<string> OnCall { get; init; } = _ => { };

        public Task<RandomBackgroundImage> ResizeAsync(string path, int width, int height, CancellationToken cancellationToken)
        {
            Calls.Add((path, width, height));
            OnCall(path);
            if (Fails(path))
                throw new RandomBackgroundProviderException($"'{path}' couldn't be read as an image");
            return Task.FromResult(new RandomBackgroundImage(Output, "image/jpeg"));
        }
    }
}
