# Privacy

Atap has no account, no telemetry and no crash reports. It sends nothing about you to the people who make it.

A few features fetch data from the internet. Each one is listed here. Everything else stays on your PC.

## What leaves your PC

| Feature | Goes to | What is sent | Default |
| --- | --- | --- | --- |
| Update check | GitHub Releases (api.github.com, with downloads from *.githubusercontent.com) | Your IP address, the Atap version and the update channel | On in builds from GitHub Releases. Not in builds from source. Store builds update through the Microsoft Store. |
| Prayer times and Hijri date | www.e-solat.gov.my, or api.waktusolat.app when e-Solat is down | Your JAKIM zone code, and the year and month | Off until you pick your area |
| Weather | api.data.gov.my | The forecast location id of your state | Off |
| Rain alerts | api.data.gov.my | Nothing about you. Atap fetches every current warning and picks yours on your PC. | Off |
| Lyrics | lrclib.net | The title, artist, album and length of the song that is playing | Off until you add the lyrics widget |
| Lyrics translation | The translation link you enter | The lyrics line playing now and the language you pick, only while the widget is open | Off until you enter a link |
| Next meeting | The host of the calendar link you enter | A request to that link | Off until you add the widget and enter a link |
| Value from a link (JSON URL) | The link you enter | The request, with the headers and sign-in details you set | Off until you add the widget |
| Home Assistant | The MQTT broker you enter | The connection, with the user name and password you set | Off until you add the widget |
| Command bar web search | The search engine you pick, through your browser | What you typed, only when you press Enter on the web search row | On, always the last row |
| Ask AI | The service you pick: api.anthropic.com, api.openai.com, generativelanguage.googleapis.com, openrouter.ai, or your own server | Your question and the earlier questions and answers while the command bar stays open, with your own key. Copied text only if you tick Include what I copied, up to 8,000 characters. When you save a key, one free request lists the models to check it. | Off until you turn it on and add a key |

Most requests use https and go to the fixed hosts listed above. Next meeting, Value from a link, Home Assistant, lyrics translation and Ask AI with your own server go to the address you enter. They may use plain http or plain MQTT only for an address on your own network: 127.x, 10.x, 172.16 to 172.31, 192.168.x, 169.254.x, and 100.64 to 100.127, which Tailscale uses. On a mobile or ISP network that also uses 100.64 addresses, turn on https or TLS for anything with a password. Each web request carries a user agent with the Atap version and the project's web address.

Atap keeps a copy of what it downloads, so the notch still works offline. The copy is in `%LOCALAPPDATA%\AtapData\cache`. You can clear it in **Settings > Privacy > Saved data**. Lyrics translations are never saved there. They stay in memory and are gone when the song changes.

Ask AI keys are locked to your Windows account in `%LOCALAPPDATA%AtapDatasecrets.json`. They are never synced, never written to a log and never shared through the local API. Questions and answers stay in memory and are gone when the command bar closes. **Remove key**, **Clear saved data** and **Delete all my Atap data** remove them.

**Settings > Privacy** also shows **What uses the internet** right now, and has a **Work offline** switch. It stops every request at once, except the update check.

## Sync

OneDrive sync is off until you turn it on. Atap makes no network call for it. It reads and writes a sync file in your OneDrive folder, and the OneDrive app uploads that file. The file holds your settings and layout. It never holds secrets, the notch position or settings that belong to this PC.

## Script widget

The script widget is off until you add it and approve a program. Atap itself sends nothing for it. It runs the program on this PC and shows what the program prints. Anything the program sends over the network is up to that program. What it runs is kept on this PC only and is never synced.

## Clipboard history

The clipboard widget keeps what you copy in memory only, so it is gone when Atap closes. If you turn on **Keep my history**, Atap also saves it in `%LOCALAPPDATA%\AtapData\state\clipboard`. The file is protected by Windows for your account, so it opens only for you on this PC. It is never synced, sent or logged. Turning the setting off deletes the file.

## What never leaves your PC

- Logs and resource readings
- Clipboard history, notes, to-dos and shelf files
- Keystrokes
- The names of apps in front, Wi-Fi names and Bluetooth devices
- Whether your microphone or camera is in use
- What scripts and programs send to the notch, and script output
- Passwords, tokens and other secrets

## Your location

**Find my area** asks Windows for your location once, when you press it. Atap matches the result to a list of districts built into the app. It sends nothing and stores nothing. Windows may use its own location service to answer.

The Wi-Fi name is read only while a feature that needs it is on. Windows may ask for Location permission for it, because a Wi-Fi name can show where you are. Atap never logs or sends the name.

## The local API

Programs on your PC can show progress on the notch through `atap push`. This is off by default. It uses a named pipe that only your Windows account can open. It is closed to the network.

## Where your data is

| Folder | Holds |
| --- | --- |
| `%APPDATA%\Atap` | settings.json, layout.json, widget data such as notes and to-dos, and logs |
| `%LOCALAPPDATA%\AtapData` | Data that belongs to this PC, the download cache, and secrets and kept clipboard history protected by Windows for your account |

Logs replace your user folder with `%USERPROFILE%` and leave out personal content such as song titles. Only the newest 7 log files are kept.

Uninstalling Atap leaves both folders in place, so a reinstall keeps your setup. To remove everything, delete both folders after you uninstall.

## Questions

Ask in [GitHub Discussions](https://github.com/anake-an/atap/discussions) or open an issue at [github.com/anake-an/atap](https://github.com/anake-an/atap/issues). See [SUPPORT.md](SUPPORT.md). To report a security problem, see [SECURITY.md](SECURITY.md).
