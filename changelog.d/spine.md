### Added

- Developers: widgets, features and settings pages now register themselves from their own folders. A new widget no longer changes any shared file.
- A widget's views load only while it is on. A widget that is off costs almost nothing.
- Menu text in English and Bahasa Melayu. Atap follows the Windows display language.
- Safe mode. After a crash, Atap starts once with only the clock, then normally again.
- Starting Atap a second time asks the running copy to open Settings, once Settings exists.
- The `atap` command-line tool, with `atap ping` and `atap --version` for now.
- Quit and Check for updates are now commands, so the tray and the command bar can run them.
- The licences of Atap and the packages it uses now ship in the install folder, in `THIRD-PARTY-NOTICES.md` and the `licenses` folder.

### Changed

- Layout file version 3. It adds profiles and gives the panel pages. A version 1 or 2 file is upgraded on first start and the old file is kept as `layout.v2.bak.json` (or `layout.v1.bak.json`).
- A layout file from a newer Atap is used read-only and never written. This is true even when this version cannot read its contents.
- "Top center" in the Position menu is now "Top centre".
- Upgrading from an earlier version skips the first-run setup.

### Fixed

- A widget that fails to start, or keeps failing, stops on its own instead of affecting the rest of the notch.
- Turning a widget off and on again quickly no longer makes it fail.
- Atap still starts when another program already holds the name it uses for a second launch.

### Security

- Widget ids from the layout file are logged without control characters and cut short, so an edited file cannot forge log lines.
- The `atap` tool prints only short, plain error codes. It also says so when a program run by another user holds the local API name.
