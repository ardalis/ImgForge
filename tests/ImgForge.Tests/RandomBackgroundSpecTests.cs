using ImgForge.Commands;
using ImgForge.Core;
using ImgForge.Core.RandomBackgrounds;

namespace ImgForge.Tests;

public class RandomBackgroundSpecTests
{
    // ---- IsRandom ----

    [Theory]
    [InlineData("random")]
    [InlineData(" Random ")]
    [InlineData("RANDOM")]
    [InlineData("random:picsum")]
    [InlineData("Random:LoremFlickr:city")]
    [InlineData("random:")]
    [InlineData("random:bogus")]
    public void IsRandom_RandomValues_ReturnsTrue(string background) =>
        Assert.True(RandomBackgroundSpec.IsRandom(background));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("randomize.jpg")]
    [InlineData("random.jpg")]
    [InlineData("./random:picsum")]
    [InlineData("https://example.com/random")]
    [InlineData("C:\\images\\random.png")]
    public void IsRandom_OtherValues_ReturnsFalse(string? background) =>
        Assert.False(RandomBackgroundSpec.IsRandom(background));

    // ---- Parse: valid ----

    [Theory]
    [InlineData("random")]
    [InlineData("  RANDOM  ")]
    public void Parse_PlainRandom_ReturnsDefaultOrder(string background)
    {
        var spec = RandomBackgroundSpec.Parse(background);

        Assert.True(spec.IsDefault);
        Assert.Null(spec.ProviderName);
        Assert.Null(spec.CreateProvider());
        Assert.Equal("--bg random", spec.OptionText);
    }

    [Theory]
    [InlineData("random:picsum", "picsum", "--bg random:picsum")]
    [InlineData("Random:Picsum", "picsum", "--bg random:picsum")]
    [InlineData("random: loremflickr ", "loremflickr", "--bg random:loremflickr")]
    [InlineData("random:placeholder", "placeholder", "--bg random:placeholder")]
    public void Parse_ProviderWithoutOption_SelectsProvider(string background, string provider, string optionText)
    {
        var spec = RandomBackgroundSpec.Parse(background);

        Assert.False(spec.IsDefault);
        Assert.Equal(provider, spec.ProviderName);
        Assert.Null(spec.Option);
        Assert.Equal(optionText, spec.OptionText);
    }

    [Theory]
    [InlineData("random:loremflickr:technology", "technology")]
    [InlineData("random:LoremFlickr:city,night", "city,night")]
    [InlineData("random:placeholder:#0F172A", "#0F172A")]
    [InlineData("random:placeholder: 0f172a ", "0f172a")]
    public void Parse_ProviderWithOption_KeepsOption(string background, string option)
    {
        var spec = RandomBackgroundSpec.Parse(background);

        Assert.Equal(option, spec.Option);
        Assert.Equal($"--bg random:{spec.ProviderName}:{option}", spec.OptionText);
    }

    // ---- Parse: errors ----

    [Fact]
    public void Parse_UnknownProvider_ListsValidProviders()
    {
        var ex = Assert.Throws<ArgumentException>(() => RandomBackgroundSpec.Parse("random:unsplash"));

        Assert.Equal(
            "Invalid --bg value \"random:unsplash\": unknown provider \"unsplash\". " +
            "Valid providers: picsum, loremflickr, placeholder (for example: --bg random:picsum).",
            ex.Message);
    }

    [Theory]
    [InlineData("random:")]
    [InlineData("random: ")]
    [InlineData("random::x")]
    public void Parse_MissingProviderName_Throws(string background)
    {
        var ex = Assert.Throws<ArgumentException>(() => RandomBackgroundSpec.Parse(background));

        Assert.Contains("a provider name is missing", ex.Message);
        Assert.Contains("Valid providers: picsum, loremflickr, placeholder", ex.Message);
    }

    [Theory]
    [InlineData("random:loremflickr:")]
    [InlineData("random:placeholder: ")]
    public void Parse_EmptyOption_Throws(string background)
    {
        var ex = Assert.Throws<ArgumentException>(() => RandomBackgroundSpec.Parse(background));

        Assert.Contains("option after the last \":\" is empty", ex.Message);
    }

    [Theory]
    [InlineData("random:picsum:grayscale")]
    [InlineData("random:picsum:")]
    public void Parse_OptionOnPicsum_Throws(string background)
    {
        var ex = Assert.Throws<ArgumentException>(() => RandomBackgroundSpec.Parse(background));

        Assert.Equal(
            $"Invalid --bg value \"{background}\": picsum doesn't take an option. Use \"random:picsum\".",
            ex.Message);
    }

    [Theory]
    [InlineData("random:loremflickr:city night")]
    [InlineData("random:loremflickr:a/b")]
    [InlineData("random:loremflickr:city,")]
    [InlineData("random:loremflickr:a:b")]
    public void Parse_InvalidLoremFlickrKeyword_Throws(string background)
    {
        var ex = Assert.Throws<ArgumentException>(() => RandomBackgroundSpec.Parse(background));

        Assert.Contains("is not a valid LoremFlickr keyword", ex.Message);
        Assert.Contains("random:loremflickr:technology", ex.Message);
    }

    [Fact]
    public void Parse_TooLongLoremFlickrKeyword_Throws()
    {
        var keyword = new string('a', LoremFlickrProvider.MaxKeywordLength + 1);

        var ex = Assert.Throws<ArgumentException>(() => RandomBackgroundSpec.Parse("random:loremflickr:" + keyword));

        Assert.Contains("is not a valid LoremFlickr keyword", ex.Message);
    }

    [Theory]
    [InlineData("random:placeholder:red")]
    [InlineData("random:placeholder:fff")]
    [InlineData("random:placeholder:##0f172a")]
    [InlineData("random:placeholder:0f172a/x")]
    public void Parse_InvalidPlaceholderColor_Throws(string background)
    {
        var ex = Assert.Throws<ArgumentException>(() => RandomBackgroundSpec.Parse(background));

        Assert.Contains("is not a valid placeholder color", ex.Message);
        Assert.Contains("6-digit hex color", ex.Message);
    }

    [Fact]
    public void Parse_NotRandom_Throws() =>
        Assert.Throws<ArgumentException>(() => RandomBackgroundSpec.Parse("photo.jpg"));

    // ---- CreateProvider ----

    [Fact]
    public void CreateProvider_Picsum_ReturnsPicsumProvider() =>
        Assert.IsType<PicsumProvider>(RandomBackgroundSpec.Parse("random:picsum").CreateProvider());

    [Theory]
    [InlineData("random:loremflickr", LoremFlickrProvider.DefaultKeyword)]
    [InlineData("random:loremflickr:City,Night", "city,night")]
    public void CreateProvider_LoremFlickr_AppliesKeyword(string background, string keyword)
    {
        var provider = Assert.IsType<LoremFlickrProvider>(RandomBackgroundSpec.Parse(background).CreateProvider());

        Assert.Equal(keyword, provider.Keyword);
    }

    [Theory]
    [InlineData("random:placeholder", PlaceholderProvider.DefaultColor)]
    [InlineData("random:placeholder:#0F172A", "0f172a")]
    public void CreateProvider_Placeholder_AppliesColor(string background, string color)
    {
        var provider = Assert.IsType<PlaceholderProvider>(RandomBackgroundSpec.Parse(background).CreateProvider());

        Assert.Equal(color, provider.Color);
    }

    [Fact]
    public async Task CreateProvider_UsesGivenHttpClient()
    {
        var handler = FakeHttpMessageHandler.Returning(() => FakeHttpMessageHandler.Image([0x89, 0x50, 0x4E, 0x47], "image/png"));
        var provider = RandomBackgroundSpec.Parse("random:loremflickr:technology").CreateProvider(new HttpClient(handler))!;

        await provider.FetchAsync(1280, 720, CancellationToken.None);

        Assert.Equal("https://loremflickr.com/1280/720/technology", Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    // ---- ImageGenerator.ResolverFor ----

    [Fact]
    public void ResolverFor_Default_UsesConfiguredResolver()
    {
        var generator = new ImageGenerator(new TemplateRenderer());

        Assert.Same(generator.RandomBackgrounds, generator.ResolverFor(RandomBackgroundSpec.Default));
    }

    [Fact]
    public void ResolverFor_NamedProvider_UsesOnlyThatProviderWithConfiguredSettings()
    {
        var generator = new ImageGenerator(new TemplateRenderer())
        {
            RandomBackgrounds = new RandomBackgroundResolver(new PicsumProvider())
            {
                DownloadTimeout = TimeSpan.FromSeconds(7),
                TempDirectory = "custom-temp"
            }
        };

        var resolver = generator.ResolverFor(RandomBackgroundSpec.Parse("random:placeholder:0f172a"));

        var provider = Assert.IsType<PlaceholderProvider>(Assert.Single(resolver.Providers));
        Assert.Equal("0f172a", provider.Color);
        Assert.Equal("--bg random:placeholder:0f172a", resolver.BackgroundOption);
        Assert.Equal(TimeSpan.FromSeconds(7), resolver.DownloadTimeout);
        Assert.Equal("custom-temp", resolver.TempDirectory);
    }

    [Fact]
    public async Task GenerateAsync_InvalidRandomProvider_ThrowsBeforeDownloading()
    {
        var called = false;
        var generator = new ImageGenerator(new TemplateRenderer())
        {
            RandomBackgrounds = new RandomBackgroundResolver(new StubRandomBackgroundProvider((_, _, _) =>
            {
                called = true;
                throw new InvalidOperationException("should not be called");
            }))
        };
        var opts = new GenerateOptions("blog", "Hello", null, "random:bogus", [], "out.png");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => generator.GenerateAsync(opts));

        Assert.Contains("unknown provider \"bogus\"", ex.Message);
        Assert.False(called);
    }

    // ---- Resolver error message for a named provider ----

    [Fact]
    public void BuildErrorMessage_NamedProvider_NamesOptionAndSuggestsDefaultOrder()
    {
        var message = RandomBackgroundResolver.BuildErrorMessage(
            [new RandomBackgroundProviderFailure("loremflickr.com", "the site returned HTTP 500")],
            "--bg random:loremflickr:technology");

        var lines = message.Split(Environment.NewLine);
        Assert.Equal(
            "Could not load a random background image from loremflickr.com " +
            "(used by --bg random:loremflickr:technology): the site returned HTTP 500.",
            lines[0]);
        Assert.Contains("  - Use --bg random to try each provider in turn instead of only this one.", lines);
    }

    [Fact]
    public void BuildErrorMessage_DefaultOrder_DoesNotSuggestDefaultOrder()
    {
        var message = RandomBackgroundResolver.BuildErrorMessage(
            [new RandomBackgroundProviderFailure("picsum.photos", "the site returned HTTP 522")]);

        Assert.DoesNotContain("try each provider in turn", message);
    }

    [Fact]
    public async Task ResolveToFileAsync_NamedProviderFails_ErrorNamesOption()
    {
        var resolver = new RandomBackgroundResolver(new StubRandomBackgroundProvider((_, _, _) =>
            throw new RandomBackgroundProviderException("the site returned HTTP 500")))
        { BackgroundOption = "--bg random:picsum" };

        var ex = await Assert.ThrowsAsync<ImageLoadException>(() => resolver.ResolveToFileAsync(100, 100));

        Assert.StartsWith(
            "Could not load a random background image from stub.example (used by --bg random:picsum): " +
            "the site returned HTTP 500.",
            ex.Message);
    }

    // ---- GenerateCommand.ValidateBackground ----

    [Theory]
    [InlineData(null)]
    [InlineData("photo.jpg")]
    [InlineData("https://example.com/bg.jpg")]
    [InlineData("random")]
    [InlineData("random:loremflickr:technology")]
    public void ValidateBackground_ValidValues_DoesNotThrow(string? background) =>
        GenerateCommand.ValidateBackground(background);

    [Fact]
    public void ValidateBackground_UnknownProvider_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => GenerateCommand.ValidateBackground("random:bogus"));

        Assert.Contains("Valid providers: picsum, loremflickr, placeholder", ex.Message);
    }
}
