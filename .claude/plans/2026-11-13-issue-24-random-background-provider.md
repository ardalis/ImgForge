# Issue #24 — Resolve `--bg random` in C# via an `IRandomBackgroundProvider` abstraction (Picsum)

Issue: https://github.com/ardalis/ImgForge/issues/24
Parent: #23 (multiple image sources). Follow-ups that build on this: #25 (fallback chain), #26–#28, #30, #31 (providers), #29 (CLI syntax).

## Summary

Today `--bg random` gives Chromium `https://picsum.photos/{w}/{h}`. A slow or failing Picsum uses up the whole
30-second page timeout. `picsum.photos` is also hard-coded in `TemplateRenderer`, `ImageGenerator` and
`ResourceLoadFailures`. This change downloads the random image in C# **before** rendering, using a short
per-provider timeout. It saves the image to a temp file and passes a `file:///` URI to the template. Picsum is
still the only provider.

## Decisions / assumptions

- **Abstraction (internal for now):** `IRandomBackgroundProvider { string Name; Task<RandomBackgroundImage> FetchAsync(int width, int height, CancellationToken) }`.
  `FetchAsync` returns the image bytes and media type, so API-based providers (Unsplash #30) fit later. The
  types are `internal`. The public API surface is kept small until the provider set settles (#29).
- **`RandomBackgroundResolver`** owns the provider list (just Picsum for now) and the ~5s per-provider timeout.
  It writes the image to a temp file with an extension that matches the media type, and returns the path.
  Fallback across several providers is **#25**. This issue keeps the list-shaped API, but the first failure
  ends the run with a clear error.
- **Failure handling:** non-2xx status, a non-`image/*` content type, a network error, or the 5s timeout throw
  `ImageLoadException`. The CLI already prints that as `Error: ...` and exits 1. The message names the provider
  and the reason in the same style as #21, for example: `Could not load a random background image from
  picsum.photos (used by --bg random): the site returned HTTP 522.`, followed by "You can:" suggestions. No PNG
  is written.
- **ImageGenerator** resolves the random background first. It renders, and builds its timeout and load-failure
  messages, with `opts with { Background = <temp path> }`. The temp file is deleted in `finally`. Because the
  page never requests a random background URL any more, `ResourceSource.RandomBackground`, the random branch of
  `BuildTimeoutMessage`, `TemplateRenderer.RandomBackgroundUrl` and both `RandomBackgroundHost` constants are
  removed.
- **`TemplateRenderer.Render` with an unresolved `random`** throws `InvalidOperationException`, explaining that
  `ImageGenerator` resolves it. `IsRandomBackground` stays as the shared check.
- **HttpClient:** one static instance for the default Picsum provider, so sockets aren't exhausted. Redirects
  are followed (Picsum redirects to its CDN). The User-Agent is `ImgForge`. The timeout comes from a linked
  `CancellationTokenSource` in the resolver, not from `HttpClient.Timeout`, so the resolver can tell "timed
  out" apart from "caller cancelled".
- **Tests:** these use a fake `HttpMessageHandler`, with no live network. The timeout test injects a short
  timeout.

## Phase 1 — Provider abstraction (`src/ImgForge.Core/RandomBackgrounds/`)

- [ ] `IRandomBackgroundProvider`, `RandomBackgroundImage` record
- [ ] `PicsumProvider(HttpClient)`: builds the URL, checks the status and the `image/*` content type, and
      returns the bytes
- [ ] `RandomBackgroundResolver`: per-provider timeout, temp file write, extension from the media type,
      `ImageLoadException` with a provider-specific message

## Phase 2 — Wire into rendering

- [ ] `ImageGenerator`: resolve before render, substitute the temp path, delete it in `finally`. Add an
      internal `RandomBackgrounds` property so tests can inject a resolver.
- [ ] `TemplateRenderer`: remove `RandomBackgroundUrl`, throw for unresolved `random`
- [ ] `ResourceLoadFailures`: remove `RandomBackground` source and constant
- [ ] `ImageGenerator.BuildTimeoutMessage`: remove random/picsum branch and constant
- [ ] Update `--bg` help text, the README (options table and the template `bg` variable row), and the docs site
      where they say picsum's URL is handed to the template

## Phase 3 — Automated tests (`tests/ImgForge.Tests`)

- [ ] Unit `PicsumProviderTests`: URL (`https://picsum.photos/{w}/{h}`), success returns bytes and media type,
      non-2xx and non-image throw
- [ ] Unit `RandomBackgroundResolverTests`: writes a temp file with the correct extension and contents. HTTP
      522, network error, non-image and timeout each give an `ImageLoadException` that names `picsum.photos`
      and the reason within the timeout. Caller cancellation propagates as `OperationCanceledException`.
- [ ] Unit `TemplateRendererTests`: an unresolved `random` throws
- [ ] Update `ResourceLoadFailuresTests` and `ImageGeneratorTimeoutMessageTests` to drop the removed
      random/picsum cases (they no longer apply)
- [ ] Integration (`Category=Integration`): `GenerateAsync` with `--bg random` and a fake provider produces a
      PNG and deletes the temp background. A failing provider throws `ImageLoadException`, writes no PNG, and
      doesn't launch the page.

## Phase 4 — Independent review

- [ ] A separate agent with fresh context reviews the full diff against #24: correctness, missed requirements,
      test gaps, regressions, security (temp file handling, URL/content handling), performance and
      accessibility
- [ ] Address all significant findings, and record any skipped findings with the reason below

## Phase 5 — Final verification

- [ ] `dotnet BuildTestFormat.cs` passes (build, all tests, `dotnet format --verify-no-changes`)

## Review notes

_(filled in after Phase 4)_
