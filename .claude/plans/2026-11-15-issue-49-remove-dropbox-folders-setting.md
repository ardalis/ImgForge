# Issue #49 — Remove the unused dropbox-folders setting

Issue: https://github.com/ardalis/ImgForge/issues/49

## Summary

#43/#44 added a `dropbox-folders` setting for #42 (Dropbox shared folders over the web). #42 is closed as not
planned, so nothing reads the setting. Remove it from code, help, tests, and README. Dropbox photos are used through
`image-directories` with a synced folder instead (#48, #47).

## Decisions / assumptions

- **Existing files:** a `dropboxFolders` property (any case) is dropped when the settings file is loaded, so it isn't
  printed by `config show` and is gone from disk the next time ImgForge saves the file. Links may contain an `rlkey`
  access key, so dropping beats keeping it as an unknown property.
- Since `show` doesn't write, it prints a note to stderr when the file still has `dropboxFolders`, saying it's ignored
  and will be removed the next time a `config add/remove/clear` saves the file (or `config clear` deletes it).
- **CLI:** `config add/remove/clear dropbox-folders` fails with a clear message: the setting was removed; sync the
  folder with the Dropbox desktop app and add its local path with `imgforge config add image-directories <path>`.
  Implemented as a custom validator on the setting argument (replacing `FromAmong`, keeping tab completions), so
  other unknown names keep the standard "not recognized" error.
- Release notes come from the GitHub release (generated); the PR body states the behavior change so it shows up there.

## Phase 1 — Core

- [x] Remove `UserSetting.DropboxFolders`, `NormalizeHttpUrl`, and its `All` entry.
- [x] Remove `UserSettings.DropboxFolders`.
- [x] `UserSettingsStore.Load` strips retired properties (`dropboxFolders`, case-insensitive) from
      `AdditionalProperties` and records that it did (`UserSettings.HadRetiredSettings`, `[JsonIgnore]`).

## Phase 2 — CLI

- [x] Drop Dropbox mentions from `Cli.RootExamples` and `ConfigCommand.Examples` (no replacement example; the removed-setting error carries the synced-folder pointer); add-value help no longer says "URL".
- [x] Setting argument: custom validator with a removed-setting message for `dropbox-folders`.
- [x] `config show` prints the retired-setting note to stderr.

## Phase 3 — Tests (`tests/ImgForge.Tests`)

- [x] Update/remove Dropbox tests in `ConfigCommandTests.cs` and `UserSettingsStoreTests.cs`.
- [x] Loading a file with `dropboxFolders` succeeds, drops the value, keeps other settings and unknown properties.
- [x] Saving after such a load removes `dropboxFolders` (and the `rlkey`) from disk.
- [x] `config show` doesn't print the links and prints the note; `config add/remove/clear dropbox-folders` fails with
      the pointer to `image-directories` and doesn't write the file.
- [x] Help output has no Dropbox mentions.

## Phase 4 — README

- [x] Remove the `dropbox-folders` table row, the `config add dropbox-folders` example, the `rlkey` note, and the
      "stored only" sentence for Dropbox. Keep the synced-folder note (#48). Note what happens to old values.

## Phase 5 — Independent review

- [ ] Separate agent reviews the full diff (correctness, scope vs. issue, tests, security, performance, accessibility).
- [ ] Address all significant findings (record any skipped ones below).

## Phase 6 — Verification

- [ ] `dotnet BuildTestFormat.cs` passes (build, tests, format).

## Review notes

(filled in after review)
