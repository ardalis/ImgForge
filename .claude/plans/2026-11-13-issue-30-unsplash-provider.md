# Issue #30 — Optional Unsplash API provider via UNSPLASH_ACCESS_KEY

Issue: https://github.com/ardalis/ImgForge/issues/30
Parent: #23. Builds on #24 (`IRandomBackgroundProvider`, `HttpImageDownloader`), #25 (fallback chain), #29
(`--bg random:<provider>`).

## Summary

Add `UnsplashProvider`, enabled only when `UNSPLASH_ACCESS_KEY` is set. Two-step flow: call
`GET https://api.unsplash.com/photos/random` (JSON), download `urls.raw` sized with `w`/`h`/`fit=crop`, and trigger
`links.download_location` as the Unsplash API guidelines require. When the key is set, Unsplash goes first in the
default order (Unsplash → Picsum → placehold.co).

## Decisions / assumptions

- **Name:** `unsplash.com`. Also selectable as `--bg random:unsplash` (no option). Selecting it without the key fails
  right away with a message telling the user to set `UNSPLASH_ACCESS_KEY`.
- **Key handling:** read from the environment once per resolver / spec; whitespace-only counts as unset. Sent only
  as `Authorization: Client-ID <key>` to `https://api.unsplash.com`. Never in messages, URLs, or `ToString`.
  `download_location` is only called when it is an `https://api.unsplash.com/` URL, so the key can't be sent to
  another host. The image URL must be `https://images.unsplash.com/...` (no key is sent there).
- **Request:** `orientation` from the size (`landscape` / `portrait` / `squarish`), `content_filter=high`,
  `Accept-Version: v1`, `Accept: application/json`. JSON body capped at 1 MB; 5-second response timeout like other
  providers.
- **Image:** `urls.raw` + `w`, `h`, `fit=crop`, `fm=jpg`, `q=80`, downloaded via `HttpImageDownloader`.
- **Errors (fall through to next provider):** 401 → key rejected; 403 → access denied / rate limit; 429 → rate
  limit; other non-success → HTTP status; malformed JSON / missing fields → "unexpected response".
- **download_location:** triggered after the image downloads successfully; failure there is ignored (best effort,
  short timeout) so it can't fail the image.
- **Attribution:** "verbose output" doesn't exist in the CLI, so attribution is always printed to stderr when the
  image came from Unsplash: `Photo by <name> (<profile url>) on Unsplash (<unsplash url>)` with
  `utm_source=imgforge&utm_medium=referral`. stdout stays just the output path. Carried via
  `RandomBackgroundImage.Attribution` → `ResolvedRandomBackground.Attribution`.

## Phase 1 — Provider and wiring

- [x] `UnsplashProvider` (`src/ImgForge.Core/RandomBackgrounds/UnsplashProvider.cs`)
- [x] `RandomBackgroundImage.Attribution` + `ResolvedRandomBackground.Attribution`
- [x] Default order: Unsplash first when `UNSPLASH_ACCESS_KEY` set (`RandomBackgroundResolver.DefaultProviders`)
- [x] `RandomBackgroundSpec`: `unsplash` provider name, key check, `CreateProvider`
- [x] `ImageGenerator` writes attribution to stderr
- [x] `--bg` help text

## Phase 2 — Automated tests (`tests/ImgForge.Tests`, unit, fake `HttpMessageHandler`)

- [x] JSON parse + image download URL + auth headers + orientation
- [x] download_location triggered (and only for api.unsplash.com); its failure ignored
- [x] 401 / 403 / 429 / 500 → clear `RandomBackgroundProviderException` without the key; falls through in resolver
- [x] Malformed JSON / missing fields / non-Unsplash image host rejected
- [x] Default order with and without key; spec parse/create for `random:unsplash` with/without key
- [x] Attribution written to warnings by `ImageGenerator`/resolver

## Phase 3 — Docs

- [x] README and docs site: `UNSPLASH_ACCESS_KEY`, default order, `random:unsplash`, attribution

## Phase 4 — Independent review

- [ ] Separate agent (fresh context) reviews the full diff vs. #30: correctness, missed requirements, test gaps,
      regressions, security (key leakage), performance, accessibility
- [ ] Address all significant findings; record skipped ones below

## Phase 5 — Final verification

- [ ] `dotnet BuildTestFormat.cs` passes

## Review notes

_(pending)_
