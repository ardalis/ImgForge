using Microsoft.Playwright;

namespace ImgForge.Core;

public class ImageGenerator(TemplateRenderer renderer)
{
    public async Task<string> GenerateAsync(GenerateOptions opts)
    {
        var html = renderer.Render(opts);
        using var playwright = await CreatePlaywrightAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await page.SetViewportSizeAsync(opts.Width, opts.Height);

        // Write the rendered HTML to a temp file and navigate to it via file:///
        // so Chromium can load local file:/// resources (images, etc.).
        // SetContentAsync gives the page an about:blank origin which blocks file:/// URLs.
        var tempHtml = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        try
        {
            await File.WriteAllTextAsync(tempHtml, html);
            var fileUri = "file:///" + tempHtml.Replace('\\', '/');
            await page.GotoAsync(fileUri, new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.ScreenshotAsync(new() { Path = opts.Out, FullPage = false });
        }
        finally
        {
            if (File.Exists(tempHtml)) File.Delete(tempHtml);
        }

        return opts.Out;
    }

    private static async Task<IPlaywright> CreatePlaywrightAsync()
    {
        try
        {
            return await Playwright.CreateAsync();
        }
        catch (Exception ex) when (ex.Message.Contains("Driver not found") || ex.Message.Contains("node.exe"))
        {
            Console.Error.WriteLine("Playwright driver not found. Running 'playwright install chromium'...");
            var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
            if (exitCode != 0)
                throw new InvalidOperationException($"Playwright install failed with exit code {exitCode}.");
            return await Playwright.CreateAsync();
        }
    }
}
