using System.Reflection;
using ImgForge.Commands;

namespace ImgForge.Tests;

public class GenerateCommandFormatTests
{
    [Theory]
    [InlineData("github")]
    [InlineData("GitHub")]
    public void ResolvePresetDimensions_GitHubFormat_UsesSocialPreviewDimensions(string format)
    {
        var result = InvokeResolvePresetDimensions(format);

        Assert.Equal((1280, 640), result);
    }

    [Fact]
    public void ResolvePresetDimensions_UnknownFormat_ThrowsWithValidChoices()
    {
        var ex = Assert.Throws<TargetInvocationException>(() => InvokeResolvePresetDimensions("unknown"));

        var inner = Assert.IsType<ArgumentException>(ex.InnerException);
        Assert.Contains("Valid choices: youtube, blog, github, podcast-show, podcast-episode.", inner.Message);
    }

    private static (int Width, int Height) InvokeResolvePresetDimensions(string format)
    {
        var method = typeof(GenerateCommand).GetMethod("ResolvePresetDimensions", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = method!.Invoke(null, [format]);
        var dims = Assert.IsType<ValueTuple<int, int>>(result);

        return (dims.Item1, dims.Item2);
    }
}
