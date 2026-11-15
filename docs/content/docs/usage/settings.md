---
title: "User Settings"
weight: 30
---

ImgForge keeps per-user settings in a JSON file in your user profile: `~/.imgforge/settings.json`
(`%USERPROFILE%\.imgforge\settings.json` on Windows). Set the `IMGFORGE_CONFIG_DIR` environment variable to keep it in
a different folder. The file is created the first time you change a setting.

| Setting | Values |
|---|---|
| `image-directories` | Local folders to pick images from (stored as full paths) |
| `dropbox-folders` | Dropbox shared-folder HTTP(S) URLs to pick images from |
| `templates` | Template files or folders (stored as full paths) |

These settings are only stored for now; `generate` doesn't read them yet. Image folders and Dropbox folders will be
used for random backgrounds ([#41](https://github.com/ardalis/ImgForge/issues/41),
[#42](https://github.com/ardalis/ImgForge/issues/42)).

```bash
imgforge config show                                   # settings file location and current settings as JSON
imgforge config path                                   # just the settings file path
imgforge config add image-directories ~/Pictures/backgrounds
imgforge config add dropbox-folders https://www.dropbox.com/scl/fo/abc123/xyz
imgforge config remove image-directories ~/Pictures/backgrounds
imgforge config clear image-directories                # empty one setting
imgforge config clear                                  # delete the settings file (all settings)
```

Relative paths are stored as full paths, so they keep working from any folder. `add` warns, but still saves, when a
path doesn't exist yet. You can also edit the file by hand; settings ImgForge doesn't recognize are kept when it
rewrites the file. If the file isn't valid JSON, `imgforge config clear` deletes it so you can start over.

Dropbox shared links can include an access key (`rlkey=...`). It's stored as typed in the settings file and printed by
`config show`, so treat the file like any other credential in your profile.
