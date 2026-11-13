namespace ImgForge.Core;

/// <summary>
/// Thrown when the rendered template does not finish loading in time, usually because a remote
/// resource it references (such as a background image) is unreachable or slow.
/// </summary>
public class ImageGenerationTimeoutException(string message, Exception? innerException = null)
    : Exception(message, innerException);
