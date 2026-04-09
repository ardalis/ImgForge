using ImgForge.Core;

namespace ImgForge.Tests;

public class FileNameHelperTests
{
    [Theory]
    [InlineData("Hello World", "hello-world.png")]
    [InlineData("The Decorator Design Pattern", "the-decorator-design-pattern.png")]
    [InlineData("Hello, World!", "hello-world.png")]
    [InlineData("C# Is Great!", "c-is-great.png")]
    [InlineData("  Leading and trailing spaces  ", "leading-and-trailing-spaces.png")]
    [InlineData("Multiple   Spaces   Between", "multiple-spaces-between.png")]
    [InlineData("Already-Has-Hyphens", "already-has-hyphens.png")]
    [InlineData("ALL CAPS TITLE", "all-caps-title.png")]
    [InlineData("Numbers 123 In Title", "numbers-123-in-title.png")]
    [InlineData("Punctuation: colons, semicolons; dashes—em-dashes", "punctuation-colons-semicolons-dashesem-dashes.png")]
    public void TitleToFileName_ReturnsExpectedSlug(string title, string expected)
    {
        var result = FileNameHelper.TitleToFileName(title);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TitleToFileName_AppendsExtension()
    {
        var result = FileNameHelper.TitleToFileName("My Post");
        Assert.EndsWith(".png", result);
    }

    [Fact]
    public void TitleToFileName_IsAllLowercase()
    {
        var result = FileNameHelper.TitleToFileName("Mixed CASE Title");
        Assert.Equal(result, result.ToLowerInvariant());
    }

    [Fact]
    public void TitleToFileName_ContainsNoSpaces()
    {
        var result = FileNameHelper.TitleToFileName("Title With Spaces");
        Assert.DoesNotContain(" ", result);
    }
}

public class FileNameHelper_ResolveOutputPathTests
{
    [Fact]
    public void ResolveOutputPath_ExplicitOut_ReturnsExplicitOut()
    {
        var result = FileNameHelper.ResolveOutputPath("output/custom.png", null, "My Title");
        Assert.Equal("output/custom.png", result);
    }

    [Fact]
    public void ResolveOutputPath_ExplicitOut_IgnoresOutDir()
    {
        var result = FileNameHelper.ResolveOutputPath("output/custom.png", "other-dir", "My Title");
        Assert.Equal("output/custom.png", result);
    }

    [Fact]
    public void ResolveOutputPath_OutDir_CombinesWithSlug()
    {
        var result = FileNameHelper.ResolveOutputPath(null, "output", "My Blog Post");
        Assert.Equal(Path.Combine("output", "my-blog-post.png"), result);
    }

    [Fact]
    public void ResolveOutputPath_NoOutNoOutDir_UsesCurrentDirectory()
    {
        var result = FileNameHelper.ResolveOutputPath(null, null, "My Blog Post");
        Assert.Equal(Path.Combine(".", "my-blog-post.png"), result);
    }

    [Fact]
    public void ResolveOutputPath_OutDir_SlugifiesTitle()
    {
        var result = FileNameHelper.ResolveOutputPath(null, "images", "The Decorator Design Pattern");
        Assert.Equal(Path.Combine("images", "the-decorator-design-pattern.png"), result);
    }
}
