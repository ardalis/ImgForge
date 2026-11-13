# Issue #19 — Better Error Message on Timeout

Issue: https://github.com/ardalis/ImgForge/issues/19

## Summary

`imgforge generate ... --bg random` fails with the raw PuppeteerSharp message
`Error: Navigation timeout of 30000 ms exceeded` when picsum.photos is unreachable or slow.
"Navigation" is an internal detail: `ImageGenerator` loads the rendered HTML with
`page.GoToAsync(..., WaitUntil = Networkidle0)`, which waits for every resource the page references,
including the remote background image. Replace that message with one that explains what timed out and
what to do about it.

## Assumptions / decisions

- Catch PuppeteerSharp's `NavigationException` (the timeout exception type thrown by `GoToAsync`) in
  `ImageGenerator` and rethrow a new `ImageGenerationTimeoutException` with a friendly message. Keep
  the original as `InnerException`. `GenerateCommand` already prints `Error: {ex.Message}`, so the CLI
  needs no changes.
- The message depends on the background:
  - `--bg random`: name picsum.photos and say it is unreachable or slow. Suggest trying again later,
    passing a different `--bg` (a local file or another URL), or leaving out `--bg` to use the template's
    own background design.
  - `--bg http(s)://host/...`: name the host and give the same advice.
  - Otherwise (local/no background): give a generic message saying a remote resource referenced by the
    template (image, font, stylesheet) did not finish loading, and ask the user to check their connection
    and the template's external URLs.
- Message building is a pure `internal static` method so it can be unit-tested without a browser.
- The navigation timeout becomes an `internal` settable property (default 30 s, same as today) so an
  integration test can use a short timeout against a local server that never responds.

## Phase 1 — Implementation (`src/ImgForge.Core`)

- [x] Add `ImageGenerationTimeoutException`.
- [x] Add `ImageGenerator.BuildTimeoutMessage(GenerateOptions, TimeSpan)`.
- [x] Wrap `GoToAsync` in a `catch (NavigationException)` and set an explicit timeout.

## Phase 2 — Tests (`tests/ImgForge.Tests`)

- [x] Unit tests (`ImageGeneratorTimeoutMessageTests`): random bg names picsum.photos and suggests
      alternatives; URL bg names the host; no/local bg yields the generic message; timeout seconds shown.
- [x] Integration test (`ImageGeneratorTests`, `Category=Integration`): template whose background points to a
      local `TcpListener` that accepts connections and never responds. With a short timeout,
      `GenerateAsync` throws `ImageGenerationTimeoutException` with the host in the message and a
      `NavigationException` inner exception.

## Phase 3 — Docs

- [x] Mention the picsum.photos dependency and the timeout behavior where `--bg random` is documented.

## Phase 4 — Independent review

- [x] Fresh-context review agent reviews the full diff (correctness, security, performance, accessibility of
      the CLI output).
- [x] Address all significant findings (record any skipped ones here).

## Phase 5 — Verification

- [x] `dotnet build && dotnet test && dotnet format --verify-no-changes` passes.

## Review findings and resolution

Fresh-context review found no blocking issues. Findings:

- **Wrong site could be blamed** (a slow `--headshot`/`--overlay`/template URL would be reported as picsum.photos):
  fixed. `ImageGenerator` now tracks in-flight requests (`Request`/`RequestFinished`/`RequestFailed`) and, on
  timeout, names the hosts still pending (the original host is used for redirects, so picsum.photos is named
  rather than its CDN). The `--bg`-specific advice is used only when the background host is the only one pending.
- **Culture-dependent duration** (`2,5` on de-DE): fixed with `CultureInfo.InvariantCulture`, plus a test.
- **Long single-line message**: fixed. The cause is on the first line and the suggestions are a bulleted list.
- **Test accept loop could throw during cleanup**: fixed (`catch (Exception) when (cts.IsCancellationRequested)`).
- **README wording** ("remote images" vs. whole page): fixed.
- **Not addressed, deferred to a follow-up:** when picsum.photos fails *immediately* (DNS failure, connection
  refused, offline), Chromium reports the image as failed rather than hanging, so no timeout occurs and the image
  is generated without a background. This issue is specifically about the timeout message. Warning about or
  failing on failed background requests changes behavior and belongs in its own issue.
- **Not addressed:** `LoadTimeout <= 0` would mean "wait forever" in Puppeteer. The property is `internal` and only
  tests set it, so there is no user exposure.

## Verification

- `dotnet build`: 0 warnings, 0 errors. `dotnet test`: 69/69 passed. `dotnet format --verify-no-changes`: clean.
- Manual CLI check against a local server that accepts connections but never responds:

  ```
  Error: Timed out after 30 seconds waiting for the background image from 127.0.0.1 to load.
  The site may be unreachable or responding slowly. You can:
    - Try again later.
    - Pass a different background with --bg (a local image file or another URL).
    - Omit --bg to use the template's built-in background design.
  ```
