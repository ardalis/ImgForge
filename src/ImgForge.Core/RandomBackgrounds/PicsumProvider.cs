namespace ImgForge.Core.RandomBackgrounds;

/// <summary>Random photos from <see href="https://picsum.photos">picsum.photos</see> (no API key).</summary>
internal sealed class PicsumProvider(HttpClient httpClient) : IRandomBackgroundProvider
{
    public PicsumProvider() : this(HttpImageDownloader.SharedClient) { }

    public string Name => "picsum.photos";

    internal static Uri ImageUrl(int width, int height) => new($"https://picsum.photos/{width}/{height}");

    public Task<RandomBackgroundImage> FetchAsync(int width, int height, CancellationToken cancellationToken) =>
        HttpImageDownloader.DownloadImageAsync(httpClient, ImageUrl(width, height), cancellationToken);
}
