namespace ImgForge.Tests;

/// <summary>An <see cref="HttpMessageHandler"/> that answers every request with a canned response, without a network.</summary>
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public static FakeHttpMessageHandler Returning(Func<HttpResponseMessage> response) =>
        new((_, _) => Task.FromResult(response()));

    public static HttpResponseMessage Image(byte[] content, string mediaType = "image/jpeg")
    {
        var body = new ByteArrayContent(content);
        body.Headers.ContentType = new(mediaType);
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = body };
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return respond(request, cancellationToken);
    }
}
