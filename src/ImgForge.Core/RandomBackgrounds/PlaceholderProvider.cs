using System.Text.RegularExpressions;

namespace ImgForge.Core.RandomBackgrounds;

/// <summary>
/// A solid-color image from <see href="https://placehold.co">placehold.co</see> (no API key). Not a photo, but very
/// unlikely to fail, so it is the last resort in the default provider order.
/// </summary>
/// <remarks>
/// placehold.co always draws a size label; it is drawn in the background color, so the image is visually solid.
/// Each dimension is capped at 4000 px by the service, which doesn't matter for a solid color.
/// </remarks>
internal sealed partial class PlaceholderProvider(HttpClient httpClient) : IRandomBackgroundProvider
{
    /// <summary>The color used when none is given: a dark slate, so the templates' white text stays readable.</summary>
    public const string DefaultColor = "1e293b";

    public PlaceholderProvider() : this(HttpImageDownloader.SharedClient) { }

    public string Name => "placehold.co";

    /// <summary>How long placehold.co has to start responding.</summary>
    public TimeSpan ResponseTimeout { get; init; } = HttpImageDownloader.DefaultResponseTimeout;

    /// <summary>
    /// The background color as a 6-digit hex value, with or without a leading <c>#</c> (e.g. <c>#1E293B</c>).
    /// Stored lower-case without the <c>#</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The value isn't a 6-digit hex color.</exception>
    public string Color
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            var hex = value.StartsWith('#') ? value[1..] : value;
            if (!ColorPattern().IsMatch(hex))
                throw new ArgumentException(
                    $"Invalid placeholder color \"{value}\". Use a 6-digit hex color such as 1e293b or #1E293B.",
                    nameof(Color));
            field = hex.ToLowerInvariant();
        }
    } = DefaultColor;

    internal Uri ImageUrl(int width, int height) =>
        new($"https://placehold.co/{width}x{height}/{Color}/{Color}/png");

    public Task<RandomBackgroundImage> FetchAsync(int width, int height, CancellationToken cancellationToken) =>
        HttpImageDownloader.DownloadImageAsync(httpClient, ImageUrl(width, height), ResponseTimeout, cancellationToken);

    [GeneratedRegex(@"\A[0-9A-Fa-f]{6}\z")]
    private static partial Regex ColorPattern();
}
