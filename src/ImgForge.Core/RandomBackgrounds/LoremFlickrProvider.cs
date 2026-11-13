using System.Globalization;
using System.Text.RegularExpressions;

namespace ImgForge.Core.RandomBackgrounds;

/// <summary>
/// Random Flickr photos from <see href="https://loremflickr.com">loremflickr.com</see> (no API key), matching a
/// keyword such as <c>abstract</c>, <c>technology</c>, or <c>city</c>.
/// </summary>
/// <remarks>Photo quality and licensing are less consistent than <see cref="PicsumProvider"/>'s Unsplash set.</remarks>
internal sealed partial class LoremFlickrProvider(HttpClient httpClient) : IRandomBackgroundProvider
{
    /// <summary>The keyword used when none is given.</summary>
    public const string DefaultKeyword = "abstract";

    /// <summary>The longest keyword value accepted, so a typo can't build an oversized URL.</summary>
    public const int MaxKeywordLength = 100;

    public LoremFlickrProvider() : this(HttpImageDownloader.SharedClient) { }

    public string Name => "loremflickr.com";

    /// <summary>How long loremflickr.com has to start responding.</summary>
    public TimeSpan ResponseTimeout { get; init; } = HttpImageDownloader.DefaultResponseTimeout;

    /// <summary>
    /// The photo subject: one keyword, or several separated by commas (e.g. <c>city,night</c>). Each keyword may
    /// contain only ASCII letters, digits, <c>-</c> and <c>_</c>, and the whole value is at most
    /// <see cref="MaxKeywordLength"/> characters. Stored lower-case.
    /// </summary>
    /// <exception cref="ArgumentException">The value is empty, too long, or contains other characters.</exception>
    public string Keyword
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!IsValidKeyword(value))
                throw new ArgumentException(
                    $"Invalid LoremFlickr keyword \"{value}\". Use one or more keywords separated by commas, " +
                    $"each made of letters, digits, '-' or '_', up to {MaxKeywordLength} characters in total " +
                    "(for example: abstract or city,night).",
                    nameof(Keyword));
            field = value.ToLowerInvariant();
        }
    } = DefaultKeyword;

    /// <summary>
    /// Optional lock value. The same size, keyword, and lock return the same image, so output is repeatable.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int? Lock
    {
        get;
        init
        {
            if (value is < 0)
                throw new ArgumentOutOfRangeException(nameof(Lock), value, "The LoremFlickr lock value cannot be negative.");
            field = value;
        }
    }

    /// <summary>True when <paramref name="value"/> is an acceptable <see cref="Keyword"/>.</summary>
    internal static bool IsValidKeyword(string value) =>
        value.Length <= MaxKeywordLength && KeywordPattern().IsMatch(value);

    internal Uri ImageUrl(int width, int height)
    {
        var url = $"https://loremflickr.com/{width}/{height}/{Keyword}";
        if (Lock is { } lockValue)
            url += "?lock=" + lockValue.ToString(CultureInfo.InvariantCulture);
        return new Uri(url);
    }

    public Task<RandomBackgroundImage> FetchAsync(int width, int height, CancellationToken cancellationToken) =>
        HttpImageDownloader.DownloadImageAsync(httpClient, ImageUrl(width, height), ResponseTimeout, cancellationToken);

    [GeneratedRegex(@"\A[A-Za-z0-9_-]+(,[A-Za-z0-9_-]+)*\z")]
    private static partial Regex KeywordPattern();
}
