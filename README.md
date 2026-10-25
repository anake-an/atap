<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/brand/atap-mark-on-dark.svg">
    <img src="assets/brand/atap-mark.svg" alt="Atap logo" width="96">
  </picture>
</p>

<h1 align="center">Atap</h1>

<p align="center"><strong>A free, open-source notch for your Windows desktop, in English and Bahasa Melayu.</strong></p>

<p align="center">
  <a href="https://github.com/anake-an/atap/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/anake-an/atap/actions/workflows/ci.yml/badge.svg"></a>
  <a href="LICENSE"><img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-blue.svg"></a>
  <a href="https://github.com/anake-an/atap/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/anake-an/atap?include_prereleases"></a>
</p>

<p align="center">
  <a href="https://atap.novafortress.my">Website</a> ·
  <a href="https://atap.novafortress.my/help">Help</a> ·
  <a href="https://github.com/anake-an/atap/releases">Releases</a> ·
  <a href="CHANGELOG.md">Changelog</a> ·
  <a href="PRIVACY.md">Privacy</a>
</p>

## What Atap is

Atap is a small black notch that sits on the edge of your screen. When idle it shows the clock. While music plays it shows what is playing. Point at it for controls. Click it to open a panel of widgets.

Every widget can be turned on or off. A layout editor sets what the notch shows in each view. Atap has no account and no telemetry. It is MIT licensed, built in Malaysia, and runs on Windows 10 and Windows 11.

## Features

**Media and sound**

- Now playing with playback controls, and synced lyrics from LRCLIB
- Volume, brightness and keyboard (Caps Lock and language) overlays
- Sound output switcher and a volume mixer for each app

**Time and focus**

- Clock, world clock, stopwatch, alarms and focus timer
- Water reminders
- To-do list and quick notes
- Next meeting from a calendar link, with a Join button

**System status**

- Battery, Wi-Fi and Bluetooth devices
- Quick toggles for dark mode, Bluetooth, microphone mute and the slim notch
- System monitor for processor, memory and disk
- Value from a link (JSON URL) and Home Assistant widgets, to show one reading from your own services
- Script widget: shows the output of a program on this PC that you approve, running it no more than once every 30 seconds. It never runs in safe mode, and any change to what it runs needs your approval again.
- A dot when an app uses your camera or microphone

**Files and clipboard**

- File shelf to hold files and folders while you work
- Clipboard history for the last 25 copies, kept in memory only

**Student**

- Next class from a timetable you set in Settings
- Assignments with deadlines

**Malaysia**

- Waktu solat from JAKIM e-Solat, with a Ramadan countdown to imsak and berbuka
- Hijri date and public holidays by state. Weather and rain alerts from MET Malaysia.

**Make it yours**

- Layout editor with panel pages, widget sizes and a compact priority
- Profiles, and app rules that switch profile, slim the notch, quiet alerts or hide it
- Five positions on one monitor or all of them, and Normal, Slim and Tucked away sizes
- Command bar with a calculator and unit converter (Ctrl+Alt+K). Panel hotkey is Ctrl+Alt+Space.
- Alerts that wait during Do not disturb and full screen apps
- First-run setup, battery saver, high contrast, larger text and less motion
- Local API and the `atap` command-line tool, so scripts can show progress on the notch
- Sync of settings and layout through your own OneDrive folder, off by default

## System requirements

- Windows 10 version 2004 (build 19041) or later, or Windows 11
- An x64 or Arm64 PC
- No .NET install needed. The runtime ships inside Atap.

## Install

Download the latest installer from [GitHub Releases](https://github.com/anake-an/atap/releases) or the [website](https://atap.novafortress.my/download). Files are named:

| PC | Installer | Portable zip |
| --- | --- | --- |
| x64, which most PCs are | `Atap-win-Setup.exe` | `Atap-win-Portable.zip` |
| Arm64, such as Snapdragon | `Atap-win-arm64-Setup.exe` | `Atap-win-arm64-Portable.zip` |

The installer puts Atap in your user folder, `%LOCALAPPDATA%\Atap`. Installed and portable copies update themselves from GitHub Releases. A preview build also gets later previews, and a stable build gets stable releases only.

> [!NOTE]
> Releases are not code signed yet, so Windows SmartScreen may warn about the installer. Signing and a Microsoft Store listing are planned but not done.

### Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.100 or later).

```
git clone https://github.com/anake-an/atap.git
cd atap
dotnet build
dotnet test
dotnet run --project src/Atap.App
```

Builds from source do not check for updates.

## Enterprise and IT

Atap installs per user and keeps everything on the PC, with no account and no server of its own.

### Install and update

- Silent install: run `Atap-win-Setup.exe --silent`. The installer is built with [Velopack](https://velopack.io), which provides this switch.
- Install location: `%LOCALAPPDATA%\Atap`.
- Updates: the app checks GitHub Releases 30 seconds after it starts and then every 24 hours. It downloads in the background and applies the update when Atap exits.
- Builds from source do not update themselves. The planned Microsoft Store build will update through the Store.
- There is no switch to turn the update check off yet. To stop it on a managed PC, block `api.github.com` for Atap. The app logs a warning and keeps running.

### Where settings and data live

| Location | Holds |
| --- | --- |
| `%APPDATA%\Atap\settings.json` | App settings and app rules |
| `%APPDATA%\Atap\layout.json` | Layout, placement and profiles |
| `%APPDATA%\Atap\data\` | Widget data such as notes and to-dos |
| `%APPDATA%\Atap\logs\` | Logs. The newest 7 files are kept, each up to about 1 MB. |
| `%LOCALAPPDATA%\AtapData\` | Data for this PC only: download cache, timer state and secrets protected by Windows data protection |

Both JSON files can be edited by hand. Atap reads changes within a second. A file that does not parse is set aside as `*.corrupt-<timestamp>.json` and replaced with defaults. Uninstalling leaves these folders in place. Delete them to remove everything.

### What goes over the network

Most requests use https and go to a fixed host. Three features go to an address you enter: next meeting, Value from a link and Home Assistant. They may use plain http or plain MQTT only for an address on your own network.

| Feature | Host | Default |
| --- | --- | --- |
| Update check | `api.github.com`, with downloads from `*.githubusercontent.com` | On in installed builds |
| Prayer times and Hijri date | `www.e-solat.gov.my`, backup `api.waktusolat.app` | Off until you pick an area |
| Weather and rain alerts | `api.data.gov.my` | Off |
| Lyrics | `lrclib.net` | Only while the lyrics widget is on |
| Next meeting | The host of the calendar link you enter | Off until you add the widget and a link |
| Value from a link (JSON URL) | The link you enter | Off until you add the widget |
| Home Assistant | The MQTT broker you enter | Off until you add the widget |
| Command bar web search | The search engine you pick, through your browser | Only when you press Enter on that row |

**Work offline** in Settings, on the Privacy page, stops every request at once except the update check. [PRIVACY.md](PRIVACY.md) lists what each request sends.

### Local API and sync

- The local API is **off by default**. When you turn it on, it listens on a named pipe that only your Windows account can open. It is closed to the network. Sync can never turn it on.
- Sync is **off by default**. When you turn it on, Atap reads and writes a sync file in your own OneDrive folder and makes no network call of its own. The OneDrive app uploads the file. Secrets, the notch position and settings that belong to one PC are never included.

### The `atap` tool

With the local API on, `atap push`, `atap alert`, `atap end` and `atap ping` show progress and alerts on the notch. Run `atap` with no arguments for the full usage and exit codes.

## Privacy and security

Atap has no account, no telemetry and no crash reports. It sends nothing about you to the people who make it. Read [PRIVACY.md](PRIVACY.md) for every request and every folder.

To report a vulnerability, follow [SECURITY.md](SECURITY.md). Please do not open a public issue for it.

## Support

Questions and bug reports are welcome. See [SUPPORT.md](SUPPORT.md) for where to go. Help pages are at [atap.novafortress.my/help](https://atap.novafortress.my/help).

## Contributing

Pull requests are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) first. It covers setup, the workflow and how to add a widget. By taking part you agree to the [Code of Conduct](CODE_OF_CONDUCT.md). Project decisions are described in [GOVERNANCE.md](GOVERNANCE.md).

More documents: [Architecture](docs/ARCHITECTURE.md), [Releasing](docs/RELEASING.md), [Third-party notices](THIRD-PARTY-NOTICES.md).

## License

MIT. See [LICENSE](LICENSE).
