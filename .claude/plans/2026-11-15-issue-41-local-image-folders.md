# Issue #41: Support Image Folder and Local Sizing

https://github.com/ardalis/ImgForge/issues/41

> Allow configuring one or more image directories, local to the machine where imgforge is running.
> Random uses the local folder as a fallback before solid colors if present. It should choose randomly from among all
> supported images in all directories provided (recursive depth search).
> Formatting the image to the desired size should be done locally in this case.

Related: #43 / PR #44 already added the `image-directories` setting (`imgforge config add image-directories <dir>`),
stored in `~/.imgforge/settings.json` but not yet read by `generate`. #42 (Dropbox) is out of scope.

## Assumptions

- **Configuration** = the existing `image-directories` user setting. No new CLI option.
- **Order**: plain `--bg random` becomes Unsplash/Pexels/Pixabay (when keyed) → picsum.photos → *local image folders*
  (only when at least one directory is configured) → placehold.co. "Fallback before solid colors" = just before the
  placeholder.
- Also add `--bg random:local` to use only the local folders (no fallback), consistent with other providers.
- **Supported images**: `.jpg`, `.jpeg`, `.png`, `.webp`, `.gif`, `.avif` (same formats as remote providers),
  case-insensitive, found recursively. Missing/unreadable directories and inaccessible subfolders are skipped. Hidden
  files are skipped. Files larger than the remote 50 MB cap or empty are skipped.
- **Local sizing**: no new imaging dependency. The chosen image is center-cropped and scaled to exactly the requested
  width × height with headless Chromium (already used to render), via `object-fit: cover`, and saved as a JPEG (PNG
  for formats that may have transparency). Behind an `IImageResizer` interface so tests can stub it.
- Settings are only read when `--bg` is random, so a broken settings file doesn't affect other runs.

## Phase 1: Core provider
- [x] `LocalImageProvider` (`IRandomBackgroundProvider`): recursive scan, dedupe, random pick, resize via `IImageResizer`.
- [x] `ChromiumImageResizer`: headless-Chromium crop/scale to width × height; fails clearly if the image can't be decoded.
- [x] Make `ImageGenerator`'s browser bootstrap shareable (`internal`).

## Phase 2: Wiring
- [x] `RandomBackgroundResolver.DefaultProviders` takes image directories; local provider inserted before placeholder.
- [x] `RandomBackgroundSpec`: `local` provider name; `CreateProvider` accepts image directories.
- [x] `ImageGenerator.ImageDirectories`; default resolver built lazily from it.
- [x] `GenerateCommand` loads `image-directories` from the settings store (injected through `Cli.BuildParser`) when `--bg` is random.
- [x] Error message for local-only failures doesn't tell the user to check their network; it points at `imgforge config add image-directories`.

## Phase 3: Tests (tests/ImgForge.Tests)
- [x] Unit: `LocalImageProviderTests` — recursive discovery across multiple dirs, extension filtering (case-insensitive),
      random choice uses all images, missing dirs skipped, no images → clear error, no dirs configured → clear error,
      requested size passed to resizer, duplicates de-duped.
- [x] Unit: `RandomBackgroundResolverTests`/`RandomBackgroundSpecTests` — default order with/without dirs; `random:local` parsing/creation; error text.
- [x] Integration (`Category=Integration`): `ChromiumImageResizer` resizes a real PNG to exact dimensions; undecodable file fails.
- [x] CLI: generate with `random:local` and no configured folders reports a clear error.

## Phase 4: Docs
- [x] README: random backgrounds table/order, settings section no longer says "not read yet"; `--bg` help text.

## Phase 5: Independent review
- [x] Separate agent reviews the full diff (correctness, requirements, tests, security, performance, accessibility).
- [x] Address all significant findings (or record why not).

## Phase 6: Verification
- [x] `dotnet BuildTestFormat.cs` passes cleanly.

## Review findings (independent agent)

Addressed:
- High: folder symlinks were followed (loop → repeated images / runaway scan). Now enumerated with a
  `ShouldRecursePredicate` that skips reparse points; links to image files are resolved for size, broken links skipped.
  Tests added.
- Medium: one bad pick failed the run. Now up to `MaxAttempts` (3) different images are tried. Tests added.
- Medium: non-timeout Chromium errors (`PuppeteerException`) bypassed fallback. Now wrapped as
  `RandomBackgroundProviderException`.
- Medium: timeout wording said "downloading" for local files. Now "could not be found and resized". Test added.
- Low: `random:picsum` etc. read settings needlessly. Now only plain `random` and `random:local` read them. Test added.
- Low: case-insensitive de-dupe by OS. Now exact (ordinal) match only.

Not addressed (deliberate):
- Chromium launches twice (resize, then render), ~1-3 s extra, only when a local image is actually used. Reusing one
  browser would change `ImageGenerator`'s structure; acceptable for now, possible follow-up.
- No unit test for the `PuppeteerException` wrap: forcing a Chromium crash reliably isn't practical; the code path is a
  single catch/rethrow.
- Huge folder trees are scanned fully each run (bounded by the 30 s per-provider budget, with a clear message).
  Reservoir sampling could avoid the list/sort; left as a follow-up if it proves slow in practice.
