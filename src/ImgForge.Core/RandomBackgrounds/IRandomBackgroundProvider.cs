namespace ImgForge.Core.RandomBackgrounds;

/// <summary>A source of random background images for <c>--bg random</c>.</summary>
internal interface IRandomBackgroundProvider
{
    /// <summary>A short, user-facing name for the provider (typically its host, e.g. <c>picsum.photos</c>).</summary>
    string Name { get; }

    /// <summary>Downloads a random image of (approximately) the given size.</summary>
    /// <exception cref="RandomBackgroundProviderException">The provider responded, but not with a usable image.</exception>
    /// <exception cref="HttpRequestException">The provider could not be reached.</exception>
    Task<RandomBackgroundImage> FetchAsync(int width, int height, CancellationToken cancellationToken);
}

/// <summary>A downloaded random background image.</summary>
/// <param name="Content">The image bytes.</param>
/// <param name="MediaType">The response media type, e.g. <c>image/jpeg</c>.</param>
internal sealed record RandomBackgroundImage(byte[] Content, string MediaType);

/// <summary>
/// Thrown by a provider that responded but didn't return a usable image. <see cref="Exception.Message"/> is a
/// user-facing reason such as "the site returned HTTP 522".
/// </summary>
internal sealed class RandomBackgroundProviderException(string reason) : Exception(reason);
