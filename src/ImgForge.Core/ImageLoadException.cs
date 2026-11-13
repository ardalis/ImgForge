namespace ImgForge.Core;

/// <summary>
/// Thrown when a remote image the user asked for (via <c>--bg</c>, <c>--headshot</c>, or <c>--overlay</c>)
/// fails to load, for example because the site is unreachable or returns an HTTP error.
/// </summary>
public class ImageLoadException(string message) : Exception(message);
