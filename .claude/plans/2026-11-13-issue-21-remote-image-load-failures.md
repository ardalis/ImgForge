# Issue #21 — Warn or fail when a remote background image fails to load immediately

Issue: https://github.com/ardalis/ImgForge/issues/21
Follow-up to #19 / PR #20 (timeout message, merged).

## Summary

If a remote image fails right away (offline, DNS failure, connection refused, HTTP 4xx/5xx), Chromium marks
the request as failed, the page settles, and ImgForge silently writes a PNG without that image. Report it
instead.

## Decisions (the issue left these open)

- **Images the user asked for fail the run (option 1).** A failed remote `--bg` (including `random`),
  `--headshot` or `--overlay` image throws `ImageLoadException`. The CLI prints `Error: ...` and exits with code 1,
  and **no PNG is written**. The user asked for that image and didn't get it, which matches the timeout
  behavior from #20. No opt-out flag for now. Dropping or replacing the option is the workaround, and a flag
  can be added later if needed.
- **Other remote resources referenced by the template only warn.** These are fonts, stylesheets, or images
  hard-coded in the template. Each failure is written to stderr as `Warning: ...` and the PNG is still written.
  The template author chose those resources, and failing on, say, a decorative web font would be too strict.
- **What counts as a failure:** for http(s) URLs only, `page.RequestFailed` (network errors such as
  `net::ERR_CONNECTION_REFUSED`) or `page.RequestFinished` with a response status >= 400. Local `file:///`
  resources are out of scope: the issue is about remote sources, and overlays with relative paths have
  pre-existing semantics.
- **Matching a failed request to an option:** compare the request's original URL (first entry of
  `RedirectChain`, else `Url`) with the URLs the template renderer produces. `random` →
  `https://picsum.photos/{w}/{h}`. Headshot and overlay http(s) URLs pass through unchanged. Both sides are
  normalized with `Uri.AbsoluteUri`.
- **Timeout takes precedence.** If loading times out, the #20 `ImageGenerationTimeoutException` is thrown as
  before.
- **Message format** follows #20: a cause line per failure naming the option, host and reason
  (`net::ERR_NAME_NOT_RESOLVED` or `HTTP 404`), then a "You can:" list of suggestions tailored to the options
  involved.
- Warnings go through an internal `TextWriter Warnings` property (default `Console.Error`) so tests can capture
  them. `ImageGenerator` already writes the Chromium download notice to stderr.

## Phase 1 — Implementation (`src/ImgForge.Core`)

- [x] Add `ImageLoadException`.
- [x] Expose the background URL the renderer produces for `random` (shared helper) so both sides agree.
- [x] In `ImageGenerator`, collect failed http(s) requests (failure text or HTTP status) during navigation.
- [x] After navigation, map failures to `--bg`/`--headshot`/`--overlay`. Throw `ImageLoadException` before taking
      the screenshot if any match. Warn for the rest.
- [x] `BuildLoadFailureMessage(...)` and `BuildTemplateResourceWarning(...)` as pure internal static methods.

## Phase 2 — Tests (`tests/ImgForge.Tests`)

- [x] Unit (`ImageGeneratorLoadFailureMessageTests`):
  - random bg names picsum.photos with `--bg` advice
  - URL bg names the host
  - headshot and overlay get their own advice
  - multiple failures are listed
  - HTTP status is shown as `HTTP 404`
  - reason wording (ORB, connection errors, other errors)
  - the warning text
- [x] Unit: matching the requested URL to an option (normalization, punycode host, escaped characters,
      `random` variants, non-matching URL → template resource).
- [x] Integration (`ImageGeneratorTests`, `Category=Integration`), using local loopback servers:
  - [x] `--bg` pointing at a closed port (connection refused) → `ImageLoadException` naming `--bg`/host, no PNG.
  - [x] `--bg` pointing at a server returning 404 → `ImageLoadException` mentioning `HTTP 404`, no PNG.
  - [x] `--bg` returning 404 with an HTML body (ORB-blocked) → "responded, but not with an image" wording.
  - [x] `--bg` that 302-redirects to an unreachable host → attributed to `--bg` (the picsum → CDN case).
  - [x] Requested and template resources both fail → warning written, then `ImageLoadException`.
  - [x] Template with a hard-coded failing image and no `--bg` → succeeds, PNG written, warning captured.
  - [x] No remote resources → no warnings.
- [x] Existing success tests still pass (no regression on the normal path).

## Phase 3 — Docs

- [x] README and usage docs: remote `--bg`/`--headshot`/`--overlay` images that fail to load stop the run; failed
      template resources produce a warning.

## Phase 4 — Independent review

- [x] Fresh-context review agent reviews the full diff (correctness, security, performance, accessibility and
      clarity of the CLI output).
- [x] Address all significant findings (record any skipped ones here).

## Phase 5 — Verification

- [x] `dotnet BuildTestFormat.cs` passes cleanly.

## Review findings and resolution

Fresh-context review found no blocking defects. Findings:

- **Real HTTP error pages were reported as "could not be reached"** (medium): Chromium's ORB blocks an
  HTML/JSON error body sent for an image request, so it arrives as `RequestFailed` with
  `net::ERR_BLOCKED_BY_ORB` and no response. Fixed the wording: ORB now reads "the site responded, but not
  with an image; it may have returned an error page". "Could not be reached" is reserved for name,
  connection, internet, proxy, SSL and certificate errors. Other errors read "the request failed (...)". I tried
  recording the status from `page.Response`, but PuppeteerSharp never sees the response for ORB-blocked
  requests (verified empirically), so I removed it. Covered by a new integration test with a `text/html` 404.
- **Redirect-origin matching untested** (medium): added an integration test where `--bg` 302-redirects to an
  unreachable host, as picsum.photos redirects to its CDN.
- **IDN and escaping mismatches turned a requested image into a warning** (low): matching now uses a key built
  from scheme, `IdnHost` (punycode), port, and the unescaped path and query. Unit tests cover both cases.
- **`random` trimmed inconsistently** (low): a single `TemplateRenderer.IsRandomBackground` helper is now used
  by the renderer, the failure classifier, and the timeout message. The renderer now also accepts `" random "`.
- **Template warnings lost when the run also fails** (low): warnings are now written before the exception is
  thrown. Covered by an integration test.
- **Not addressed: secrets in URL paths** (low): warnings already strip credentials, query and fragment, and
  error messages show only the host. A token embedded in a template's URL path is still printed. That URL comes
  from the template author's own file, and truncating paths would make warnings less useful.
- **Not addressed: closed-port race in tests** (low): another process could grab the freed loopback port
  between `Stop()` and Chromium connecting. Extremely unlikely, and the integration suite passed in 3 repeated runs.

## Verification

- `dotnet BuildTestFormat.cs`: build clean (0 warnings), 95/95 tests passed, format clean.
- Manual CLI runs:
  - `--bg https://no-such-host.invalid/bg.jpg`: prints the error, exits 1, no PNG.
  - Template with an unreachable `<img>`: prints a warning, exits 0, PNG written.
  - `--bg` and `--headshot` both set to `https://github.com/ardalis.png` (302 → avatars CDN): renders
    correctly with no warning, so there is no false positive on redirects.
  - `--bg random` at the time of testing: picsum.photos itself was down (curl: no response after 20 s,
    Cloudflare 522 after 39 s), and the #19 timeout message was shown as expected.

## CI fix: concurrent first-run Chromium download

The first CI run failed: 7 integration tests threw `PuppeteerException: Failed to download Chrome` wrapping
`IOException ... chrome-linux64.zip ... being used by another process`. xUnit runs test classes in parallel, and on a
fresh runner `ImageGeneratorTests` and the new `ImageGeneratorLoadFailureTests` both started the first-run Chromium
download into the same zip. This is a real library bug (any concurrent `GenerateAsync` calls on a fresh machine hit it),
not just a test problem. Fixed in `ImageGenerator.EnsureBrowserAsync`:

- [x] Serialize the download with a static `SemaphoreSlim`, and skip the check after the first success.
- [x] Check for the exact Chrome build Puppeteer launches (`SupportedBrowser.Chrome` + `Chrome.DefaultBuildId`)
      instead of `GetInstalledBrowsers().Any()`. Before, a leftover headless shell or an older Chrome build from a
      previous PuppeteerSharp version suppressed the download, and the launch then failed with "cannot find the file".
- [x] Reproduced locally by deleting the cached browsers: without the fix, 3 tests failed with the same IOException;
      with the fix, 95/95 passed from a fresh state. `dotnet BuildTestFormat.cs` is clean.
