namespace ImgForge.Core.RandomBackgrounds;

/// <summary>
/// A parsed <c>--bg random[:provider[:option]]</c> value: either the default provider order, or one named provider
/// (with an optional provider-specific option such as a LoremFlickr keyword or a placeholder color).
/// </summary>
internal sealed record RandomBackgroundSpec
{
    /// <summary>The <c>--bg</c> keyword that asks for a random background.</summary>
    public const string Keyword = "random";

    /// <summary>The <c>--bg</c> text used for the default provider order.</summary>
    public const string DefaultOptionText = "--bg " + Keyword;

    public const string Picsum = "picsum";
    public const string LoremFlickr = "loremflickr";
    public const string Placeholder = "placeholder";
    public const string Unsplash = "unsplash";
    public const string Pexels = "pexels";
    public const string Pixabay = "pixabay";

    /// <summary>The provider names accepted after <c>random:</c>, in the order shown to users.</summary>
    public static IReadOnlyList<string> ProviderNames { get; } = [Picsum, LoremFlickr, Placeholder, Unsplash, Pexels, Pixabay];

    /// <summary>Providers that need an API key: the environment variable holding it, and how to describe it.</summary>
    private static readonly Dictionary<string, KeyedProvider> KeyedProviders = new()
    {
        [Unsplash] = new(UnsplashProvider.AccessKeyVariable, "Unsplash", "access key", "https://unsplash.com/developers"),
        [Pexels] = new(PexelsProvider.ApiKeyVariable, "Pexels", "API key", "https://www.pexels.com/api/"),
        [Pixabay] = new(PixabayProvider.ApiKeyVariable, "Pixabay", "API key", "https://pixabay.com/api/docs/"),
    };

    private sealed record KeyedProvider(string Variable, string Service, string KeyKind, string SignUpUrl);

    /// <summary>The default provider order (<c>--bg random</c>).</summary>
    public static RandomBackgroundSpec Default { get; } = new(null, null);

    /// <summary>The API key for a provider that needs one. Private so it never appears in <c>ToString</c>.</summary>
    private readonly string? _apiKey;

    private RandomBackgroundSpec(string? providerName, string? option, string? apiKey = null)
    {
        ProviderName = providerName;
        Option = option;
        _apiKey = apiKey;
    }

    /// <summary>The selected provider (one of <see cref="ProviderNames"/>), or <c>null</c> for the default order.</summary>
    public string? ProviderName { get; }

    /// <summary>The provider-specific option, or <c>null</c> when none was given.</summary>
    public string? Option { get; }

    /// <summary>True for plain <c>--bg random</c> (the default provider order with fallback).</summary>
    public bool IsDefault => ProviderName is null;

    /// <summary>The normalized <c>--bg</c> value, e.g. <c>--bg random:loremflickr:technology</c>, for messages.</summary>
    public string OptionText =>
        ProviderName is null ? DefaultOptionText
        : Option is null ? $"{DefaultOptionText}:{ProviderName}"
        : $"{DefaultOptionText}:{ProviderName}:{Option}";

    /// <summary>
    /// True when <paramref name="background"/> asks for a random image: <c>random</c> or <c>random:...</c>
    /// (case-insensitive, surrounding whitespace ignored). Doesn't check that the rest is valid.
    /// </summary>
    public static bool IsRandom(string? background)
    {
        var value = background?.Trim();
        return value is not null &&
            (value.Equals(Keyword, StringComparison.OrdinalIgnoreCase) ||
             value.StartsWith(Keyword + ":", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Parses a <c>--bg</c> value for which <see cref="IsRandom"/> is true.</summary>
    /// <exception cref="ArgumentException">
    /// The value isn't a random background, names an unknown provider, has a missing or invalid option, or selects an
    /// API provider (Unsplash, Pexels, Pixabay) whose key environment variable isn't set.
    /// </exception>
    public static RandomBackgroundSpec Parse(string background) =>
        Parse(background, ApiProviderHelpers.KeyFromEnvironment);

    /// <inheritdoc cref="Parse(string)"/>
    /// <param name="background">The <c>--bg</c> value.</param>
    /// <param name="apiKey">
    /// Gets the key from the named environment variable (or <c>null</c>); called only for a provider that needs a key.
    /// </param>
    internal static RandomBackgroundSpec Parse(string background, Func<string, string?> apiKey)
    {
        if (!IsRandom(background))
            throw new ArgumentException($"\"{background}\" is not a random background (expected \"{Keyword}\" or \"{Keyword}:<provider>\").", nameof(background));

        var parts = background.Trim().Split(':', 3);
        if (parts.Length == 1)
            return Default;

        var name = parts[1].Trim().ToLowerInvariant();
        if (name.Length == 0)
            throw Invalid(background, $"a provider name is missing after \"{Keyword}:\". {ValidProvidersText}");
        if (!ProviderNames.Contains(name))
            throw Invalid(background, $"unknown provider \"{parts[1].Trim()}\". {ValidProvidersText}");

        if (KeyedProviders.TryGetValue(name, out var keyed))
        {
            if (parts.Length == 3)
                throw Invalid(background, $"{name} doesn't take an option. Use \"{Keyword}:{name}\".");
            var key = apiKey(keyed.Variable);
            if (string.IsNullOrWhiteSpace(key))
                throw Invalid(background, $"{keyed.Service} needs an {keyed.KeyKind}. Set the {keyed.Variable} " +
                    $"environment variable to your {keyed.Service} {keyed.KeyKind} (free at {keyed.SignUpUrl}).");
            return new RandomBackgroundSpec(name, null, key.Trim());
        }

        if (parts.Length == 2)
            return new RandomBackgroundSpec(name, null);

        if (name == Picsum)
            throw Invalid(background, $"{name} doesn't take an option. Use \"{Keyword}:{name}\".");

        var option = parts[2].Trim();
        if (option.Length == 0)
            throw Invalid(background, $"the {name} option after the last \":\" is empty. {OptionHelp(name)}");

        switch (name)
        {
            case LoremFlickr when !LoremFlickrProvider.IsValidKeyword(option):
                throw Invalid(background, $"\"{option}\" is not a valid LoremFlickr keyword. {OptionHelp(name)}");
            case Placeholder when !PlaceholderProvider.IsValidColor(option):
                throw Invalid(background, $"\"{option}\" is not a valid placeholder color. {OptionHelp(name)}");
        }

        return new RandomBackgroundSpec(name, option);
    }

    /// <summary>
    /// Creates the selected provider (configured with <see cref="Option"/>), or <c>null</c> for the default order.
    /// </summary>
    /// <param name="httpClient">The client to use, or <c>null</c> for <see cref="HttpImageDownloader.SharedClient"/>.</param>
    public IRandomBackgroundProvider? CreateProvider(HttpClient? httpClient = null)
    {
        var client = httpClient ?? HttpImageDownloader.SharedClient;
        return ProviderName switch
        {
            null => null,
            Picsum => new PicsumProvider(client),
            LoremFlickr => Option is null ? new LoremFlickrProvider(client) : new LoremFlickrProvider(client) { Keyword = Option },
            Placeholder => Option is null ? new PlaceholderProvider(client) : new PlaceholderProvider(client) { Color = Option },
            Unsplash => new UnsplashProvider(client, _apiKey!),
            Pexels => new PexelsProvider(client, _apiKey!),
            Pixabay => new PixabayProvider(client, _apiKey!),
            _ => throw new InvalidOperationException($"Unhandled provider \"{ProviderName}\".")
        };
    }

    private static string ValidProvidersText =>
        $"Valid providers: {string.Join(", ", ProviderNames)} (for example: --bg {Keyword}:{Picsum}).";

    private static string OptionHelp(string name) => name switch
    {
        LoremFlickr => "Use one or more keywords separated by commas, each made of letters, digits, '-' or '_', " +
            $"up to {LoremFlickrProvider.MaxKeywordLength} characters in total (for example: {Keyword}:{LoremFlickr}:technology).",
        _ => $"Use a 6-digit hex color such as 1e293b or #1E293B (for example: {Keyword}:{Placeholder}:0f172a)."
    };

    private static ArgumentException Invalid(string background, string reason) =>
        new($"Invalid --bg value \"{background.Trim()}\": {reason}");
}
