# Issue #42: Dropbox

https://github.com/ardalis/ImgForge/issues/42

> Add support for using a (collection of) Dropbox folder(s) (via http) for images. Resize would take place locally.
> Random would consider all supported images in all folders recursively.

Related: #43 / PR #44 added the `dropbox-folders` setting (stored, not yet read). #41 / PR #45 added local image folders,
`LocalImageProvider`, and `ChromiumImageResizer` (local crop/scale), which this reuses.

## Assumptions

- **Configuration** = the existing `dropbox-folders` user setting (shared-folder links). No new CLI option, no API key.
- **Order** (user instruction): Dropbox comes just before local folders. Plain `--bg random` becomes
  Unsplash/Pexels/Pixabay (when keyed) → picsum.photos → *Dropbox folders* (when configured) → local image folders (when
  configured) → placehold.co.
- Also add `--bg random:dropbox` (Dropbox only, no fallback), like `random:local`.
- **"Via http"**: a Dropbox shared-folder link with `dl=1` downloads the whole folder (recursively) as a zip, with no
  sign-in or API key. Only `https://dropbox.com` / `www.dropbox.com` links are accepted (sent over https); other query
  parameters (e.g. `rlkey`) are kept and `dl` is forced to `1`.
- **Caching**: downloading a whole folder every run would be slow, so each folder's zip is cached in
  `<settings folder>/cache/dropbox/<sha256 of link>.zip` and re-downloaded after 24 hours. If a refresh fails and an
  older copy exists, the older copy is used (works offline).
- **Picking**: every supported image (same extensions as local folders) in every zip is equally likely. macOS metadata
  (`__MACOSX/`, `._*`) and hidden (dot) entries, empty entries and entries over 50 MB are skipped. Only the chosen entry
  is extracted, to a temp file ImgForge names itself (no zip-slip), with the 50 MB cap enforced on actual bytes. Up to 3
  entries are tried if one can't be resized.
- **Resize** locally with the existing `IImageResizer` (`ChromiumImageResizer`).
- **Time limit**: the first download of a big folder can take longer than the 30 s per-provider limit, so the Dropbox
  provider gets its own (3 minutes). A dead site still fails fast via the 5 s response timeout.
- **Privacy**: shared links can carry access keys, so messages never print the link; folders are referred to by their
  position in the setting ("Dropbox folder 2").
- One unreachable folder doesn't fail the run if another folder has images.

## Phase 1: Core provider
- [ ] `DropboxProvider` (`IRandomBackgroundProvider`): link validation/normalization, cached zip download (atomic
      replace, size cap, stale fallback), entry listing across zips, random pick, bounded extraction, resize.
- [ ] Per-provider time limit hook on `IRandomBackgroundProvider` used by `RandomBackgroundResolver`.

## Phase 2: Wiring
- [ ] `RandomBackgroundResolver.DefaultProviders` takes Dropbox folders; inserted just before local folders.
- [ ] `RandomBackgroundSpec`: `dropbox` provider name; `CreateProvider` accepts Dropbox folders.
- [ ] `ImageGenerator.DropboxFolders`; browser pre-download for Dropbox too.
- [ ] `GenerateCommand` loads `dropbox-folders` alongside `image-directories` for `random` / `random:dropbox`.
- [ ] Error/hint text for Dropbox failures (`imgforge config add dropbox-folders <link>`); timeout wording.

## Phase 3: Tests (tests/ImgForge.Tests)
- [ ] Unit `DropboxProviderTests` (fake HTTP handler, stub resizer, temp cache dir): link normalization (dl=1, keeps
      rlkey, rejects non-Dropbox hosts), recursive entry listing across multiple zips with filtering, cache reuse within
      24 h, refresh after expiry, stale cache on failure, HTTP error/non-zip messages without the link, oversize, one bad
      folder skipped, no folders configured, resize gets requested size, retry on bad entry, extracted temp file deleted.
- [ ] Unit: resolver/spec — default order with Dropbox before local; `random:dropbox` parsing/creation; error hint.
- [ ] CLI: `random:dropbox` with no configured folders → clear error; settings loading for random/random:dropbox.

## Phase 4: Docs
- [ ] README: Dropbox section, provider order, settings table text; `--bg` help text and a help example.

## Phase 5: Independent review
- [ ] Separate agent reviews the full diff (correctness, requirements, tests, security, performance, accessibility).
- [ ] Address all significant findings (or record why not).

## Phase 6: Verification
- [ ] `dotnet BuildTestFormat.cs` passes cleanly.
