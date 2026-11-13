using ImgForge.Core.RandomBackgrounds;

namespace ImgForge.Tests;

/// <summary>A random background provider whose behavior is supplied by the test.</summary>
internal sealed class StubRandomBackgroundProvider(
    Func<int, int, CancellationToken, Task<RandomBackgroundImage>> fetch,
    string name = "stub.example")
    : IRandomBackgroundProvider
{
    public string Name => name;

    public int Calls { get; private set; }

    public Task<RandomBackgroundImage> FetchAsync(int width, int height, CancellationToken cancellationToken)
    {
        Calls++;
        return fetch(width, height, cancellationToken);
    }
}
