using PuppeteerSharp;

namespace ImgForge.Core;

public class ImageGenerator(TemplateRenderer renderer)
{
    public async Task<string> GenerateAsync(GenerateOptions opts)
    {
        var html = renderer.Render(opts);

        await EnsureBrowserAsync();

        await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        await page.SetViewportAsync(new ViewPortOptions { Width = opts.Width, Height = opts.Height });

        // Write the rendered HTML to a temp file and navigate to it via file:///
        // so Chromium can load local file:/// resources (images, etc.).
        // SetContentAsync gives the page an about:blank origin which blocks file:/// URLs.
        var tempHtml = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        try
        {
            await File.WriteAllTextAsync(tempHtml, html);
            var fileUri = "file:///" + tempHtml.Replace('\\', '/');
            await page.GoToAsync(fileUri, new NavigationOptions { WaitUntil = [WaitUntilNavigation.Networkidle0] });
            await page.ScreenshotAsync(opts.Out, new ScreenshotOptions { FullPage = false });
        }
        finally
        {
            if (File.Exists(tempHtml)) File.Delete(tempHtml);
        }

        return opts.Out;
    }

    private static async Task EnsureBrowserAsync()
    {
        var fetcher = new BrowserFetcher();
        if (!fetcher.GetInstalledBrowsers().Any())
        {
            Console.Error.WriteLine("Downloading Chromium (first run only)...");
            await fetcher.DownloadAsync();
        }
    }
}
