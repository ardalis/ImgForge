using Microsoft.Playwright;

namespace ImgForge.Core;

public class ImageGenerator(TemplateRenderer renderer)
{
    public async Task<string> GenerateAsync(GenerateOptions opts)
    {
        var html = renderer.Render(opts);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await page.SetViewportSizeAsync(opts.Width, opts.Height);
        await page.SetContentAsync(html, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.ScreenshotAsync(new() { Path = opts.Out, FullPage = false });
        return opts.Out;
    }
}
