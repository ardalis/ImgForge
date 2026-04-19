using ImgForge.Commands;

namespace ImgForge.Tests;

public class GenerateCommandFormatTests
{
    [Theory]
    [InlineData("github")]
    [InlineData("GitHub")]
    public void ResolvePresetDimensions_GitHubFormat_UsesSocialPreviewDimensions(string format)
    {
        var result = GenerateCommand.ResolvePresetDimensions(format);

        Assert.True(result.HasValue);
        Assert.Equal((1280, 640), result.Value);
    }

    [Fact]
    public void ResolvePresetDimensions_UnknownFormat_ThrowsWithValidChoices()
    {
        var ex = Assert.Throws<ArgumentException>(() => GenerateCommand.ResolvePresetDimensions("unknown"));

        Assert.Contains("Valid choices:", ex.Message);
        Assert.Contains("github", ex.Message);
    }
}
