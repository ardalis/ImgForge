# Issue #26 — Add LoremFlickr random background provider

Issue: https://github.com/ardalis/ImgForge/issues/26
Parent: #23. Builds on #24 (`IRandomBackgroundProvider`, `HttpImageDownloader`) and #25 (ordered fallback chain).
CLI wiring of keyword/lock and provider selection is #29.

## Summary

Add `LoremFlickrProvider`, which downloads `https://loremflickr.com/{w}/{h}/{keyword}` (optional `?lock={n}` for a
repeatable image). No API key. Default keyword `abstract`. Keyword and lock are settable on the provider so #29 can
wire them to `--bg random:loremflickr[:keyword]`.

## Decisions / assumptions

- **Name:** `loremflickr.com` (matches the host-name convention used by `picsum.photos`).
- **Keyword:** one or more keywords separated by commas (LoremFlickr's own syntax, e.g. `city,night`). Each keyword
  may contain ASCII letters, digits, `-` and `_`. Anything else (empty, `/`, `?`, spaces, empty entries) is rejected
  with an `ArgumentException` when set, so a bad value can never change the URL path or query. Keywords are
  lower-cased for the URL. LoremFlickr's `/all` (match-all) suffix is out of scope.
- **Lock:** optional non-negative `int`; negative values are rejected. Emitted as `?lock={n}` (invariant culture).
- **Timeouts / validation / size limits:** reuse `HttpImageDownloader` exactly as `PicsumProvider` does
  (5s response timeout via `ResponseTimeout`, image-type and 50 MB checks). LoremFlickr redirects to its cache;
  the shared client follows redirects.
- **Availability check (issue task):** checked from a real machine on 2026-11-13. `https://loremflickr.com/` returns
  200, but **every image request returned HTTP 500** (`/1280/720/abstract`, `/1000/420/technology?lock=42`,
  `/320/240/dog`, with and without custom User-Agent/Accept headers). So LoremFlickr is **not added to the default
  provider order** in this change; the default stays `[PicsumProvider]`. Adding it to the default order (and the
  final order) is left to #29 after a successful availability re-check.

## Phase 1 — Provider (`src/ImgForge.Core/RandomBackgrounds/LoremFlickrProvider.cs`)

- [x] `LoremFlickrProvider(HttpClient)` + parameterless ctor using `HttpImageDownloader.SharedClient`
- [x] `Name`, `ResponseTimeout`, `Keyword` (default `abstract`, validated), `Lock` (nullable, validated)
- [x] `ImageUrl(width, height)` builds the URL; `FetchAsync` delegates to `HttpImageDownloader`

## Phase 2 — Automated tests (`tests/ImgForge.Tests/LoremFlickrProviderTests.cs`, unit, fake `HttpMessageHandler`)

- [x] URL construction: default keyword, custom keyword, comma-separated keywords, upper-case lowered, lock
- [x] Invalid keyword / negative lock rejected
- [x] Success returns bytes + media type; redirect target not relevant (handled by HttpClient)
- [x] Failure: HTTP 500 (the currently observed outage) and non-image content → `RandomBackgroundProviderException`
- [x] Works as a fallback in `RandomBackgroundResolver` after a failing provider
- [x] Default resolver order unchanged (existing test)

## Phase 3 — Docs

- [x] Mention LoremFlickr provider status where README/docs describe `--bg random` providers, if such a list exists
      — no change needed: README and the docs site say `random` currently uses picsum.photos, which stays true
      because LoremFlickr isn't in the default order and isn't selectable until #29 (which owns the docs update).

## Phase 4 — Independent review

- [x] A separate agent with fresh context reviews the full diff against #26: correctness, missed requirements, test
      gaps, regressions, security (URL injection via keyword), performance, accessibility
- [x] Address all significant findings; record skipped ones with reasons below

## Phase 5 — Final verification

- [x] `dotnet BuildTestFormat.cs` passes (build, all tests, `dotnet format --verify-no-changes`)

## Review notes

A separate general-purpose agent with fresh context reviewed `git diff origin/main...HEAD`. It found no
significant issues. It confirmed that the keyword pattern can't change the host, path or query, that the provider
matches `PicsumProvider`'s conventions, and that leaving LoremFlickr out of the default order is reasonable. It
re-checked loremflickr.com during the review and still got HTTP 500. It raised four low-severity items, and all
four were addressed:

1. **Exception parameter name was `value`:** `ArgumentException` adds `(Parameter 'value')` to its message, which
   would confuse CLI users once #29 shows the message. The exceptions now use `nameof(Keyword)` / `nameof(Lock)`,
   and the tests check the full message and `ParamName` exactly.
2. **No limit on keyword length:** a keyword is now limited to `MaxKeywordLength` (100) characters. There are
   tests for exactly 100 and for 101.
3. **Undisposed `HttpClient`s in tests:** the tests that only build URLs or check settings now share one static
   client. Its handler throws if a request is ever sent.
4. **Availability result recorded only here:** posted on #26 and #29 that LoremFlickr returned HTTP 500 on
   2026-11-13 and should be re-checked before it's added to the default order.
