using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ImgForge.Core.RandomBackgrounds;

/// <summary>Shared plumbing for providers that call a JSON API (Unsplash, Pexels, Pixabay) before downloading an image.</summary>
internal static class ApiProviderHelpers
{
    /// <summary>The largest API (JSON) response accepted.</summary>
    internal const int MaxJsonBytes = 1024 * 1024;

    /// <summary>The reason given when the API's JSON is malformed or missing something ImgForge needs.</summary>
    internal const string UnexpectedResponse = "the site returned an unexpected response";

    /// <summary>The value of environment variable <paramref name="variable"/>, trimmed, or <c>null</c> when it isn't set
    /// (or is only whitespace).</summary>
    public static string? KeyFromEnvironment(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { } key && !string.IsNullOrWhiteSpace(key) ? key.Trim() : null;

    /// <summary>A GET request that asks for JSON (replacing the shared client's default <c>Accept: image/*</c>).</summary>
    public static HttpRequestMessage JsonRequest(Uri url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>Sends <paramref name="request"/>, giving the site <paramref name="responseTimeout"/> to start responding.</summary>
    /// <exception cref="RandomBackgroundProviderException">The site didn't respond in time.</exception>
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpRequestMessage request, TimeSpan responseTimeout, CancellationToken cancellationToken)
    {
        using var responseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        responseCts.CancelAfter(responseTimeout);
        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, responseCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RandomBackgroundProviderException(
                $"the site did not respond within {HttpImageDownloader.FormatSeconds(responseTimeout)}");
        }
    }

    /// <summary>Reads a JSON body of at most <see cref="MaxJsonBytes"/>.</summary>
    /// <exception cref="RandomBackgroundProviderException">The body is too large or the connection dropped.</exception>
    public static async Task<byte[]> ReadJsonAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxJsonBytes)
            throw new RandomBackgroundProviderException(UnexpectedResponse);

        using var buffer = new MemoryStream();
        try
        {
            await using var body = await content.ReadAsStreamAsync(cancellationToken);
            var chunk = new byte[16384];
            int read;
            while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxJsonBytes)
                    throw new RandomBackgroundProviderException(UnexpectedResponse);
                buffer.Write(chunk, 0, read);
            }
        }
        catch (IOException ex)
        {
            throw new RandomBackgroundProviderException(
                $"the connection was lost while reading the response ({ex.GetBaseException().Message.TrimEnd('.', ' ')})");
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Parses <paramref name="json"/> with <paramref name="read"/>, turning malformed JSON (or an invalid string such as
    /// a lone surrogate escape) into an "unexpected response" <see cref="RandomBackgroundProviderException"/>.
    /// </summary>
    public static T Parse<T>(byte[] json, Func<JsonElement, T> read)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return read(document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // InvalidOperationException: e.g. GetString() on a string with an invalid surrogate escape.
            throw new RandomBackgroundProviderException(UnexpectedResponse);
        }
    }

    /// <summary>True when the site says no requests are left (<c>X-Ratelimit-Remaining: 0</c>).</summary>
    public static bool IsRateLimitExhausted(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Ratelimit-Remaining", out var values) &&
        values.FirstOrDefault()?.Trim() == "0";

    /// <summary>The string at <paramref name="path"/> under <paramref name="element"/>, or <c>null</c>.</summary>
    public static string? GetString(JsonElement element, params string[] path)
    {
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
                return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    /// <summary><paramref name="value"/> as an absolute https URL, or <c>null</c>.</summary>
    public static Uri? AbsoluteHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps ? url : null;

    /// <summary><paramref name="value"/> as an absolute https URL on one of <paramref name="hosts"/>, or <c>null</c>.</summary>
    public static Uri? HttpsUrlOnHost(string? value, params string[] hosts) =>
        AbsoluteHttpsUrl(value) is { } url && hosts.Any(h => url.Host.Equals(h, StringComparison.OrdinalIgnoreCase))
            ? url
            : null;

    /// <summary>
    /// A name from an API with control, format (e.g. right-to-left override), and line/paragraph separator characters
    /// removed, so it can't mess up or spoof the terminal line. Empty when nothing printable is left.
    /// </summary>
    public static string CleanName(string? name) => new string((name ?? "").Where(IsPrintable).ToArray()).Trim();

    /// <summary><paramref name="url"/> with <paramref name="parameters"/> appended to its query.</summary>
    public static Uri AppendQuery(Uri url, string parameters)
    {
        var query = url.Query.TrimStart('?');
        return new UriBuilder(url) { Query = query.Length == 0 ? parameters : $"{query}&{parameters}" }.Uri;
    }

    private static bool IsPrintable(char c) => char.GetUnicodeCategory(c) is not (
        UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or
        UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate);
}
