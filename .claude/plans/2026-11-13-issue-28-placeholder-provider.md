# Issue #28 — Add solid-color placeholder provider as last-resort fallback

Issue: https://github.com/ardalis/ImgForge/issues/28
Parent: #23. Builds on #24 (`IRandomBackgroundProvider`, `HttpImageDownloader`) and #25 (ordered fallback chain).
CLI selection of providers / configurable order is #29.

## Summary

Add `PlaceholderProvider`, which downloads a solid-color PNG from placehold.co, and append it to the default
provider order after Picsum so `--bg random` almost always yields *something* (with the existing fallback warning).

## Decisions / assumptions

- **Service:** placehold.co (`https://placehold.co/{w}x{h}/{color}/{color}/png`). Text color = background color so
  the dimension label placehold.co always draws is invisible. Checked from a real machine on 2026-11-13: HTTP 200,
  `image/png`, under 1 s; a decoded 1280x720 image has 3 distinct pixel values all within ±1 of the requested color
  (anti-aliasing of the hidden label), i.e. visually solid. `?text=%20` does not remove the label, and omitting the
  text-color segment makes `png` be read as a color and returns SVG — so the explicit two-color form is required.
- **dummyimage.com** (also up, 2026-11-13) is not added; one placeholder service is enough as the last resort, and
  #29 can add more if wanted.
- **Size:** placehold.co clamps each dimension to 4000 px. That's fine for a solid color (template backgrounds use
  `cover`); no client-side clamping needed.
- **Color:** `Color` property, 6-digit hex (`#` prefix allowed), validated so it can't alter the URL; stored
  lower-case without `#`. Default `1e293b` (dark slate), because all built-in templates draw white text.
- **Default order:** `Picsum → placehold.co`. LoremFlickr stays out (HTTP 500 per #26).
- **Offline fallback (issue task "consider"):** generating a solid/gradient PNG locally would be fully offline and
  can't fail. It's preferable as the *final* step after placehold.co, not instead of it, but it's a separate change
  (PNG encoding without an image library, and a resolver path that doesn't need HTTP). Deferred: recommended as a
  follow-up issue; noted in a comment on #28.

## Phase 1 — Provider (`src/ImgForge.Core/RandomBackgrounds/PlaceholderProvider.cs`)

- [x] `PlaceholderProvider(HttpClient)` + parameterless ctor using `HttpImageDownloader.SharedClient`
- [x] `Name` (`placehold.co`), `ResponseTimeout`, `Color` (default `1e293b`, validated)
- [x] `ImageUrl(width, height)`; `FetchAsync` delegates to `HttpImageDownloader`

## Phase 2 — Default order

- [x] `RandomBackgroundResolver()` uses `PicsumProvider, PlaceholderProvider`

## Phase 3 — Automated tests (unit, fake `HttpMessageHandler`, `tests/ImgForge.Tests`)

- [x] `PlaceholderProviderTests`: URL for size and default color, custom color (with `#`, upper-case), invalid colors
      rejected, success returns bytes/media type, HTTP error and non-image → `RandomBackgroundProviderException`
- [x] Resolver falls back from a failing provider to `PlaceholderProvider`
- [x] Update default-resolver test for the new order and timeouts

## Phase 4 — Docs

- [x] README, docs site, and `--bg` help text: random uses picsum.photos, falling back to a solid-color placeholder
      from placehold.co

## Phase 5 — Independent review

- [ ] Separate agent with fresh context reviews the full diff against #28 (correctness, missed requirements, test
      gaps, regressions, security, performance, accessibility)
- [ ] Address all significant findings; record skipped ones below

## Phase 6 — Final verification

- [ ] `dotnet BuildTestFormat.cs` passes (build, all tests, `dotnet format --verify-no-changes`)

## Review notes

(pending)
