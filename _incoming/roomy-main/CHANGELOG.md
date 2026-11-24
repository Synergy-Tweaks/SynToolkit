# Changelog

## Unreleased

- **Fits smaller screens.** The main and Settings windows now shrink to fit the screen, so the Scan button and Save button never end up under the taskbar on laptops with high display scaling. Thanks to [@croupier](https://github.com/croupier) for finding it ([#6](https://github.com/krezio/roomy/pull/6)).
- **Leftovers from uninstalled apps.** Roomy now finds folders in AppData that don't match anything installed and haven't changed in 3+ months (50 MB or more). They're always your call and never ticked, and with the Recycle Bin option on, whole folders go to the Recycle Bin. Suggested by Hyperion481 on Reddit.

## 0.1.5

- **Hibernation file.** Roomy now shows the hidden `hiberfil.sys`, often 6–16 GB, and can turn hibernation off through Windows' own `powercfg` with one admin prompt. Fast Startup turns off with it, and you can turn it back on from Settings at any time.
- **Game launcher caches.** Roomy now clears the built-in browser caches of Battle.net, the EA app, Ubisoft Connect, Riot Client and GOG Galaxy. Only Chromium cache folders are touched, never logins or game files.
- **Simplified Chinese.** Roomy now speaks 简体中文 on Simplified Chinese Windows. Thanks to [@amaoo](https://github.com/amaoo) ([#5](https://github.com/krezio/roomy/pull/5)).

## 0.1.4

- **Safer cleanup with junctions and symlinks.** Roomy now checks where every file really lives before deleting it, and skips anything reached through a junction, symlink or mount point anywhere in the path, not just inside the cleaned folder. The check and the delete use the same open file handle, so the path can't be swapped in between. Thanks to [@Anchoder](https://github.com/Anchoder) for the detailed report ([#4](https://github.com/krezio/roomy/issues/4)).
- **Clear message when Roomy can't start.** Instead of Windows' "unknown hard error", Roomy now says what went wrong and saves the details to `%LOCALAPPDATA%\Roomy\crash.log`, so problems can be reported and fixed. The file stays on your PC.

## 0.1.3

- **Russian translation.** Roomy now follows your Windows language and speaks Russian as well as English. Thanks to [@KVGeorgich](https://github.com/KVGeorgich) ([#2](https://github.com/krezio/roomy/pull/2), [#3](https://github.com/krezio/roomy/pull/3)).
- **Recycle Bin option.** In Settings, choose to send old installers and big downloads to the Recycle Bin instead of deleting them, so you can get them back. Caches are still deleted, since apps rebuild them.
- **More app caches.** Roomy now finds the Spotify, Epic Games Launcher and new Microsoft Teams caches. Spotify offline downloads are left alone.
- **Sharper look.** Badges and pill buttons no longer have distorted corners. Thanks to [@spoonkuh](https://github.com/spoonkuh) ([#1](https://github.com/krezio/roomy/pull/1)).
- The warning about Windows Update files now wraps on narrow windows.

## 0.1.2

- **Settings window.** Choose whether Roomy starts with Windows and how it checks in the background.
- **Low disk space alerts.** Optional background check that lets you know when a drive is running low.
- **Weekly cache check.** Optional reminder when at least 1 GB of temp files and app caches could be cleared. Nothing is deleted without you reviewing it.
- Only one copy of Roomy runs at a time; opening it again brings the existing window forward.
- The Settings window now shows the Roomy version.
- Clearer wording throughout the app.

## 0.1.1

- New graphite look for the app.
- Updated screenshots and demo.

## 0.1.0

- First release: scan, plain-English results, space map, biggest files and share card.
