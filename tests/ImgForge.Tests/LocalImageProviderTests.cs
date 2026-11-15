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

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private static string Image(string folder, string relativePath)
    {
        var path = Path.Combine(folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1]);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class RecordingResizer : IImageResizer
    {
        public static readonly byte[] Output = [0xFF, 0xD8, 0xFF, 0xE0, 7];

        public List<(string Path, int Width, int Height)> Calls { get; } = [];

        public Task<RandomBackgroundImage> ResizeAsync(string path, int width, int height, CancellationToken cancellationToken)
        {
            Calls.Add((path, width, height));
            return Task.FromResult(new RandomBackgroundImage(Output, "image/jpeg"));
        }
    }
}
