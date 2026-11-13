using ImgForge.Core;

namespace ImgForge.Tests;

public class ResourceLoadFailuresTests
{
    private static GenerateOptions Options(
        string? background = null,
        string? headshot = null,
        params string[] overlays) => new(
        Template: "blog",
        Title: "Test",
        Subtitle: null,
        Background: background,
        Overlays: overlays.Select(o => new OverlayImage(o)).ToList(),
        Out: "out.png",
        Width: 1200,
        Height: 630,
        Headshot: headshot is null ? null : new HeadshotOptions(headshot));

    // ---- Classify ----

    [Fact]
    public void Classify_RandomBackgroundUrl_IsRandomBackground()
    {
        var failures = ResourceLoadFailures.Classify(
            Options("random"),
            [new FailedRequest("https://picsum.photos/1200/630", "net::ERR_NAME_NOT_RESOLVED")]);

        var f = Assert.Single(failures);
        Assert.Equal(ResourceSource.RandomBackground, f.Source);
        Assert.Equal("picsum.photos", f.Host);
        Assert.True(f.IsRequestedByUser);
    }

    [Fact]
    public void Classify_UrlBackground_MatchesAfterNormalization()
    {
        var failures = ResourceLoadFailures.Classify(
            Options("HTTPS://Images.Example.com:443/a b.jpg"),
            [new FailedRequest("https://images.example.com/a%20b.jpg", "HTTP 404")]);

        Assert.Equal(ResourceSource.Background, Assert.Single(failures).Source);
    }

    [Fact]
    public void Classify_UnicodeHost_MatchesPunycodeReportedByChromium()
    {
        var failures = ResourceLoadFailures.Classify(
            Options("https://bücher.example/cover.jpg"),
            [new FailedRequest("https://xn--bcher-kva.example/cover.jpg", "net::ERR_NAME_NOT_RESOLVED")]);

        Assert.Equal(ResourceSource.Background, Assert.Single(failures).Source);
    }

    [Fact]
    public void Classify_CharactersChromiumEscapes_StillMatch()
    {
        var failures = ResourceLoadFailures.Classify(
            Options("https://images.example.com/img.jpg?name=o'brien"),
            [new FailedRequest("https://images.example.com/img.jpg?name=o%27brien", "HTTP 404")]);

        Assert.Equal(ResourceSource.Background, Assert.Single(failures).Source);
    }

    [Theory]
    [InlineData(" random ")]
    [InlineData("Random")]
    public void Classify_RandomBackgroundVariants_AreRecognized(string bg)
    {
        var failures = ResourceLoadFailures.Classify(
            Options(bg),
            [new FailedRequest("https://picsum.photos/1200/630", "net::ERR_NAME_NOT_RESOLVED")]);

        Assert.Equal(ResourceSource.RandomBackground, Assert.Single(failures).Source);
    }

    [Fact]
    public void Classify_HeadshotAndOverlay_AreAttributed()
    {
        var failures = ResourceLoadFailures.Classify(
            Options(headshot: "https://people.example.com/me.jpg", overlays: ["https://logos.example.com/logo.png"]),
            [
                new FailedRequest("https://people.example.com/me.jpg", "net::ERR_CONNECTION_REFUSED"),
                new FailedRequest("https://logos.example.com/logo.png", "HTTP 500")
            ]);

        Assert.Equal([ResourceSource.Headshot, ResourceSource.Overlay], failures.Select(f => f.Source));
    }

    [Fact]
    public void Classify_UnmatchedUrl_IsTemplateResource()
    {
        var failures = ResourceLoadFailures.Classify(
            Options("random"),
            [new FailedRequest("https://fonts.example.com/font.woff2", "net::ERR_NAME_NOT_RESOLVED")]);

        var f = Assert.Single(failures);
        Assert.Equal(ResourceSource.Template, f.Source);
        Assert.False(f.IsRequestedByUser);
    }

    [Fact]
    public void Classify_IgnoresNonHttpAndDuplicates()
    {
        var failures = ResourceLoadFailures.Classify(
            Options(),
            [
                new FailedRequest("file:///C:/missing.png", "net::ERR_FILE_NOT_FOUND"),
                new FailedRequest("data:image/png;base64,xx", "net::ERR_INVALID_URL"),
                new FailedRequest("https://cdn.example.com/x.png", "HTTP 404"),
                new FailedRequest("https://cdn.example.com/x.png", "HTTP 404")
            ]);

        Assert.Equal("https://cdn.example.com/x.png", Assert.Single(failures).Url);
    }

    // ---- BuildErrorMessage ----

    private static ResourceLoadFailure Failure(ResourceSource source, string reason = "net::ERR_NAME_NOT_RESOLVED",
        string host = "images.example.com") =>
        new(source, $"https://{host}/img.jpg", host, reason);

    [Fact]
    public void ErrorMessage_RandomBackground_NamesPicsumAndSuggestsAlternatives()
    {
        var message = ResourceLoadFailures.BuildErrorMessage(
            [Failure(ResourceSource.RandomBackground, host: "picsum.photos")]);

        var lines = message.Split(Environment.NewLine);
        Assert.Equal(
            "Could not load a random background image from picsum.photos (used by --bg random): " +
            "the site could not be reached (net::ERR_NAME_NOT_RESOLVED).",
            lines[0]);
        Assert.Contains("try again later", message);
        Assert.Contains("Pass a different background with --bg", message);
        Assert.Contains("omit --bg to use the template's built-in background design", message);
        Assert.DoesNotContain("--headshot", message);
    }

    [Fact]
    public void ErrorMessage_HttpStatus_IsDescribed()
    {
        var message = ResourceLoadFailures.BuildErrorMessage([Failure(ResourceSource.Background, "HTTP 404")]);

        Assert.StartsWith(
            "Could not load the --bg background image from images.example.com: the site returned HTTP 404.",
            message);
    }

    [Theory]
    [InlineData("net::ERR_BLOCKED_BY_ORB", "the site responded, but not with an image; it may have returned an error page (net::ERR_BLOCKED_BY_ORB)")]
    [InlineData("net::ERR_CONNECTION_REFUSED", "the site could not be reached (net::ERR_CONNECTION_REFUSED)")]
    [InlineData("net::ERR_INTERNET_DISCONNECTED", "the site could not be reached (net::ERR_INTERNET_DISCONNECTED)")]
    [InlineData("net::ERR_CERT_DATE_INVALID", "the site could not be reached (net::ERR_CERT_DATE_INVALID)")]
    [InlineData("net::ERR_TOO_MANY_REDIRECTS", "the request failed (net::ERR_TOO_MANY_REDIRECTS)")]
    public void ErrorMessage_DescribesReason(string reason, string expected)
    {
        var message = ResourceLoadFailures.BuildErrorMessage([Failure(ResourceSource.Background, reason)]);

        Assert.StartsWith($"Could not load the --bg background image from images.example.com: {expected}.", message);
    }

    [Fact]
    public void ErrorMessage_Headshot_HasHeadshotAdvice()
    {
        var message = ResourceLoadFailures.BuildErrorMessage([Failure(ResourceSource.Headshot)]);

        Assert.Contains("the --headshot image from images.example.com", message);
        Assert.Contains("omit --headshot", message);
        Assert.DoesNotContain("--bg", message);
    }

    [Fact]
    public void ErrorMessage_MultipleFailures_ListsEachAndCombinesAdvice()
    {
        var message = ResourceLoadFailures.BuildErrorMessage(
        [
            Failure(ResourceSource.Background, host: "bg.example.com"),
            Failure(ResourceSource.Overlay, "HTTP 403", host: "logo.example.com")
        ]);

        var lines = message.Split(Environment.NewLine);
        Assert.Equal("Could not load 2 requested images:", lines[0]);
        Assert.Contains("  - the --bg background image from bg.example.com: the site could not be reached (net::ERR_NAME_NOT_RESOLVED).", lines);
        Assert.Contains("  - the --overlay image from logo.example.com: the site returned HTTP 403.", lines);
        Assert.Contains("--bg (a local image file", message);
        Assert.Contains("remove that --overlay", message);
    }

    // ---- BuildWarning ----

    [Fact]
    public void Warning_NamesUrlWithoutCredentialsOrQuery()
    {
        var failure = new ResourceLoadFailure(ResourceSource.Template,
            "https://user:secret@fonts.example.com:8443/font.woff2?token=abc", "fonts.example.com", "HTTP 404");

        var warning = ResourceLoadFailures.BuildWarning(failure);

        Assert.Equal(
            "Warning: Could not load https://fonts.example.com:8443/font.woff2 referenced by the template: " +
            "the site returned HTTP 404. The image was generated without it.",
            warning);
    }
}
