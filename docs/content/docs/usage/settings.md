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
| `templates` | Template files or folders (stored as full paths) |

`generate` reads `image-directories` for `--bg random` and `--bg random:local`; `templates` is only stored for now. To
use photos from Dropbox, sync the folder with the Dropbox desktop app and add its local path to `image-directories`.

```bash
imgforge config show                                   # settings file location and current settings as JSON
imgforge config path                                   # just the settings file path
imgforge config add image-directories ~/Pictures/backgrounds
imgforge config remove image-directories ~/Pictures/backgrounds
imgforge config clear image-directories                # empty one setting
imgforge config clear                                  # delete the settings file (all settings)
```

Relative paths are stored as full paths, so they keep working from any folder. `add` warns, but still saves, when a
path doesn't exist yet. You can also edit the file by hand; settings ImgForge doesn't recognize are kept when it
rewrites the file. If the file isn't valid JSON, `imgforge config clear` deletes it so you can start over.

Earlier versions had a `dropbox-folders` setting that was never used; it has been removed. A `dropboxFolders` entry
left in an existing settings file is ignored and isn't shown by `config show`. It's deleted the next time ImgForge
saves the file, or run `imgforge config clear` to delete the whole file now.
