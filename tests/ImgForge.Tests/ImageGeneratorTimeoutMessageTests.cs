using ImgForge.Core;

namespace ImgForge.Tests;

public class ImageGeneratorTimeoutMessageTests
{
    private static GenerateOptions Options(string? background) => new(
        Template: "blog",
        Title: "Test",
        Subtitle: null,
        Background: background,
        Overlays: [],
        Out: "out.png");

    [Theory]
    [InlineData("random")]
    [InlineData("RANDOM")]
    public void RandomBackground_NamesPicsumAndSuggestsAlternatives(string bg)
    {
        var message = ImageGenerator.BuildTimeoutMessage(Options(bg), TimeSpan.FromSeconds(30));

        Assert.Contains("picsum.photos", message);
        Assert.Contains("unreachable", message);
        Assert.Contains("Try again later", message);
        Assert.Contains("--bg", message);
        Assert.Contains("Omit --bg", message);
        Assert.DoesNotContain("Navigation", message);
    }

    [Fact]
    public void UrlBackground_NamesHost()
    {
        var message = ImageGenerator.BuildTimeoutMessage(
            Options("https://images.example.com/photos/cover.jpg"), TimeSpan.FromSeconds(30));

        Assert.Contains("images.example.com", message);
        Assert.DoesNotContain("picsum.photos", message);
        Assert.Contains("Try again later", message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("./images/cover.jpg")]
    [InlineData("C:\\images\\cover.jpg")]
    public void NoOrLocalBackground_ReturnsGenericTemplateMessage(string? bg)
    {
        var message = ImageGenerator.BuildTimeoutMessage(Options(bg), TimeSpan.FromSeconds(30));

        Assert.Contains("waiting for the template to finish loading", message);
        Assert.Contains("remote resource", message);
        Assert.DoesNotContain("picsum.photos", message);
    }

    [Fact]
    public void RandomBackground_PendingPicsumOnly_UsesBackgroundMessage()
    {
        var message = ImageGenerator.BuildTimeoutMessage(
            Options("random"), TimeSpan.FromSeconds(30), ["picsum.photos"]);

        Assert.Contains("random background image from picsum.photos", message);
        Assert.Contains("Omit --bg", message);
    }

    [Fact]
    public void RandomBackground_DifferentHostPending_BlamesThatHostNotPicsum()
    {
        var message = ImageGenerator.BuildTimeoutMessage(
            Options("random"), TimeSpan.FromSeconds(30), ["slow.example.com"]);

        Assert.Contains("remote content from slow.example.com", message);
        Assert.DoesNotContain("picsum.photos", message);
        Assert.Contains("--headshot", message);
    }

    [Fact]
    public void MultiplePendingHosts_ListsAllHosts()
    {
        var message = ImageGenerator.BuildTimeoutMessage(
            Options("random"), TimeSpan.FromSeconds(30), ["picsum.photos", "fonts.example.com", "PICSUM.PHOTOS"]);

        Assert.Contains("remote content from picsum.photos, fonts.example.com to load", message);
        Assert.Contains("These sites may be unreachable", message);
    }

    [Fact]
    public void NoBackground_PendingHost_NamesHost()
    {
        var message = ImageGenerator.BuildTimeoutMessage(
            Options(null), TimeSpan.FromSeconds(30), ["cdn.example.com"]);

        Assert.Contains("remote content from cdn.example.com", message);
        Assert.Contains("Try again later", message);
    }

    [Fact]
    public void Message_IsMultiLineForReadability()
    {
        var message = ImageGenerator.BuildTimeoutMessage(Options("random"), TimeSpan.FromSeconds(30));

        var lines = message.Split(Environment.NewLine);
        Assert.True(lines.Length >= 3);
        Assert.StartsWith("Timed out after 30 seconds", lines[0]);
    }

    [Theory]
    [InlineData(30, "30 seconds")]
    [InlineData(1, "1 second ")]
    [InlineData(2.5, "2.5 seconds")]
    public void IncludesTimeoutDuration(double seconds, string expected)
    {
        var message = ImageGenerator.BuildTimeoutMessage(Options("random"), TimeSpan.FromSeconds(seconds));

        Assert.Contains($"Timed out after {expected}", message);
    }

    [Fact]
    public void DurationFormatting_IsCultureInvariant()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            var message = ImageGenerator.BuildTimeoutMessage(Options("random"), TimeSpan.FromSeconds(2.5));

            Assert.Contains("Timed out after 2.5 seconds", message);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }
}
