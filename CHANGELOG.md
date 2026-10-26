# Changelog

All notable changes to this project are recorded in this file.

The format follows [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/) and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.2.0] - 2026-10-26

Ask AI arrives: ask a question from the command bar with your own key for Claude, ChatGPT, Gemini, OpenRouter or your own server. It is off until you turn it on.

### Added

- Ask AI, off until you turn it on. Type a question in the command bar and press Enter, and the answer shows right there.
- Use your own key for Claude, ChatGPT, Gemini or OpenRouter, or your own server such as Ollama. The service bills you, not Atap.
- Settings walks you through it: pick a service, get a key, paste it and pick Fast or Smarter. Atap checks the key for free.
- Your key stays on this PC, locked to your Windows account, and never syncs.
- Stop, Copy and Ask again under each answer. Follow-up questions remember the conversation until you close the bar.
- Include what I copied sends your copied text with the question, only when you tick it, and shows it first.
- An optional shortcut opens the command bar ready to ask.
- What uses the internet lists Ask AI. Clear saved data now removes your Ask AI keys too.

## [1.1.2] - 2026-10-26

The notch turns slim for full-screen and maximized apps, Edit layout and Settings look and feel better, lyrics are found for Apple Music, and prayer times can play a chime or the azan. Updates you start now install and reopen Atap by themselves.

### Added

- Waktu solat can play a soft chime or your own azan recording at prayer time. The sound plays once, and Stop on the notch ends it.
- Delete all my Atap data in Settings, Privacy removes everything Atap keeps on this PC, then closes Atap.

### Changed

- A maximized app turns the notch slim on its screen too, so the notch does not cover its tabs or title bar. It comes back when the app is restored, minimized or no longer in front.
- The notch pictures in Settings and in Edit layout have the same shape as the real notch, with its curved shoulders under the screen edge.
- Edit layout uses plainer words, and an empty "When you click it" page says what a click shows right now.
- Scrolling in Settings and in the Add widgets list is smooth and keeps up when you scroll fast. It no longer stops when the pointer passes over a list or text box.
- The Privacy page says plainly which parts of Atap use the internet, what they send, and whether each one is on.
- The prayer time alert is now a choice: Notch only, Soft chime, Azan or No alert. If you had the alert off, it stays off.
- When you press Check for updates and there is a new version, Atap installs it and opens again by itself. You no longer need to quit Atap. An update found in the background still waits until Atap closes, so Atap never restarts while you work.

### Fixed

- Delete all my Atap data tries again for a moment when a file is in use, so it leaves nothing behind in nearly every case.
- The Privacy page names the right server for Home Assistant set-ups that use a separate message server.
- A browser or video in full screen turns the notch slim on its screen instead of hiding it. Windows reports these like a game, which hid the notch before.
- Changing the language in Settings no longer makes the choice buttons flicker or the page seem to reload. The labels change in place.
- Lyrics are found for songs from Apple Music, which puts the artist and album together in one field.
- Lyrics are found for more songs: the album loses " - EP" and " - Single", and a song with extras such as "(feat. Someone)" or " - Remastered" is searched without them too.

## [1.1.1] - 2026-10-26

A full-screen or borderless app now turns the notch slim on its own screen, and two small fixes.

### Changed

- A full-screen or borderless app turns the notch slim on its own screen, instead of hiding the notch on every screen. The notch comes back when you close or minimize the app, or switch to another one. Games in exclusive full screen and presentations still hide it.

### Fixed

- The local API keeps all its listeners after a pipe fails to open for a moment, so programs that push progress are not slowed down until Atap restarts.
- Sync tries a conflict copy again a few seconds later when it could not be opened, such as while a virus scan reads it, instead of waiting for the next change in the folder.

## [1.1.0] - 2026-10-26

Fixes for what people found in 1.0.0, a new Edit layout with a live picture of your notch, and smoother hovering and clicking. It also uses less memory and works better on a second screen with a different scale.

### Added

- Edit layout now shows your notch as it will look. Drag widgets onto it, move them around, drop them on another panel page, or drag them off to remove them.
- Every widget in the Add widgets list shows a preview with sample content, its sizes and one line about what it does.
- Settings shows a picture of your notch in each state, with an Edit layout button.

### Changed

- The notch states have plain names: Small notch, When you point at it, When you click it and When something is live.
- Click a widget on the notch picture to pick its size, move it or remove it. The keyboard works too: arrow keys pick a widget, Ctrl and an arrow key moves it, and Delete removes it.
- Buttons across Atap have the same rounded square shape.
- Chips, badges and alarm days are rounded squares too, and the alarm switch looks like the switches in Settings.
- Text on the notch uses the same colours in every widget.
- Widgets with nothing to show say so in the same way, and paused timers look the same.
- Edit layout is called Sunting susun atur everywhere in Malay, and Mute is Bisu.
- Quick toggles is called Suis pantas in Malay, and the Water widget is now Water reminder.
- The notch menu says Screen instead of Monitor, and its update messages are full sentences.
- The Push widget is now Progress from programs, and the Script widget is Your own script.
- The resource meter, version card and app rules use plain words.
- Edit layout in the notch menu no longer says coming soon.
- Settings says Delete for profiles and Clear for finished to-dos.

### Fixed

- A click on the notch opens your own widgets, a size bigger than on hover, instead of a fixed music and Missed page. The missed alerts card only shows when you missed something.
- Cards in the open notch and the panel fit together with no empty black gaps, and cards side by side line up.
- The Lyrics card stays small when a song has no lyrics.
- Hovering and clicking the notch is smooth again: no flicker, no jump and no empty moment while it changes size.
- The music bars beside the album art move while a song plays, and the open notch shows how far through the song you are.
- The small focus timer has a button to stop and start over.
- On a side edge, the small notch shows the focus time when paused and no longer leaves a gap.
- The volume mixer shows plain app names, calls Windows' own sounds "System sounds" and shows each app once, with its helpers.
- Long sound output names show in full when you point at them.
- Edit layout opens fully on the screen the notch is on, below the notch, even when your screens use different scaling.
- Picking Bahasa Melayu or English in Settings switches the language in one click, without the page flickering.
- Atap uses less memory after you close Settings.
- Atap gives memory back to Windows after you close Settings or setup.
- Album art takes less memory while music plays.
- Closing setup with the X button no longer keeps it in memory.
- Things that were due while the PC slept still happen after it wakes. A profile switches at its set time, the battery stops showing as live, finished live activities clear and sync catches up.
- A widget you just added picks up its first settings change straight away.
- The notch notices when a video or browser goes full screen with F11 or its own full screen button.
- Alarms, reminders and other widgets that need no place on the notch keep working in a profile with its own widget list.
- Time left on alarms, assignments and the next meeting counts down each minute while you can see it.
- The next meeting widget says when your calendar could not be loaded, instead of saying you have no meetings.
- A big calendar no longer makes the notch stutter while it loads.
- Rain alerts show up as soon as you pick your state, without waiting for the next check.
- Ticking an assignment done on the notch is kept when you add or remove one in Settings.
- The Ramadan countdown follows the Hijri day, which begins at Maghrib. The evening before Ramadan counts down to the first imsak, and the last evening of Ramadan does not.
- Prayer times and the Hijri date load for your new area when you change it while they are still loading.
- The Caps Lock indicator always matches your keyboard light.
- Alarms, to-do reminders, the next class and water reminders keep their local time when you change time zone.
- The command bar opens centred on a screen with a different scale.
- Edit layout opens at its full size on a second screen with a different scale, instead of opening small and cramped.
- Settings and setup open fully on the screen, with the title bar visible, on a second screen with a different scale.
- Opening Atap again while it runs brings Settings to the front, instead of opening it behind other windows.
- The "When you click it" picture in Settings shows what a click really opens.
- "Which screen" keeps showing your screen after you move the notch to another spot.
- Apple Music shows as one row in the volume mixer, without AMPLibraryAgent.
- The focus timer's Start button stays in place, so a double click does not stop the timer.
- The size marks in the Add widgets list are rounded squares, not pills.

## [1.1.0-preview.1] - 2026-10-26

A preview of 1.1.0 with fixes for what people found in 1.0.0, and a new Edit layout with a live picture of your notch. Stable users are not moved to previews by the updater.

### Added

- Edit layout now shows your notch as it will look. Drag widgets onto it, move them around, drop them on another panel page, or drag them off to remove them.
- Every widget in the Add widgets list shows a preview with sample content, its sizes and one line about what it does.
- Settings shows a picture of your notch in each state, with an Edit layout button.

### Changed

- The notch states have plain names: Small notch, When you point at it, When you click it and When something is live.
- Click a widget on the notch picture to pick its size, move it or remove it. The keyboard works too: arrow keys pick a widget, Ctrl and an arrow key moves it, and Delete removes it.
- Buttons across Atap have the same rounded square shape.
- Chips, badges and alarm days are rounded squares too, and the alarm switch looks like the switches in Settings.
- Text on the notch uses the same colours in every widget.
- Widgets with nothing to show say so in the same way, and paused timers look the same.
- Edit layout is called Sunting susun atur everywhere in Malay, and Mute is Bisu.
- Quick toggles is called Suis pantas in Malay, and the Water widget is now Water reminder.
- The notch menu says Screen instead of Monitor, and its update messages are full sentences.
- The Push widget is now Progress from programs, and the Script widget is Your own script.
- The resource meter, version card and app rules use plain words.
- Edit layout in the notch menu no longer says coming soon.
- Settings says Delete for profiles and Clear for finished to-dos.

### Fixed

- A click on the notch opens your own widgets, a size bigger than on hover, instead of a fixed music and Missed page. The missed alerts card only shows when you missed something.
- Cards in the open notch and the panel fit together with no empty black gaps, and cards side by side line up.
- The Lyrics card stays small when a song has no lyrics.
- Hovering and clicking the notch is smooth again: no flicker, no jump and no empty moment while it changes size.
- The music bars beside the album art move while a song plays, and the open notch shows how far through the song you are.
- The small focus timer has a button to stop and start over.
- On a side edge, the small notch shows the focus time when paused and no longer leaves a gap.
- The volume mixer shows plain app names, calls Windows' own sounds "System sounds" and shows each app once, with its helpers.
- Long sound output names show in full when you point at them.
- Edit layout opens fully on the screen the notch is on, below the notch, even when your screens use different scaling.
- Picking Bahasa Melayu or English in Settings switches the language in one click, without the page flickering.
- Atap uses less memory after you close Settings.
- Atap gives memory back to Windows after you close Settings or setup.
- Album art takes less memory while music plays.
- Closing setup with the X button no longer keeps it in memory.
- Things that were due while the PC slept still happen after it wakes. A profile switches at its set time, the battery stops showing as live, finished live activities clear and sync catches up.
- A widget you just added picks up its first settings change straight away.
- The notch notices when a video or browser goes full screen with F11 or its own full screen button.
- Alarms, reminders and other widgets that need no place on the notch keep working in a profile with its own widget list.
- Time left on alarms, assignments and the next meeting counts down each minute while you can see it.
- The next meeting widget says when your calendar could not be loaded, instead of saying you have no meetings.
- A big calendar no longer makes the notch stutter while it loads.
- Rain alerts show up as soon as you pick your state, without waiting for the next check.
- Ticking an assignment done on the notch is kept when you add or remove one in Settings.
- The Ramadan countdown follows the Hijri day, which begins at Maghrib. The evening before Ramadan counts down to the first imsak, and the last evening of Ramadan does not.
- Prayer times and the Hijri date load for your new area when you change it while they are still loading.
- The Caps Lock indicator always matches your keyboard light.
- Alarms, to-do reminders, the next class and water reminders keep their local time when you change time zone.
- The command bar opens centred on a screen with a different scale.

## [1.0.0] - 2026-10-26

First public release of Atap, a free notch for Windows. It is open source under the MIT licence, made in Malaysia, and available in English and Bahasa Melayu. Releases are not code signed yet, so Windows SmartScreen may warn when you run the installer. Installers and portable zips are provided for x64 and Arm64 PCs.

### Added

**Notch**

- A notch at the top of the screen for Windows 10 (2004 and later) and Windows 11. It has idle, compact and expanded states, curved shoulders where it meets the screen edge, and a soft spring when it grows and shrinks.
- Five positions: top left, top centre, top right, left edge and right edge. Pick one from the right-click menu or from a picture in Settings.
- Show it on the primary monitor, one chosen monitor or all monitors. It falls back to the primary monitor when the chosen one is not connected.
- Notch size: Slim is a thin bar that opens when you point at it, and music and alerts still show. Tucked away is a line at the screen edge, and only urgent alerts drop down.
- Hide for 1 hour from the right-click menu. The notch comes back by itself, or when you start Atap again.
- Drag files over the notch and it opens, so you can drop them on the file shelf.
- Scroll on the notch to change the volume. Middle-click it to play or pause music.
- A panel with up to six pages of widgets, opened with a hotkey. Tap to open or close it, or hold to peek at the notch.
- A command bar for Atap actions, quick sums, unit conversions, installed apps and web search. Sums handle + - * / ^, brackets and percent, so 200 + 10% gives 220, and 0.1 + 0.2 gives 0.3. Conversions such as "5 km to mi" or "100 °C ke °F" work with English and Bahasa Melayu unit names, including the kati and tahil. There is no currency. Web search is always the last row and opens only when you press Enter.
- Alerts show as a short pill, then the notch goes back to what it showed before. Urgent alerts go first. An alarm stays until you close it, for up to a minute.
- Alerts that arrive during a game, a full-screen video or Do not disturb wait in the Missed alerts widget. Alarms still show as soon as the notch is back.
- An Alert sound setting: the Windows sound, your own sound file or no sound. A sound plays once, and the Stop button ends it. "Stay quiet during Do not disturb" is on by default.
- The notch shows the volume level when you use the volume keys, and the brightness level on laptop and tablet screens. Both can be turned off under Behaviour in Settings.
- Caps Lock and keyboard language show on the notch when you press Caps Lock or switch language. Atap only notices which key you pressed, never what you type.

**Widgets**

- Media: now playing info and playback controls.
- Lyrics: the words of the playing song, line by line, from LRCLIB. Japanese and Korean lines can show in Latin letters. You can add a translation link under More options to show a translation under each line.
- Clock, with the hours above the minutes on the left and right edges.
- Sound output: switch between speakers and headphones from the notch.
- Volume mixer: make each app louder or quieter, or mute it.
- Battery: how much is left and whether it is charging. The small notch says "Charging" for 10 seconds when you plug in. A battery low alert shows once each time the battery runs down, at a level you pick.
- Wi-Fi: whether you are connected, the signal strength and whether the connection is metered. The network name shows only if you turn that on.
- Bluetooth: your connected devices, with their battery where the device reports it.
- Camera and mic: a dot on the notch while an app uses your camera or microphone, and which app it is.
- System monitor: how busy the processor is, memory used, and free space on your system drive.
- Quick toggles: dark mode, Bluetooth, mute the microphone and the slim notch, one click each. Toggles your PC does not have are hidden.
- File shelf: drop files and folders on it to keep them close, click to open, or drag them out.
- Clipboard: the last 25 things you copied. Click one to copy it again. It skips copies that password managers mark as private. A "Keep my history" option remembers them after Atap closes, locked to your Windows account.
- Focus timer with short and long breaks, stopwatch with laps, and world clock for up to three places. The focus timer and stopwatch keep going if Atap or the PC restarts.
- Alarms with repeat days and a 5 minute snooze. An alarm that was missed while the PC slept shows "Missed alarm" once.
- Water: a gentle reminder to drink during the hours you pick, and a count of glasses for the day. It waits while the PC is locked.
- To-do, with a reminder at 9 AM on the day you give a task. Notes, with pinned notes.
- Next class and Assignments, from a timetable and deadlines you add in Settings. The small notch shows your next class from 15 minutes before it starts.
- Next meeting, from a private calendar link (Google Calendar, Outlook or iCloud), with a Join button for Teams, Zoom and Meet. It shows on the notch 5 minutes before the start.

**Malaysia**

- Waktu solat: the next prayer and a countdown on the notch, and every time for today when you open it. Times come from JAKIM's e-Solat, with api.waktusolat.app as a backup. All 60 JAKIM zones are included.
- The notch shows the next prayer 10 minutes before it, and a short alert at the prayer time. Both can be changed or turned off. During Ramadan the widget also counts down to imsak and berbuka.
- Hijri date, with JAKIM's official date for your area.
- Pick your state and area on the Prayer times page, or press Find my area. Your location stays on your PC. Nothing is fetched until you pick an area.
- Weather: MET Malaysia's forecast for your state, with today in full and the next three days.
- Rain alerts: thunderstorm and heavy rain warnings for your state, as an alert on the notch. Atap fetches the whole warning list and picks your state on your PC, so your state is never sent.
- Public holidays: the next holidays in your state, from the official federal and state lists for 2026 and 2027, bundled with Atap. Dates that may still change, such as Hari Raya, are marked. When the lists run out, it asks you to update Atap.

**Customisation**

- Edit layout: choose what the notch shows when idle, when you point at it and on each panel page. Add widgets from a searchable list, pick a size for each, move them or remove them. Set the compact priority for when two things are live at once.
- Undo, Redo, Start over and Cancel. Nothing is saved until you press Done. Settings can bring back the layout from before the last Done.
- Profiles: make other sets of widgets, such as one for games or for class, and pick your usual one in Settings.
- App rules: switch profile, make the notch slim, quiet alerts or hide the notch when an app is in front, at a set time, on a Wi-Fi network or by power state.
- Battery saver modes: on battery (the default), always, never, below a battery level or follow Windows energy saver. Background updates run less often while it is on.
- First-run setup: pick a language, where the notch sits, a starting layout, your area and whether Atap starts with Windows. The four starting layouts are Minimal, Student, Work and Malaysia, and each says whether it uses the internet.
- Settings with pages for Home, What it shows, Prayer times, Privacy and About. Start with Windows is off until you turn it on.
- Text size and Less motion options.

**Custom widgets and sync**

- Value from a link: shows one value from any web link that gives JSON, as a number, text, bar, list or status dot. Set a warning level to light up the notch. If the link stops answering, the last value stays with "Out of date".
- Script: runs a program you choose on this PC and shows what it prints. It runs only what you approve on this PC, never in safe mode, at most every 30 seconds, and stops any run over 10 seconds. A change to the program, an argument or the working folder needs approval again.
- Home Assistant: one reading from your MQTT broker, such as a room temperature or a door sensor. Your password is locked on this PC.
- OneDrive sync: your settings and layout follow you to your other PCs through your OneDrive folder. Where the notch sits and settings that only make sense on one PC stay on each PC. First-run setup can use the settings already in OneDrive, after saving a copy of this PC's settings.

**Privacy and safety**

- Work offline, on the Privacy page, stops Atap using the internet straight away and keeps showing what it saved. Checking for updates is not affected.
- Saved data shows how much space Atap's downloads take and clears them with one button. Parts of the notch that go online keep a saved copy for when you are offline.
- Privacy lists what uses the internet, and whether it is on.
- Keys, tokens and passwords you enter are kept with Windows data protection. They open only for you on this PC and are never synced.
- Atap waits longer after a failed request, respects services that ask it to wait and never asks for the same thing twice at once.
- Safe mode: after a crash, Atap starts once with only the clock, then normally again.

**Accessibility and language**

- A Language setting: Same as Windows, English or Bahasa Melayu. It changes the text at once, with no restart.
- Short times read naturally in both languages, for example "in 23 min" and "3 hari lagi".
- Settings and the new controls work with the keyboard and screen readers. They show a focus ring, grow with the text size and use your system colours in high contrast mode. Status dots change shape for each level, so colour is never the only sign.

**For IT and developers**

- The `atap` command-line tool with `ping`, `push`, `end` and `alert`, and `atap --version`. Turn on the local API in Settings to let programs on this PC show progress and alerts on the notch. Nothing outside the PC can reach it. Scripts can send 3 alerts at once, then 1 every 5 seconds. You can add `atap` to PATH, and uninstalling removes it.
- A layout file at `%APPDATA%\Atap\layout.json` and a settings file `settings.json`. Changes made by hand while Atap runs show up straight away. A layout file that does not parse is backed up as `layout.corrupt-<timestamp>.json` and replaced with defaults. Older layout files are upgraded on first start and the old file is kept as a backup. A file from a newer Atap is never downgraded.
- Self-update from GitHub Releases through Velopack for the installer and the portable zip. It checks 30 seconds after start and then every 24 hours.
- A single-instance guard. Starting Atap a second time opens Settings in the running copy.
- Crash logging to `%APPDATA%\Atap\logs`, keeping the newest 7 files of about 1 MB at most.
- The licences of Atap and the packages it uses ship in the install folder, in `THIRD-PARTY-NOTICES.md` and the `licenses` folder.
- Build option `-p:AtapChannel=Store` for a Microsoft Store build without Velopack updates. A Store listing is not published yet.

### Security

- Plain http only works for addresses on your own network, such as a Home Assistant box. The address is checked after the name is looked up.
- Redirects to another site are refused, so your headers and keys never follow a link somewhere else.
- Saved copies and logs never hold the web address. Logs show only the site and the status.
- Script settings with hidden or blank-looking characters, or with a folder or file name that ends in a space or dot, are refused.
- A sync file cannot turn on the local API, PATH or start with Windows. Secret values never leave the PC, and a synced widget asks for its keys again if another PC changes where it connects.
- Text in pushes and alerts is cleaned before it is shown, and the `atap` tool prints only short, plain error codes.

[Unreleased]: https://github.com/anake-an/atap/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/anake-an/atap/compare/v1.1.2...v1.2.0
[1.1.2]: https://github.com/anake-an/atap/compare/v1.1.1...v1.1.2
[1.1.1]: https://github.com/anake-an/atap/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/anake-an/atap/compare/v1.0.0...v1.1.0
[1.1.0-preview.1]: https://github.com/anake-an/atap/compare/v1.0.0...v1.1.0-preview.1
[1.0.0]: https://github.com/anake-an/atap/releases/tag/v1.0.0
