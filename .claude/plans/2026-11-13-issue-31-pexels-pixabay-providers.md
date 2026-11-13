# Issue #31 — Optional Pexels and Pixabay API providers

Issue: https://github.com/ardalis/ImgForge/issues/31
Parent: #23. Builds on #30 (`UnsplashProvider`: key-from-env pattern, attribution on stderr, `random:<provider>` that
needs a key).

## Summary

Add `PexelsProvider` (`PEXELS_API_KEY`) and `PixabayProvider` (`PIXABAY_API_KEY`). Each is turned on only when its key
is set. Same two-step flow as Unsplash: call the JSON API, then download the image with `HttpImageDownloader`.

## Decisions / assumptions

- **Default order** (each keyed provider only when its key is set): Unsplash → Pexels → Pixabay → picsum.photos →
  placehold.co. Keyed providers give better photos, so they go first, in the order they were added.
- **Selection:** `--bg random:pexels` and `--bg random:pixabay` (no option). Selecting one without its key fails right
  away, naming the environment variable and the sign-up page (same as `random:unsplash`).
- **Pexels** (`pexels.com`): no random endpoint, so `GET https://api.pexels.com/v1/curated?per_page=1&page=<random 1..1000>`.
  If that page is empty but `total_results` > 0 (the curated list is shorter than expected), retry once with a random
  page within `total_results`. Key sent only as the raw `Authorization` header (Pexels' scheme) to `api.pexels.com`.
  Image: `src.original` (must be `https://images.pexels.com/...`) + `auto=compress&cs=tinysrgb&fit=crop&w=&h=`, so
  Pexels crops to size. Attribution (Pexels guidelines): `Photo by <name> (<photographer_url>) on Pexels (https://www.pexels.com)`;
  the profile link is only shown when it is on `pexels.com` / `www.pexels.com`.
- **Pixabay** (`pixabay.com`): `GET https://pixabay.com/api/?key=<key>&image_type=photo&orientation=<horizontal|vertical|all>&safesearch=true&per_page=20&page=<random 1..10>`
  (every query is capped at 500 hits, so 200 photos is safely in range) and pick a random hit. Pixabay requires
  the key as a query parameter: it is escaped, sent only to `https://pixabay.com/api/`, and never appears in messages
  (messages never include URLs). Image: `largeImageURL` (≤1280 px; the template scales with `background-size: cover`),
  falling back to `webformatURL`; host must be `pixabay.com` or `cdn.pixabay.com`. ImgForge downloads the image to a
  temp file, so it never hotlinks (Pixabay's terms forbid permanent hotlinking). Attribution (not required but
  appreciated): `Photo by <user> on Pixabay (<pageURL>)`, with `pageURL` only when on `pixabay.com`.
- **Pixabay 24 h caching rule:** each CLI run makes one request; not caching results across runs is an accepted gap
  (no on-disk cache), noted in the README and docs.
- **Errors (fall through to next provider):** Pexels 401/403 → key rejected; Pixabay 400/401/403 → key rejected
  (Pixabay answers a bad key with 400); 429 → rate limit (with "limit reached" wording when `X-Ratelimit-Remaining: 0`);
  other non-success → HTTP status; malformed JSON / missing fields / wrong image host / no photos → "unexpected response"
  or "returned no photos".
- **Shared code:** extract the JSON-API plumbing from `UnsplashProvider` (send with response timeout, size-capped JSON
  read, `GetString` path helper, https URL check, printable-name cleanup, rate-limit header check) into an internal
  `ApiProviderHelpers` class; Unsplash keeps its behavior and tests.
- **Spec key lookup:** `RandomBackgroundSpec.Parse(background, Func<string, string?> environmentVariable)` replaces the
  Unsplash-only `Func<string?>` test seam; the key is held privately so it never shows in `ToString`.
- **Randomness:** each provider has an internal `Func<int, int> NextRandom` (default `Random.Shared.Next`) so tests can
  pin the page / hit.

## Phase 1 — Shared helpers

- [x] `ApiProviderHelpers` with the JSON plumbing; refactor `UnsplashProvider` onto it (no behavior change)

## Phase 2 — Providers and wiring

- [x] `PexelsProvider` (`src/ImgForge.Core/RandomBackgrounds/PexelsProvider.cs`)
- [x] `PixabayProvider` (`src/ImgForge.Core/RandomBackgrounds/PixabayProvider.cs`)
- [x] `RandomBackgroundResolver.DefaultProviders(unsplash, pexels, pixabay)` + default ctor reads all three keys
- [x] `RandomBackgroundSpec`: `pexels`, `pixabay` names, key checks, `CreateProvider`; generic key lookup
- [x] `--bg` help text

## Phase 3 — Automated tests (`tests/ImgForge.Tests`, unit, fake `HttpMessageHandler`)

- [x] Pexels: request URL + raw `Authorization` header + `Accept: application/json`; sized image URL; attribution;
      empty page retry within `total_results`; 401/429/500 messages without the key; malformed JSON / wrong image
      host / no photos; API timeout; resolver fall-through and attribution
- [x] Pixabay: request URL (escaped key, orientation by size, random page) and random hit; image URL fallback;
      attribution; 400/429/500 messages without the key; malformed JSON / wrong host / no hits; key never in any
      message; resolver fall-through
- [x] Default order with each combination of keys; spec parse/create for `random:pexels` / `random:pixabay` with and
      without keys and with an option
- [x] Existing Unsplash tests still pass after the refactor

## Phase 4 — Docs

- [x] README and docs site: `PEXELS_API_KEY`, `PIXABAY_API_KEY`, default order, `random:pexels`, `random:pixabay`,
      attribution and terms

## Phase 5 — Independent review

- [x] Separate agent (fresh context) reviews the full diff vs. #31: correctness, missed requirements, test gaps,
      regressions, security (key leakage, especially Pixabay's query-string key), performance, accessibility
- [x] Address all significant findings; record skipped ones below

## Phase 6 — Final verification

- [x] `dotnet BuildTestFormat.cs` passes

## Review notes

A separate general-purpose agent (fresh context) reviewed the diff against #31. It found no significant problems:
the Unsplash refactor keeps its behavior, no key-leak path was found (Pixabay's key is escaped, only sent to
`pixabay.com/api/`, and no message includes a URL; the Pexels key goes only to `api.pexels.com`), and the API facts in
this plan checked out. Low-severity findings:

1. **Fixed:** a non-numeric Pexels `total_results` made `TryGetInt32` throw, discarding a usable photo. Now checks the
   value kind first; tested.
2. **Fixed:** the 24-hour caching rule is now mentioned in the README and docs, not just this plan.
3. **Fixed (tests):** Pexels retry page also empty; Pexels network error through the resolver (no key in the
   warning); Pixabay `largeImageURL` on a wrong host with a valid `webformatURL`; Pixabay key trimmed before escaping.
4. **Fixed:** "keyless queries" wording in this plan (the 500-hit cap applies to every query).
5. **Not changed:** every Pixabay 400 is reported as a likely key problem, though Pixabay also returns 400 for a page
   out of range. With popular photos and pages 1–10 of 20 that can't happen in practice, and the message already says
   "the request was rejected ... check that PIXABAY_API_KEY is a valid Pixabay API key" rather than asserting the key
   is wrong.
6. **Not changed:** the Pexels "403 with `X-Ratelimit-Remaining: 0`" arm is probably never hit (Pexels uses 429), but
   it is harmless and keeps the providers consistent.
7. **Not changed (optional hardening):** checking the final host after redirects. The API URLs are fixed, .NET drops
   `Authorization` on redirect and refuses https→http, so the Pexels key can't follow a redirect, and the Pixabay key
   would only move if Pixabay itself put it in a `Location` header.
