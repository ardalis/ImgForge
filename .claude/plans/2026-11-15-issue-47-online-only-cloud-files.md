# Issue #47 — Local image folders with online-only (download-on-demand) Dropbox files

Issue: https://github.com/ardalis/ImgForge/issues/47

## Summary

Dropbox (and OneDrive, iCloud) can keep files online-only. Confirm (and fix where needed) that `LocalImageProvider`
(1) can use an online-only photo, downloading it on demand, and (2) scanning reads only metadata, so only the chosen
photo is downloaded.

## Analysis

- **Windows (Cloud Files API, used by Dropbox and OneDrive):** placeholders are reparse points
  (`IO_REPARSE_TAG_CLOUD_*`) with `RecallOnDataAccess` (0x400000) and/or `Offline`, and report their real logical size
  in directory enumeration, so `FileInfo.Length` (from the enumeration data) doesn't download anything and isn't 0.
  They aren't `Hidden`/`System`, so `AttributesToSkip` keeps them. Placeholder **folders** can also be reparse points,
  and `ShouldRecursePredicate` skipped every reparse point folder — the biggest risk: subfolders of a cloud folder
  could be silently skipped. Opening a file's reparse data (`LinkTarget`) or listing a folder is metadata-only;
  only reading file data hydrates it.
- **macOS (File Provider, `~/Library/CloudStorage/Dropbox`):** online-only files are "dataless"; `stat`/`readdir`
  report the real size and don't materialize content; they aren't symlinks. Reading the data downloads it.
- **Linux:** Dropbox has no online-only mode.
- **Timeouts:** the download happened inside Chromium (20 s load timeout, with a misleading "did not load" message)
  under the resolver's 30 s per-provider limit. A large photo on a slow connection may not fit.

## Decisions / assumptions

- Recurse into reparse-point folders that aren't links (`LinkTarget` is `null`, i.e. not a symlink or junction), e.g.
  cloud placeholder folders. Links to folders still aren't followed.
- Only ask for a file's `LinkTarget` when it is a reparse point (avoids opening every plain file on Windows).
- Before resizing, `LocalImageProvider` reads the chosen file once (sequential, async, cancellable) with its own
  `ReadTimeout` (default 60 s), so an on-demand download happens outside Chromium. On timeout it fails with a clear
  message mentioning online-only files and does not try other images (they would likely be slow too). A read error
  (IOException etc.) counts as a failed attempt, so another image is tried.
- The resolver's per-provider limit for the local provider is `DownloadTimeout + ReadTimeout`, so the read timeout,
  not the 30 s cap, decides.
- **Not verifiable here:** this agent runs on Linux; behavior on real Windows/macOS Dropbox online-only folders
  (including watching for bulk downloads) must be confirmed by hand. The PR lists the manual checks.

## Phase 1 — Scan fixes

- [x] `ShouldRecurseInto`: recurse unless the folder is a link (Windows: name-surrogate reparse tag; elsewhere: link target).
- [x] `IsUsableSize`: check `LinkTarget` only for reparse points.

## Phase 2 — On-demand download

- [x] `LocalImageProvider.ReadTimeout` and an injectable file reader (default reads the whole file).
- [x] Read the chosen file before resizing; timeout → clear message; read errors → try another image.
- [x] Resolver gives the local provider `DownloadTimeout + ReadTimeout`.

## Phase 3 — Tests (`tests/ImgForge.Tests`)

- [x] Recurse decision: plain folder, reparse folder without a link target (cloud placeholder), link → not followed.
- [x] Placeholder-like attributes (`Offline`, `RecallOnDataAccess`) aren't skipped by the scan options.
- [x] FetchAsync reads the chosen file before resizing (and only that one).
- [x] Read timeout → clear message, resizer not called, no retry.
- [x] Read error → another image is tried.
- [x] Resolver uses the extended limit for the local provider.

## Phase 4 — Docs

- [x] README local image folders: online-only Dropbox/OneDrive/iCloud support, timeouts, what's downloaded.

## Phase 5 — Independent review

- [x] Separate agent reviews the full diff (correctness, security, performance, accessibility).
- [x] Address significant findings (or record why not):
  - Flaky resolver timeout test (real file read raced a 100 ms `ReadTimeout`) → stubbed `ReadFile`.
  - "Not a link" was too broad (`LinkTarget` is null for WSL symlinks, WCI links) → on Windows, decide by the reparse
    tag's name-surrogate bit (`WindowsReparsePoints`, `GetFileInformationByHandleEx(FileAttributeTagInfo)`, metadata
    only); unreadable tag → not searched. Linux/macOS keep the `LinkTarget` check.
  - README over-promised → iCloud needs macOS 14+, "usually only the chosen photo" (retries), simpler wording, bullets.
    The PR uses "Relates to #47" since on-device checks on Windows/macOS are still needed.
  - Build the full path only for reparse-point folders; comment noting each online-only file costs one metadata open.
  - `ReadTimeout` validated (positive, at most 1 hour) so `DownloadTimeout + ReadTimeout` can't overflow or go infinite.
  - Not changed: a cloud folder that can't be listed mid-search (e.g. the sync app isn't running) still ends the search
    of that root folder, keeping what was found so far. Same as before this change, and out of scope here.

## Phase 6 — Verification

- [x] `dotnet BuildTestFormat.cs` passes (518 tests).
