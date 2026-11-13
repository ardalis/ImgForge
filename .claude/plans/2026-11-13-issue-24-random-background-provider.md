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
- **`RandomBackgroundResolver`** wraps a single provider (just Picsum for now). Fallback across several
  providers is **#25**, which will turn this into a list. It writes the image to a temp file with
  `FileMode.CreateNew` and an extension that matches the media type, and returns the path.
- **Two timeouts (revised after review):** the provider has **5s to start responding** (headers), which is the
  fast-fail the issue asks for. The resolver caps the **whole download at 30s**, so a large image on a slow
  connection isn't cut off. The two have different messages: "the site did not respond within 5 seconds" and
  "the image did not finish downloading within 30 seconds".
- **Accepted content types:** only `image/jpeg`, `png`, `webp`, `gif` and `avif`. SVG, HTML and other types
  are rejected with a clear message instead of silently rendering no background. The body is capped at 50 MB.
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
- **HttpClient:** one static instance for the default Picsum provider (a `SocketsHttpHandler` with a 5-minute
  `PooledConnectionLifetime`), so sockets aren't exhausted and DNS changes are picked up. Redirects are
  followed (Picsum redirects to its CDN). The User-Agent is `ImgForge`. Timeouts come from linked
  `CancellationTokenSource`s, not from `HttpClient.Timeout`, so "timed out" can be told apart from "caller
  cancelled".
- **Local file URIs** are now built with `new Uri(path).AbsoluteUri`, with `'` escaped as `%27`. Before, a temp
  or user path containing `'`, `#`, `%` or a space produced a broken `url('...')` in the built-in templates.
  This also affects `--bg` and `--headshot` local paths, and fixes them.
- **Template checked first:** `GenerateAsync` checks that the template loads before downloading, so a bad
  `--template` is reported right away instead of after a network call.
- **Tests:** these use a fake `HttpMessageHandler`, with no live network. The timeout test injects a short
  timeout.

## Phase 1 — Provider abstraction (`src/ImgForge.Core/RandomBackgrounds/`)

- [x] `IRandomBackgroundProvider`, `RandomBackgroundImage` record
- [x] `PicsumProvider(HttpClient)`: builds the URL, checks the status and the `image/*` content type, and
      returns the bytes
- [x] `RandomBackgroundResolver`: per-provider timeout, temp file write, extension from the media type,
      `ImageLoadException` with a provider-specific message

## Phase 2 — Wire into rendering

- [x] `ImageGenerator`: resolve before render, substitute the temp path, delete it in `finally`. Add an
      internal `RandomBackgrounds` property so tests can inject a resolver.
- [x] `TemplateRenderer`: remove `RandomBackgroundUrl`, throw for unresolved `random`
- [x] `ResourceLoadFailures`: remove `RandomBackground` source and constant
- [x] `ImageGenerator.BuildTimeoutMessage`: remove random/picsum branch and constant
- [x] Update `--bg` help text, the README (options table and the template `bg` variable row), and the docs site
      where they say picsum's URL is handed to the template

## Phase 3 — Automated tests (`tests/ImgForge.Tests`)

- [x] Unit `PicsumProviderTests`: URL (`https://picsum.photos/{w}/{h}`), success returns bytes and media type,
      non-2xx and non-image throw
- [x] Unit `RandomBackgroundResolverTests`: writes a temp file with the correct extension and contents. HTTP
      522, network error, non-image and timeout each give an `ImageLoadException` that names `picsum.photos`
      and the reason within the timeout. Caller cancellation propagates as `OperationCanceledException`.
- [x] Unit `TemplateRendererTests`: an unresolved `random` throws
- [x] Update `ResourceLoadFailuresTests` and `ImageGeneratorTimeoutMessageTests` to drop the removed
      random/picsum cases (they no longer apply)
- [x] Integration (`Category=Integration`): `GenerateAsync` with `--bg random` and a fake provider produces a
      PNG and deletes the temp background. A failing provider throws `ImageLoadException`, writes no PNG, and
      doesn't launch the page.

## Phase 4 — Independent review

- [x] A separate agent with fresh context reviews the full diff against #24: correctness, missed requirements,
      test gaps, regressions, security (temp file handling, URL/content handling), performance and
      accessibility
- [x] Address all significant findings, and record any skipped findings with the reason below

## Phase 5 — Final verification

- [x] `dotnet BuildTestFormat.cs` passes (build, all tests, `dotnet format --verify-no-changes`)

## Review notes

A separate general-purpose agent with fresh context reviewed `git diff main...HEAD`. It found no blocking issues.

Addressed:
1. **File URI escaping (medium):** temp paths containing `'`, `#` or `%` broke `url('...')`. Fixed in
   `TemplateRenderer.ResolveLocalPath`. The renderer test and the end-to-end integration test use a folder
   named `O'Brien #1 50%`, and mutation-checking against the old code makes both tests fail.
2. **Flaky temp-file assertion (medium):** the test that snapshotted the shared temp folder now uses its own
   `TempDirectory` and asserts that the folder is empty afterwards.
3. **Timeout covered the whole body, and the message said "did not respond" (medium):** split into a 5s
   response timeout and a 30s overall download timeout, each with its own message and tests (including one
   where the body is slower than the response timeout).
4. **A failed delete in `finally` could hide the real error:** now best-effort `TryDelete`.
5. **The image was downloaded before the template was checked:** the template is now checked first, and a test
   asserts that the provider is never called.
6. **Any `image/*` was trusted:** restricted to raster types Chromium can draw.
8. **Temp file creation:** now `FileMode.CreateNew`.
9. **DNS staleness:** `PooledConnectionLifetime` set on the shared handler.
10. **Untidy network error text:** now uses `GetBaseException().Message` with the trailing period trimmed.
12. **Test gaps:** added a test that the temp file is deleted when rendering fails after the download, a test
   for the default resolver configuration, and a test that nothing is left behind when the provider fails.
   Fixed the leaked `GetTempFileName` file in the renderer test. Removed the "before loading page" claim from a
   test name that didn't prove it.
13. **Docs:** fixed the inconsistent README comment, and documented the 5s and 30s timeouts accurately.

Not changed (deliberately):
- **7. The image is held in memory twice (up to about 100 MB at the cap):** acceptable for a CLI, and keeping
  `RandomBackgroundImage` as bytes keeps the provider contract simple for #26–#31. Streaming straight to disk
  can follow if large images become a concern.
- **11. `TemplateRenderer.Render` now throws for an unresolved `random`:** intended (see Decisions), and called
  out in the PR description as a behavior change for anyone using the library directly.
