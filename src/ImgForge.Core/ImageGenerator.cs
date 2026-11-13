using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using ImgForge.Core.RandomBackgrounds;
using PuppeteerSharp;
using PuppeteerSharp.BrowserData;

namespace ImgForge.Core;

public class ImageGenerator(TemplateRenderer renderer)
{
    /// <summary>
    /// How long to wait for the rendered template and all of its resources to load.
    /// </summary>
    internal TimeSpan LoadTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Where warnings about failed template resources are written (stderr by default).
    /// </summary>
    internal TextWriter Warnings { get; set; } = Console.Error;

    /// <summary>
    /// Downloads the image for <c>--bg random</c> (the default provider order) before rendering. Its
    /// <see cref="RandomBackgroundResolver.DownloadTimeout"/> and <see cref="RandomBackgroundResolver.TempDirectory"/>
    /// also apply to <c>--bg random:&lt;provider&gt;</c>.
    /// </summary>
    internal RandomBackgroundResolver RandomBackgrounds { get; set; } = new();

    public async Task<string> GenerateAsync(GenerateOptions opts)
    {
        // Download a random background up front (with a short timeout) and hand the template a local file,
        // so a slow or failing image service fails fast instead of using up the page-load timeout.
        string? randomBackgroundFile = null;
        try
        {
            if (TemplateRenderer.IsRandomBackground(opts.Background))
            {
                var spec = RandomBackgroundSpec.Parse(opts.Background!);
                // Report a bad --template before spending time on (or failing) a download.
                TemplateRenderer.EnsureTemplateExists(opts.Template);
                var resolved = await ResolverFor(spec).ResolveToFileAsync(opts.Width, opts.Height);
                randomBackgroundFile = resolved.Path;
                if (resolved.Failures.Count > 0)
                    await Warnings.WriteLineAsync(RandomBackgroundResolver.BuildFallbackWarning(resolved));
                opts = opts with { Background = randomBackgroundFile };
            }

            return await GenerateResolvedAsync(opts);
        }
        finally
        {
            // Best effort, so a locked temp file can't hide the real error.
            if (randomBackgroundFile is not null)
                RandomBackgroundResolver.TryDelete(randomBackgroundFile);
        }
    }

    /// <summary>
    /// The default resolver for <c>--bg random</c>, or a single-provider resolver (no fallback) for
    /// <c>--bg random:&lt;provider&gt;</c>.
    /// </summary>
    internal RandomBackgroundResolver ResolverFor(RandomBackgroundSpec spec) =>
        spec.CreateProvider() is { } provider
            ? new RandomBackgroundResolver(provider)
            {
                BackgroundOption = spec.OptionText,
                DownloadTimeout = RandomBackgrounds.DownloadTimeout,
                TempDirectory = RandomBackgrounds.TempDirectory
            }
            : RandomBackgrounds;

    private async Task<string> GenerateResolvedAsync(GenerateOptions opts)
    {
        var html = renderer.Render(opts);

        await EnsureBrowserAsync();

        var launchOptions = new LaunchOptions
        {
            Headless = true,
            Args = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? ["--no-sandbox"] : []
        };
        await using var browser = await Puppeteer.LaunchAsync(launchOptions);
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
            // Track in-flight requests so a timeout can name the site(s) that never responded.
            var pending = new ConcurrentDictionary<IRequest, byte>();
            // Track failed requests so a remote image that fails fast (offline, DNS failure, HTTP error)
            // is reported instead of silently producing an image without it.
            var failed = new ConcurrentQueue<FailedRequest>();
            page.Request += (_, e) => pending.TryAdd(e.Request, 0);
            page.RequestFinished += (_, e) =>
            {
                pending.TryRemove(e.Request, out byte _);
                var status = (int?)e.Request.Response?.Status;
                if (status >= 400)
                    failed.Enqueue(new FailedRequest(OriginalUrl(e.Request), $"HTTP {status}"));
            };
            page.RequestFailed += (_, e) =>
            {
                pending.TryRemove(e.Request, out byte _);
                // ERR_ABORTED means the page itself cancelled the request (e.g. an image that was replaced), not a failure.
                // Note: an HTML/JSON error page returned for an image arrives here as ERR_BLOCKED_BY_ORB (Chromium
                // blocks it before PuppeteerSharp sees the response), so its HTTP status isn't available.
                if (!string.Equals(e.Request.FailureText, "net::ERR_ABORTED", StringComparison.Ordinal))
                    failed.Enqueue(new FailedRequest(OriginalUrl(e.Request), e.Request.FailureText ?? "unknown error"));
            };

            try
            {
                await page.GoToAsync(fileUri, new NavigationOptions
                {
                    WaitUntil = [WaitUntilNavigation.Networkidle0],
                    Timeout = (int)LoadTimeout.TotalMilliseconds
                });
            }
            catch (NavigationException ex) when (IsTimeout(ex))
            {
                throw new ImageGenerationTimeoutException(BuildTimeoutMessage(opts, LoadTimeout, GetPendingHosts(pending.Keys)), ex);
            }

            ReportLoadFailures(opts, failed);

            await page.ScreenshotAsync(opts.Out, new ScreenshotOptions { FullPage = false });
        }
        finally
        {
            if (File.Exists(tempHtml)) File.Delete(tempHtml);
        }

        return opts.Out;
    }

    /// <summary>
    /// Throws <see cref="ImageLoadException"/> if a remote image the user asked for failed to load;
    /// writes a warning for each failed remote resource referenced only by the template.
    /// </summary>
    private void ReportLoadFailures(GenerateOptions opts, IEnumerable<FailedRequest> failed)
    {
        var failures = ResourceLoadFailures.Classify(opts, failed);

        foreach (var failure in failures.Where(f => !f.IsRequestedByUser))
            Warnings.WriteLine(ResourceLoadFailures.BuildWarning(failure));

        var requested = failures.Where(f => f.IsRequestedByUser).ToList();
        if (requested.Count > 0)
            throw new ImageLoadException(ResourceLoadFailures.BuildErrorMessage(requested));
    }

    private static string OriginalUrl(IRequest request) =>
        request.RedirectChain?.FirstOrDefault()?.Url ?? request.Url;

    private static bool IsTimeout(NavigationException ex) =>
        ex.InnerException is TimeoutException
        || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the distinct hosts of in-flight HTTP(S) requests. For redirected requests the host of the
    /// original URL is used rather than the CDN it redirects to.
    /// </summary>
    private static List<string> GetPendingHosts(IEnumerable<IRequest> requests) =>
        requests
            .Select(OriginalUrl)
            .Select(GetHttpHost)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? GetHttpHost(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.Host
            : null;

    /// <summary>
    /// Builds a user-facing explanation of a page-load timeout, naming the remote site(s) responsible
    /// and suggesting alternatives.
    /// </summary>
    /// <param name="opts">
    /// The generation options (used to recognise the background source). <c>--bg random</c> has already been
    /// resolved to a local file by the time the page loads, so it is never the cause of a timeout.
    /// </param>
    /// <param name="timeout">The load timeout that elapsed.</param>
    /// <param name="pendingHosts">Hosts of requests still in flight when the timeout elapsed, if known.</param>
    internal static string BuildTimeoutMessage(
        GenerateOptions opts, TimeSpan timeout, IReadOnlyCollection<string>? pendingHosts = null)
    {
        var secondsValue = timeout.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture);
        var seconds = $"{secondsValue} second{(timeout.TotalSeconds == 1 ? "" : "s")}";

        var bgHost = GetHttpHost(opts.Background?.Trim());

        IReadOnlyList<string> hosts = pendingHosts is { Count: > 0 }
            ? pendingHosts.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : bgHost is not null ? [bgHost] : [];

        if (hosts.Count == 0)
        {
            return $"Timed out after {seconds} waiting for the template to finish loading." + Environment.NewLine +
                   "A remote resource referenced by the template (such as an image, font, or stylesheet) " +
                   "may be unreachable or responding slowly." + Environment.NewLine +
                   "Check your network connection and any external URLs in the template, then try again.";
        }

        var onlyBackground = bgHost is not null
            && hosts.All(h => string.Equals(h, bgHost, StringComparison.OrdinalIgnoreCase));

        var what = onlyBackground
            ? $"the background image from {bgHost}"
            : $"remote content from {string.Join(", ", hosts)}";

        var lines = new List<string>
        {
            $"Timed out after {seconds} waiting for {what} to load.",
            hosts.Count == 1
                ? "The site may be unreachable or responding slowly. You can:"
                : "These sites may be unreachable or responding slowly. You can:",
            "  - Try again later."
        };

        if (onlyBackground)
        {
            lines.Add("  - Pass a different background with --bg (a local image file or another URL).");
            lines.Add("  - Omit --bg to use the template's built-in background design.");
        }
        else
        {
            lines.Add("  - Use local files instead of remote URLs for --bg, --headshot, or --overlay, or omit them.");
            lines.Add("  - Check your network connection and any external URLs in the template.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    // Serializes the first-run Chromium download. Concurrent GenerateAsync calls would otherwise all see
    // "no browser installed" and download to the same zip file at once, failing with an IOException.
    private static readonly SemaphoreSlim BrowserDownloadLock = new(1, 1);
    private static volatile bool _browserReady;

    private static async Task EnsureBrowserAsync()
    {
        if (_browserReady) return;

        await BrowserDownloadLock.WaitAsync();
        try
        {
            if (_browserReady) return;

            // Check for the exact Chrome build Puppeteer.LaunchAsync will start. Another browser (e.g. a
            // headless shell) or an older build left by a previous PuppeteerSharp version doesn't count.
            var fetcher = new BrowserFetcher();
            var installed = fetcher.GetInstalledBrowsers().Any(b =>
                b.Browser == SupportedBrowser.Chrome && b.BuildId == Chrome.DefaultBuildId);
            if (!installed)
            {
                Console.Error.WriteLine("Downloading Chromium (first run only)...");
                await fetcher.DownloadAsync();
            }

            _browserReady = true;
        }
        finally
        {
            BrowserDownloadLock.Release();
        }
    }
}
