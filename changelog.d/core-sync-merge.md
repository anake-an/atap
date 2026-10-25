### Added

- Merge logic for the coming OneDrive sync, in Atap.Core. Nothing turns it on yet.
- The sync file keeps each setting, the rules, each part of the layout and the settings of each widget as a separate unit.
- The newest change of each unit wins. A tie goes to the PC with the higher device id, so every PC picks the same winner.
- A unit taken from another PC keeps that PC's stamp, so it never comes back as a new change.
- The first PC to turn on sync writes its settings with the time of that sync. A second PC that picks "Use the settings from OneDrive" always takes them, whatever the device ids.
- A PC whose clock runs ahead does not make the PCs rewrite the sync file in turn. A change made after its change still wins.
- OneDrive conflict copies of the sync file are merged the same way. "Keep this PC's settings" also wins over newer changes in a conflict copy.
- Placement, settings that belong to one PC and secret values never leave the PC. A sync file cannot turn on the local API, PATH or start with Windows.
- A Secret setting stays on the PC even when it holds a value typed by hand.
- A secret value in a list, such as a request header, stays with its own item when another PC reorders the list. An item that was changed on another PC comes without the secret, so the widget has to be set up again.
- A layout or settings file that was reset to its defaults can take its units from OneDrive, so the defaults do not spread to the other PCs.
- A sync file over 1 MB, nested deeper than 32 levels or written by a newer Atap is ignored. Sync never writes over it by itself; only choosing Replace the sync file does.
- Keys and passwords are put back into a synced widget only while it still connects where it did. If another PC changes its address, host, server or port, the widget asks for them again, so a changed sync file can never send them somewhere new.
- A setting stamped with an impossible time, such as the year 9999, is ignored, so it can never lock that setting on the other PCs.
- A sync file that is not valid UTF-8, for example one saved as ANSI in Notepad, counts as broken. It is written over instead of stopping sync.
