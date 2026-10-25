### Added

- Changes you make to layout.json or settings.json by hand while Atap runs now show up straight away.
- Keys and tokens you enter in Settings are kept safe with Windows data protection. They only open for you on this PC and are never synced.

### Changed

- Settings and layout changes are saved half a second after you stop changing them, so quick changes give one save instead of many.
- If a settings file is open in another app, Atap tries again a moment later. If it still cannot save, it keeps your change and tries again with your next one.

### Fixed

- A settings or layout file you are still typing in is never backed up or replaced. Atap waits until it reads correctly again.
- A layout file from a newer Atap, for example one synced from another PC, is never downgraded when you change the layout.
- A widget file that cannot be read, such as a damaged to-do list, is kept aside instead of being overwritten by the next save.
