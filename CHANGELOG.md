# Changelog

All notable changes to this project are recorded in this file.

The format follows [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/) and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Atap logo on the app, the installer and the Microsoft Store tiles.
- Notch window for Windows 10 (2004+) and 11 with idle, compact and expanded states.
- Media widget with now playing info and playback controls.
- Clock widget.
- Layout file at `%APPDATA%\Atap\layout.json` that sets enabled widgets, what each state shows and the compact priority. A file that does not parse is backed up as `layout.corrupt-<timestamp>.json` and replaced with defaults. A file that cannot be read or backed up, for example because it is locked, is left alone. Defaults are used for that session and menu choices are not saved.
- Placement on five positions (top left, top center, top right, left edge, right edge), set from the right-click menu. Menu choices change only the placement in `layout.json` and keep other edits to the file.
- Offset along the edge, set by editing `offset` in `layout.json`. Picking a position from the menu sets it back to 0.
- Curved shoulders where the notch meets the screen edge.
- Monitor choice: primary, one chosen monitor or all monitors. The notch falls back to the primary monitor when the chosen one is not connected.
- Layout schema version 2. Version 1 files are upgraded on load.
- Single-instance guard. A second launch exits quietly.
- Crash logging to `%APPDATA%\Atap\logs`, keeping the newest 7 files of about 1 MB at most. If UI errors keep coming (more than 20 in 10 seconds), Atap logs it and closes instead of using CPU in a loop.
- Self-update from GitHub Releases through Velopack for the installer and the portable zip. It checks 30 seconds after start and then every 24 hours. Preview builds also get later previews.
- Build option `-p:AtapChannel=Store` for a Microsoft Store build without Velopack updates. A Store listing is not published yet.

[Unreleased]: https://github.com/anake-an/atap/commits/main
