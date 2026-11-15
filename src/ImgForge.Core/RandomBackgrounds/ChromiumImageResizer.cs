using System.Net;
using PuppeteerSharp;

namespace ImgForge.Core.RandomBackgrounds;

/// <summary>Crops and scales a local image file to an exact size, on this machine.</summary>
internal interface IImageResizer
{
    /// <summary>
    /// Scales <paramref name="path"/> to cover <paramref name="width"/> × <paramref name="height"/> (keeping its aspect
    /// ratio) and crops the overflow evenly from both sides, like CSS <c>object-fit: cover</c>.
    /// </summary>
    /// <exception cref="RandomBackgroundProviderException">The file couldn't be read or decoded as an image.</exception>
    Task<RandomBackgroundImage> ResizeAsync(string path, int width, int height, CancellationToken cancellationToken);
}

/// <summary>
/// Resizes images with the headless Chromium ImgForge already uses to render templates, so no imaging library (with
/// its native dependencies) is needed. Output is a JPEG for JPEG sources and a PNG otherwise (keeping transparency).
/// </summary>
internal sealed class ChromiumImageResizer : IImageResizer
{
    /// <summary>JPEG quality (0-100) used for JPEG output.</summary>
    internal const int JpegQuality = 90;

    /// <summary>Where the page that draws the image is written (the system temp folder by default).</summary>
    internal string TempDirectory { get; init; } = Path.GetTempPath();

    /// <summary>How long the image has to load and decode.</summary>
    internal TimeSpan LoadTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public async Task<RandomBackgroundImage> ResizeAsync(string path, int width, int height, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new RandomBackgroundProviderException($"'{fullPath}' no longer exists");

        var jpeg = IsJpeg(fullPath);
        var html = BuildPage(new Uri(fullPath).AbsoluteUri, width, height);

        await ImageGenerator.EnsureBrowserAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var pagePath = Path.Combine(TempDirectory, $"imgforge-resize-{Guid.NewGuid():N}.html");
        try
        {
            await File.WriteAllTextAsync(pagePath, html, cancellationToken);

            await using var browser = await Puppeteer.LaunchAsync(ImageGenerator.CreateLaunchOptions());
            cancellationToken.ThrowIfCancellationRequested();
            await using var page = await browser.NewPageAsync();
            await page.SetViewportAsync(new ViewPortOptions { Width = width, Height = height });

            try
            {
                await page.GoToAsync(new Uri(pagePath).AbsoluteUri, new NavigationOptions
                {
                    WaitUntil = [WaitUntilNavigation.Load],
                    Timeout = (int)LoadTimeout.TotalMilliseconds
                });
            }
            catch (NavigationException ex) when (ex.InnerException is TimeoutException
                || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                throw new RandomBackgroundProviderException(
                    $"'{fullPath}' did not load within {HttpImageDownloader.FormatSeconds(LoadTimeout)}");
            }
            cancellationToken.ThrowIfCancellationRequested();

            // The page waits for the image (or its error) before "load" fires; a broken image has no natural size.
            var decoded = await page.EvaluateExpressionAsync<bool>(
                "(() => { const i = document.getElementById('bg'); return i.complete && i.naturalWidth > 0; })()");
            if (!decoded)
                throw new RandomBackgroundProviderException($"'{fullPath}' couldn't be read as an image");

            var bytes = await page.ScreenshotDataAsync(jpeg
                ? new ScreenshotOptions { Type = ScreenshotType.Jpeg, Quality = JpegQuality }
                : new ScreenshotOptions { Type = ScreenshotType.Png, OmitBackground = true });
            cancellationToken.ThrowIfCancellationRequested();

            return new RandomBackgroundImage(bytes, jpeg ? "image/jpeg" : "image/png");
        }
        finally
        {
            RandomBackgroundResolver.TryDelete(pagePath);
        }
    }

    internal static bool IsJpeg(string path) =>
        Path.GetExtension(path) is { } ext &&
        (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase));

    /// <summary>A page that draws the image at <c>file:///</c> URI <paramref name="imageUri"/> covering the whole viewport.</summary>
    internal static string BuildPage(string imageUri, int width, int height) =>
        "<!doctype html><html><head><meta charset=\"utf-8\"><style>" +
        "html,body{margin:0;padding:0;background:transparent;overflow:hidden}" +
        $"img{{display:block;width:{width}px;height:{height}px;object-fit:cover;object-position:center}}" +
        "</style></head><body>" +
        $"<img id=\"bg\" alt=\"\" src=\"{WebUtility.HtmlEncode(imageUri)}\">" +
        "</body></html>";
}
