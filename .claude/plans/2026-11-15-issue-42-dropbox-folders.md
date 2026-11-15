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
  older copy exists, the older copy is used (works offline) and the next refresh is tried an hour later.
- **Picking**: every supported image (same extensions as local folders) in every zip is equally likely. macOS metadata
  (`__MACOSX/`, `._*`) and hidden (dot) entries, empty entries and entries over 50 MB are skipped. Only the chosen entry
  is extracted, to a temp file ImgForge names itself (no zip-slip), with the 50 MB cap enforced on actual bytes. Up to 3
  entries are tried if one can't be resized.
- **Resize** locally with the existing `IImageResizer` (`ChromiumImageResizer`).
- **Time limit**: the first download of a big folder can take longer than the 30 s per-provider limit, so the Dropbox
  provider gets its own (10 minutes, after review). A dead site still fails fast via a 15 s response timeout.
- **Privacy**: shared links can carry access keys, so messages never print the link; folders are referred to by their
  position in the setting ("Dropbox folder 2").
- One unreachable folder doesn't fail the run if another folder has images.

## Phase 1: Core provider
- [x] `DropboxProvider` (`IRandomBackgroundProvider`): link validation/normalization, cached zip download (atomic
      replace, size cap, stale fallback), entry listing across zips, random pick, bounded extraction, resize.
- [x] Per-provider time limit hook on `IRandomBackgroundProvider` used by `RandomBackgroundResolver`.

## Phase 2: Wiring
- [x] `RandomBackgroundResolver.DefaultProviders` takes Dropbox folders; inserted just before local folders.
- [x] `RandomBackgroundSpec`: `dropbox` provider name; `CreateProvider` accepts Dropbox folders.
- [x] `ImageGenerator.DropboxFolders`; browser pre-download for Dropbox too.
- [x] `GenerateCommand` loads `dropbox-folders` alongside `image-directories` for `random` / `random:dropbox`.
- [x] Error/hint text for Dropbox failures (`imgforge config add dropbox-folders <link>`); timeout wording.

## Phase 3: Tests (tests/ImgForge.Tests)
- [x] Unit `DropboxProviderTests` (fake HTTP handler, stub resizer, temp cache dir): link normalization (dl=1, keeps
      rlkey, rejects non-Dropbox hosts), recursive entry listing across multiple zips with filtering, cache reuse within
      24 h, refresh after expiry, stale cache on failure, HTTP error/non-zip messages without the link, oversize, one bad
      folder skipped, no folders configured, resize gets requested size, retry on bad entry, extracted temp file deleted.
- [x] Unit: resolver/spec — default order with Dropbox before local; `random:dropbox` parsing/creation; error hint.
- [x] CLI: `random:dropbox` with no configured folders → clear error; settings loading for random/random:dropbox.

## Phase 4: Docs
- [x] README: Dropbox section, provider order, settings table text; `--bg` help text and a help example.

## Phase 5: Independent review
- [x] Separate agent reviews the full diff (correctness, requirements, tests, security, performance, accessibility).
- [x] Address all significant findings (or record why not).

## Phase 6: Verification
- [x] `dotnet BuildTestFormat.cs` passes cleanly.

## Review findings (independent agent)

No high-severity findings. Addressed:
- Medium: big/slow folders. The 30 s refresh cap (which cancelled refreshes mid-body forever) was removed; refreshes
  now fall back to the older copy only when Dropbox fails or doesn't start responding, and a failed refresh backs off
  for an hour instead of retrying every run. Folders download in parallel. Provider limit raised from 3 to 10 minutes.
  Tests added (back-off).
- Medium: file-system errors (unwritable cache, disk full, failed first move, `EnsureZip` IO) escaped as raw
  exceptions and stopped the `--bg random` fallback. Now wrapped as `RandomBackgroundProviderException`; read errors
  ("connection lost") are told apart from write errors ("couldn't be saved"). Test added.
- Low: 5 s response timeout across Dropbox's redirect → 15 s.
- Low: redirects are now required to end on a Dropbox host (`*.dropbox.com`, `*.dropboxusercontent.com`). Tests added.
- Low: cache folder created owner-only on Unix; leftover partial downloads older than a day are cleaned up.
- Low: `/scl/fi/` and `/s/` file links rejected by `config add` and at fetch time; "not a folder" message mentions
  Dropbox's zip size limit. Tests added.
- Low: settings-read warning now says "image and Dropbox folders".

Not addressed (deliberate):
- Cached zips for links removed from the setting aren't deleted automatically (README says to delete the cache folder).
  Pruning on `config remove` would couple config to the provider's cache layout; possible follow-up.
- A partial download that hits the 10-minute limit isn't resumed. Dropbox's zip endpoint is generated on the fly, so
  range resumption isn't reliable; documented the limit instead.
- If one folder is slow enough to hit the overall limit, images from other (fresh) folders are discarded for that run.
  Parallel downloads make this unlikely; partial results on timeout would need the resolver to support it.
- No entry-count cap on huge zip central directories: it's the user's own data and bounded by the 2 GB download cap.
- Extracted temp images use default permissions; they exist only for the second or two of resizing.
