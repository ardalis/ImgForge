# Issue #43 — Persistent Configuration

Issue: https://github.com/ardalis/ImgForge/issues/43

## Summary

"Add CLI commands for viewing and clearing user settings which should be stored in the user profile by default in an
imgforge specific folder as json. Examples of things to store there include local image directories and online Dropbox
folders as well as templates."

Related: #41 (local image directories for `--bg random`), #42 (Dropbox folders). Neither is implemented yet; this issue
adds the settings store and CLI that they will read from.

## Decisions / assumptions

- Settings file: `~/.imgforge/settings.json` (`Environment.SpecialFolder.UserProfile`). "By default" means it can be
  moved: the `IMGFORGE_CONFIG_DIR` environment variable names a different folder (also lets tests and CI stay out of
  the real profile).
- Settings (camelCase JSON, every one a list of strings):
  - `imageDirectories` — local image folders (#41). Stored as full paths.
  - `dropboxFolders` — Dropbox shared-folder HTTP(S) URLs (#42).
  - `templates` — template files or folders (stored as full paths).
  Unknown JSON properties (hand edits, newer versions) are kept when the file is rewritten.
- Commands under `imgforge config`:
  - `config show` — prints the settings file path and the settings as JSON (empty lists when unset/no file).
  - `config path` — prints just the settings file path.
  - `config clear [<setting>]` — with no argument, deletes the settings file; with a setting name, empties that setting.
  - `config add <setting> <value>` / `config remove <setting> <value>` — beyond the issue's "view and clear", but
    without them the settings can only be edited by hand until #41/#42 land. Small and low-risk, so included.
  - CLI setting names are kebab-case: `image-directories`, `dropbox-folders`, `templates`.
- Validation on `add`: Dropbox values must be absolute HTTP(S) URLs; paths become full paths, with a warning
  (not an error) when the path doesn't exist yet. Adding a value already present is a no-op with a message.
- Using the settings in `generate` (random from image directories, Dropbox, named templates) is out of scope —
  that's #41/#42 and later work.
- A malformed settings file gives a clear error naming the file (exit 1); `config clear` still works on it.
- Writes go to a temp file then move, so a crash doesn't leave a half-written file.

## Phase 1 — Core settings store (`src/ImgForge.Core/Configuration/`)

- [x] `UserSettings` (lists + extension data) and `UserSettingsStore` (default dir, Load, Save, Clear)
- [x] `UserSetting` key metadata: CLI name, JSON name, accessor, value normalization/validation

## Phase 2 — CLI (`src/ImgForge/Commands/ConfigCommand.cs`)

- [x] `config show|path|clear|add|remove`, output via `IConsole` so tests use `TestConsole`
- [x] Register in `Cli.BuildParser` (store injectable for tests); help examples for `config`
- [x] Root examples include a config example

## Phase 3 — Tests (`tests/ImgForge.Tests`)

- [x] `UserSettingsStoreTests`: default path (env override), load missing file, round trip, unknown properties kept,
      malformed JSON error, clear all
- [x] `ConfigCommandTests`: show (empty/with values), path, add (normalizes paths, rejects bad URL, duplicate no-op,
      unknown setting), remove (present/missing), clear all / one setting, clear on malformed file
- [x] `CliHelpTests`: config help shows examples; config examples parse

## Phase 4 — Docs

- [x] README "User settings" section + docs site page `docs/content/docs/usage/settings.md`

## Phase 5 — Independent review

- [ ] Separate agent reviews full diff (correctness, coverage, security, performance, accessibility)
- [ ] Address significant findings (record any skipped ones here)

## Phase 6 — Verification

- [ ] `dotnet BuildTestFormat.cs` clean
