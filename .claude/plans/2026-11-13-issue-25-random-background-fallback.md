# Issue #25 — Fall back through multiple random background providers in order

Issue: https://github.com/ardalis/ImgForge/issues/25
Parent: #23. Builds on #24 (`IRandomBackgroundProvider`, `RandomBackgroundResolver`, Picsum). Providers that will
plug into this chain: #26 (LoremFlickr), #27 (imagecdn), #28 (placeholder). CLI selection/order: #29.

## Summary

`RandomBackgroundResolver` wraps a single provider today. Make it take an ordered list, try each in turn, and
return the first image that downloads successfully. A failure (non-2xx such as 522, no response within ~5s,
non-image content, network error, download over the per-provider cap) moves on to the next provider. If every
provider fails, the error lists each provider tried and why it failed.

## Decisions / assumptions

- **Resolver takes `IReadOnlyList<IRandomBackgroundProvider>`** (at least one, no nulls). The default list is still
  just `PicsumProvider` — adding more providers to the default order is the job of #26–#28 once they exist and are
  verified to be reachable.
- **Per-provider timeouts:** each provider keeps its ~5s "start responding" timeout (from #24, enforced by
  `HttpImageDownloader`), and the resolver's 30s `DownloadTimeout` now applies **per provider attempt** rather than
  to the whole chain. A dead provider costs ~5s, not the whole budget. A provider that responds but trickles its
  body is cut off at 30s and the next one is tried.
- **Caller cancellation** stops the chain immediately and propagates `OperationCanceledException` (it is not a
  provider failure).
- **Unexpected exception types** (anything other than `RandomBackgroundProviderException`, `HttpRequestException`,
  or our own timeout) still propagate — they are bugs, not provider outages.
- **Error message:**
  - One provider configured (today's default): unchanged from #24, e.g.
    `Could not load a random background image from picsum.photos (used by --bg random): the site returned HTTP 522.`
  - Several providers: `Could not load a random background image for --bg random. Every provider failed:` followed
    by one `  - <name>: <reason>` line per provider in the order tried, then the same "You can:" suggestions.
- **"Which provider was used" logging:** there is no verbose flag in the CLI. Instead, `ResolveToFileAsync` returns a
  `ResolvedRandomBackground` (path, provider name, earlier failures). When an earlier provider failed and a later
  one succeeded, `ImageGenerator` writes one line to its `Warnings` writer (stderr), e.g.
  `Warning: picsum.photos failed (the site returned HTTP 522); used loremflickr.com for --bg random instead.`
  Nothing is printed when the first provider succeeds. A general `--verbose` flag is out of scope.

## Phase 1 — Fallback resolver (`src/ImgForge.Core/RandomBackgrounds/RandomBackgroundResolver.cs`)

- [x] Constructor takes an ordered provider list (params / `IEnumerable`), validates non-empty and no nulls;
      default constructor = `[new PicsumProvider()]`; expose `Providers`
- [x] `FetchAsync` loops providers: per-attempt linked CTS with `DownloadTimeout`; known failures recorded as
      `RandomBackgroundProviderFailure(Name, Reason)` and the loop continues; caller cancellation propagates
- [x] `ResolveToFileAsync` returns `ResolvedRandomBackground(Path, ProviderName, Failures)`
- [x] Aggregated error message when every provider fails (single-provider wording unchanged)

## Phase 2 — Wire into `ImageGenerator`

- [x] Use the returned path; write the fallback warning to `Warnings` when `Failures` is non-empty
- [x] Update README / docs wording: `random` tries the configured providers in order (currently picsum.photos),
      each with its own 5s/30s timeouts

## Phase 3 — Automated tests (`tests/ImgForge.Tests`, unit, no network)

- [x] First provider succeeds → its image is used, later providers never called, no failures reported
- [x] First fails with HTTP 522 (through a real `PicsumProvider` + `FakeHttpMessageHandler`) → second used, failure recorded
- [x] First times out (no response) → second used, quickly (well under the overall budget)
- [x] First exceeds per-provider download timeout → second still gets its own full attempt
- [x] All fail → `ImageLoadException` whose message names every provider and its reason, in order; no temp file left
- [x] Caller cancellation during first provider → `OperationCanceledException`, second never called
- [x] Constructor rejects empty list / null provider
- [x] Existing single-provider tests keep passing unchanged (message wording preserved); default-config test updated
- [x] `ImageGenerator` (fake providers, integration category): fallback writes the warning and still produces a PNG;
      no warning when the first provider succeeds

## Phase 4 — Independent review

- [ ] A separate agent with fresh context reviews the full diff against #25: correctness, missed requirements, test
      gaps, regressions, security, performance, accessibility
- [ ] Address all significant findings; record skipped ones with reasons below

## Phase 5 — Final verification

- [ ] `dotnet BuildTestFormat.cs` passes (build, all tests, `dotnet format --verify-no-changes`)

## Review notes

_(filled in after Phase 4)_
