# Architecture

Atap is a C# .NET 10 WPF app. The code is split in three projects, plus two test projects.

| Project | Role |
| --- | --- |
| `src/Atap.Core` | Contracts and pure logic: widgets, layout, settings, storage, scheduler, HTTP, alerts, commands, notch surfaces and placement math. No UI. Core never logs. It returns outcomes and raises events, and the app logs them. |
| `src/Atap.App` | The WPF notch, composition, the built-in widgets, Windows wrappers, updates and logging. |
| `src/Atap.Cli` | The `atap` command-line tool. NativeAOT, no reference to Core. It talks to Atap over a named pipe. |
| `tests/Atap.Core.Tests` | xUnit tests for Core and the CLI. |
| `tests/Atap.App.Tests` | xUnit tests for the app: composition, guards over views and strings, and WPF tests on one STA thread. |

Anything that can be tested without a window lives in Core. Core, App and the CLI build for `win-x64` and `win-arm64`.

## Folders

```
src/Atap.Core/   Widgets/ Layout/ Placement/ Settings/ Storage/ Localization/ Scheduling/
                 Net/ Alerts/ Notch/ Shared/ Power/ Commands/ Input/ Rules/ Updates/
                 Malaysia/ Media/ Productivity/ Lyrics/ Custom/ Sync/ LocalApi/ Diagnostics/
                 CompactArbiter.cs NotchState.cs
src/Atap.App/    Program.cs App.xaml(.cs) AppInfo.cs
                 Composition/  startup, hosts, discovery, lifetimes, safe mode
                 Notch/        windows, view models, menu, surfaces, input
                 Features/     one folder per feature, such as Panel, Alerts, CommandBar and Hotkeys
                 Settings/     the settings window, the generated form and the hand-made cards
                 Platform/     Windows sources, one folder per area
                 Widgets/      one folder per widget
                 Ui/           theme, tokens, the UI kit and the {loc:T} markup extension
                 Interop/      monitor and window P/Invokes
                 Updates/ Diagnostics/
src/Atap.Cli/    Program.cs PushClient.cs
changelog.d/     one changelog fragment per branch
```

## Extension model

Widgets, features and settings cards register themselves with an assembly attribute in their own folder. Adding one never edits a shared file. The app reads the attributes once at startup.

| Attribute | Type | Created |
| --- | --- | --- |
| `[assembly: AtapWidget(typeof(XDescriptor))]` | `IWidgetDescriptor` | The descriptor at startup. The widget only while it is enabled. |
| `[assembly: AtapFeature(typeof(XFeature))]` | `IFeatureDescriptor` | The descriptor at startup. The feature only while `IsEnabled` is true. |
| `[assembly: AtapSettingsCard(typeof(XCard))]` | `ISettingsCard` | Only while the settings window is open. |

A descriptor has a parameterless constructor and holds constants only. Widget code, view models, views and heavy packages are reached only inside `Create`. The guard test `CatalogIsolationTests` loads `Atap.dll` into its own load context and reads every descriptor. It also asks every feature what startup asks: its settings, its `EnabledBy` keys and `IsEnabled` in both modes. It fails if NAudio, MQTTnet, System.Management, Velopack or the Windows Runtime projections (`Microsoft.Windows.SDK.NET`, `WinRT.Runtime`) were loaded.

### Ids

- A type id matches `^[a-z][a-z0-9-]{1,31}$`, for example `media`.
- An instance id is the type id, or `<typeId>#<name>` for types that allow several instances, for example `json-url#sensor`. The name matches `^[a-z0-9][a-z0-9-]{0,31}$`.
- `enabledWidgets`, placements, `widgetSettings` and `compactPriority` in layout.json hold instance ids.
- `WidgetCatalog` holds the descriptors. A malformed type id or a duplicate is left out and logged. `TryResolve` checks an instance id against its type.

### A widget

`IWidgetDescriptor` describes the type: names (string keys), group, sizes, states, settings, capabilities, whether it allows several instances and the URI of its views. `IWidget` is one running instance. `WidgetBase` takes the id and descriptor from its `WidgetContext` and handles `IsLive`.

`WidgetContext` carries everything an instance may use: the UI thread, the clock, a scheduler, an HTTP client limited to its capabilities, text, an alert sink, commands, shared sources, app settings, power and network state, secrets, file storage, where it is visible right now and its lifetime. The scheduler, commands and shared sources are scoped. What an instance schedules, registers or leases ends when it stops. So do its handlers for changes to the command list.

### Views

Each widget ships `DataTemplate`s in its own `<Name>Views.xaml`. `WidgetViewLoader` merges that dictionary when the first instance of the type is created and removes it after the last one is gone. App.xaml does not list widget views, so the views of a disabled widget are never parsed.

A template gets a `PlacementViewModel`, and the widget view model is its `ViewModel` property. `WidgetTemplateSelector` looks up these keys and takes the first match:

1. `<typeId>.<state>.<slot>`, for example `media.Compact.Left`
2. `<typeId>.<state>.<size>`, for example `media.Expanded.Medium`
3. `<typeId>.<state>`
4. `<typeId>`

Keys are case-sensitive. The state, slot and size are the enum names in PascalCase. If nothing matches it uses `widget.missing`. Every template root sets `AutomationProperties.Name`, usually from the view model's `AccessibleSummary`.

## Composition

`App.OnStartup` builds everything in a fixed order, with no container:

1. The high contrast theme, before any window, so the first frame already has the right colours.
2. Safe mode check. If `%LOCALAPPDATA%\AtapData\crash.marker` exists, Atap runs the clock only, and features that turn themselves off for `AppMode.Safe` stay off. The marker is deleted.
3. `App.xaml.cs` creates the update service, or none in Store builds, and passes it to `AppServices.Create`. That reads settings.json, then layout.json, then sets the text and the UI culture. Then it makes the scheduler, Windows state, alerts, commands, shared sources, HTTP, secrets, the catalogs and the two hosts. No window opens yet.
4. An install that has layout.json but no settings.json is an upgrade, so `onboarding.done` is set. A new install gets `onboarding.done` set to false.
5. The second-launch listener (`Local\Atap.ShowSettings`). If it cannot start, only the second-launch signal is lost.
6. `WidgetHost` creates the enabled instances.
7. The app registers its own commands, `app.quit` and `updates.check`.
8. `NotchHost` shows the notch windows.
9. `FeatureHost` starts the features that are on.
10. `WidgetHost` starts the instances, one after another.
11. Updates.

Quit stops features, closes the notch windows, disposes the widgets and then the services.

Battery saver reaches the scheduler and HTTP: `AppServices` copies `IPowerState.SaverActive` to both whenever power state changes.

### What features and cards get

`FeatureContext` has the scoped scheduler, commands and leases, plus the app settings, the layout, the foreground state, the alert queue and `INotchControl`. It also has:

- `Widgets`, an `IWidgetHostView` with the catalog, the instances, `StatusOf`, `StatusChanged` and `RestartAsync`
- `SettingsCards`, the card types
- `Services`, the whole `AppServices`, for the settings window and the tray. It holds `NotchHost`, `Updates` and services that are not scoped.

A settings card gets a `SettingsCardContext` with the same `AppServices` and a lifetime that ends when the window closes.

### WidgetHost

- Applying a list of instance ids is a diff. Removed instances stop, new ones start and the rest keep running.
- Each instance has an `OwnerLifetime`. On stop the host first cancels its `Stopping` token, so a start that timed out gives up. Then it awaits `StopAsync` (10 second cap), disposes the lifetime and calls `DisposeAsync`.
- `StartAsync` has a 10 second cap. One start that throws or times out marks the instance Failed at once.
- Errors from scheduled callbacks and from `ReportError` are counted. Five within 60 seconds mark it Failed and stop it. `RestartAsync` runs a fresh instance.
- `RestartSoon` restarts one instance 500 ms after the last call, for settings changes.
- Apply, start, restart, the stop after five errors and dispose run one at a time, in order. A widget never stops while its start is still running, and a new instance starts only after the old one has let go of its commands and leases.
- An id from layout.json is logged without control characters and cut to 64 characters.
- Every notch window reports what it shows. Each instance gets the highest visibility over all windows.

### FeatureHost

A feature exists only while `IsEnabled` is true. It is checked again only when a key in its `EnabledBy` list changes. When a feature stops, the notch flags it set are cleared.

Starts, stops and dispose run one at a time, in order. A feature turned off while it is still starting stops only once its start has finished. `RunningIds` lists a feature only after its start has finished.

`systemClock` (in `Platform/Clock`) is always on, in safe mode too. It tells the scheduler when the Windows clock or time zone changes and when the PC wakes from sleep.

### Shared sources

Heavy Windows sources are shared through `ISharedServices.Acquire<T>()`. A source is created on the first lease through its static `Create`. After the last lease ends, it waits 30 seconds (one scheduler item) and is then disposed.

## The notch

### Surfaces

`NotchSurfaceResolver.Resolve` picks what a window shows. The first that applies wins:

1. Hidden: a rule says hide, or a full-screen app is in front
2. Edit
3. Panel
4. Expanded: hover or peek, with something to show
5. Alert
6. Slim
7. Compact: a live widget owns the notch and has a compact placement
8. Idle

Each surface has a template `surface.<Name>` in `Notch/Surfaces/<Name>/`. `Surfaces.xaml` merges them all. Surface files reach shared keys with DynamicResource, because each file is parsed on its own.

- Idle, Compact and Expanded show the slot layout from `SurfaceParts.xaml`.
- Panel shows the pages of widgets on a grid, with a new alert above and page dots below. A click on the notch opens it. A click, Esc or a click elsewhere closes it. The sideways wheel turns pages.
- Slim is a thin bar, with a short accent line while something is live. `NotchSizeFeature` offers Normal, Slim and Tucked away, and can hide the notch for an hour.
- Alert shows the current alert from `AlertViewModel`, as a screen reader live region.
- Edit shows only a badge while the layout editor window is open.

`NotchViewModel` builds the inputs from hover, the active layout, the live widgets, the full-screen state and the flags in `NotchControl`. It rebuilds content only when the surface, the compact owner or the layout content changes.

### NotchControl and input

Features change the notch through `INotchControl`:

- `SetFlag(flag, owner, on)`. A flag is on while any owner has it on.
- `SetSurfaceContent(surface, viewModel, owner)` gives a surface something to show. The Alert surface is shown only while it has content.
- `AddInputHandler` offers wheel, horizontal wheel, middle click, click and key input to handlers in order. With no handler, input does nothing.
- The notch takes keys only while Panel or Edit shows. It then drops `WS_EX_NOACTIVATE`, takes focus, and gives it back to the window that was in front when it closes. PageUp, PageDown and Ctrl+Tab turn pages, and Tab and the arrow keys move focus. Other keys go to the handlers. `NotchFocus` makes sure only one notch window across the monitors has the keyboard.
- Everywhere else the notch never takes focus, so a click on it leaves the app in front as it is.
- `IsSet`, `GetSurfaceContent` and `Changed` let a feature read what is set, for example whether a rule hides the notch.

### Menu

The right-click menu has the version, Position and Monitor, then every registered command that has a `MenuGroup`, then updates and Quit. Command items are built when the menu opens. The groups today are settings (Settings, the command bar and quick settings), layout (`layout.edit`), the notch size and the panel. "Edit layout (coming soon)" shows only when no command `layout.edit` is registered. All text follows the UI language.

### CompactArbiter

Several widgets can be live at once, but the compact notch shows one. `CompactArbiter.SelectOwner` walks the active compact priority list (instance ids) and returns the first instance that is live and whose type supports compact. An instance missing from the list never takes compact.

## Text

UI text is English or Bahasa Melayu. Each owner folder has `Strings/<area>.en.json` and `Strings/<area>.ms.json`, flat JSON with named placeholders such as `"in {minutes} min"`. Keys start with their area, for example `clock.name`.

- The files are embedded as `Atap.Strings.<area>.<lang>.json`. A target in Atap.App.csproj adds them after MSBuild assigns cultures, so `clock.en.json` stays in Atap.dll instead of a satellite assembly.
- `Localizer` lists the files once and loads an area on its first key. Fallback goes from the current language to English, then to the key itself. A missing key is logged once.
- In XAML, `{loc:T media.previous}` binds to the localizer's indexer, so text follows a language switch without a restart.
- The setting `language.choice` is `system`, `en` or `ms`. `system` picks Bahasa Melayu for any ms Windows display language. The UI culture is en-MY or ms-MY. The regional format stays as Windows has it.
- `StringsParityTests` checks that both languages have the same keys and placeholders, keys start with their area, and no value has an em dash or a semicolon.
- `SatelliteResourceLanguages` is `en;ms`, so no other culture folders ship.

UI copy uses British spelling. Code identifiers stay as they are.

## Files

| File | Holds |
| --- | --- |
| `%APPDATA%\Atap\layout.json` | Placement, enabled widgets, states, compact priority, widget settings and profiles |
| `%APPDATA%\Atap\settings.json` | App settings (`values`) and rules (`IAppSettings.Rules`). Created on the first start. |
| `%APPDATA%\Atap\data\<instanceId>\` | A widget's user data, such as notes and to-dos |
| `%APPDATA%\Atap\logs\` | Logs, one file a day |
| `%LOCALAPPDATA%\AtapData\state\<instanceId>\` | A widget's machine state, such as timer end times. Never synced. |
| `%LOCALAPPDATA%\AtapData\cache\http\` | The HTTP disk cache. File names are a SHA-256 of the request, so no URL shows. |
| `%LOCALAPPDATA%\AtapData\secrets.json` | Secrets, each one protected with Windows data protection (DPAPI) for the current user. Never synced. |
| `%LOCALAPPDATA%\AtapData\crash.marker` | Written when Atap closes because of an error |

`%LOCALAPPDATA%\Atap` is Velopack's install folder, so Atap keeps its own machine data in `AtapData`.

`SettingsStore` and `LayoutStore` share `JsonFileStore`. Writes wait 500 ms so a burst becomes one write. Each write goes to a `.tmp` file first and applies the change to the file as it is on disk, so hand edits survive. Both files are watched through `FolderWatch`, and an outside change is read after 300 ms of quiet. A file that cannot be parsed is set aside as `*.corrupt-<timestamp>.json`.

### layout.json and LayoutStore

The schema version is 3. Version 3 adds `profiles` and gives the panel pages:

```json
"panel": { "pages": [{ "id": "main", "name": "Main", "placements": [] }] }
```

`LayoutStore` handles the file and serves it as `ILayoutService`:

- A missing file is created with `DefaultLayout`.
- Writes go to a `.tmp` file first and then replace the real file.
- A file that cannot be parsed is renamed to `layout.corrupt-<timestamp>.json` and defaults are written.
- An older file is copied to `layout.v<n>.bak.json` first. The upgrade then runs on the raw JSON, so unknown fields survive. Version 2 to 3 moves the panel's placements into a page `main`. If the save fails, the upgrade runs again next time.
- A file from a newer Atap is used read-only and never written. If its shape cannot be read, defaults are used in memory and the file is still left alone.
- A profile cannot use the id `default`, which names the top level. Such a profile gets a free id such as `default-2`.
- When the placement changes, through the menu or anyone else using `ILayoutService`, `NotchHost` moves the windows.
- If the file could not be read at startup, or could not be backed up, it is left alone and nothing is written that session.
- `Update(change)` changes the layout in memory, then applies the same change to the file as it is on disk and saves that. Hand edits made while Atap runs are kept. If the file does not parse at that moment, nothing is written.
- Every model class keeps fields it does not know, so a newer file round-trips.
- `Active` is the active profile with inherited fields filled in. A profile may override the enabled widgets, the states and the compact priority.
- The offset is clamped to plus or minus 100000 on load.

## Scheduler

`Scheduler` is the one timer for scheduled work. It keeps its items in a min-heap and arms a single one-shot timer for the earliest due time plus slack. With no items, no timer exists.

- Each wake runs everything due within 20 ms of it, so nearby items share one wake. A repeating item may run a little early to join a wake. A one-shot never runs early.
- `EveryMinute` and `EverySecond` are aligned to the boundary plus 20 ms. Periods under one second are raised to one second, and periods over 366 days are cut to 366 days.
- `ScheduleOptions` sets the slack, whether the item can be deferred, and its `MissedRun` policy. `RefreshOptions(period)` gives network refreshes a slack of 10 % of the period, at most 5 minutes, and marks them deferrable.
- Under battery saver, deferrable periods are multiplied by 4. A change in saver state re-times the waiting items at once.
- A callback that throws is reported to its owner and the others still run.
- One timer waits at most a day. An item due later, such as an alarm two months away, arms again when that timer fires.
- `EverySecond` is only for a countdown on screen. `TimerTicks` uses it while the timer shows in Expanded or Panel, every minute otherwise, and nothing while the notch is hidden or idle.
- Widgets and features get a scoped scheduler. Their items end with their lifetime.

The `systemClock` feature calls `NotifyClockChanged` when the clock or time zone changes and `NotifyResumed` when the PC wakes. Aligned ticks then move to the next boundary. A repeating item never waits more than one period from now, so a clock set back an hour does not hold a 5 minute item for 65 minutes. `At` items keep their time, except those with `WallClock` set. Those come from a local time, such as an alarm at 07:00, so they run at once after a clock change or resume, and their owner works the time out again in the new time zone.

After sleep, missed items follow their `MissedRun` policy. With `Skip`, a missed item runs once, in a shared refresh 10 seconds after resume plus up to 5 seconds of jitter. With `RunOnce`, the item runs on the next turn. A missed one-shot is never dropped, so the work it waits for still happens. Code that shows the user a set time, such as an alarm, checks for itself whether that time passed during sleep.

## HTTP

`HttpDataService` creates its `HttpClient` on the first request. Each widget gets a copy limited to its capabilities.

- **Hosts.** https only, and only the hosts in `NetworkHosts`. A widget with `WidgetPermissions.UserUrls` may use any https host, and plain http only to a private address, checked after DNS. The next meeting and JSON URL widgets have it.
- **Limits.** By default a response may be 1 MB and a request may take 15 seconds, with 10 seconds to connect.
- **Redirects.** Followed by hand, at most 3, and only to the same origin.
- **Cache.** Memory and disk. The memory part keeps up to 8 MB, least recently used first. The disk part is in `%LOCALAPPDATA%\AtapData\cache\http`. A request with headers or a secret stays in memory only. Requests send `If-None-Match` and `If-Modified-Since` when the cache has a copy. Under battery saver or on a metered network, cached copies count as fresh four times as long.
- **Politeness.** Identical requests in flight are fetched once. Each host has a minimum gap between requests: 1 second by default, 60 seconds for e-solat.gov.my, 15 seconds for data.gov.my and 2 seconds for lrclib.net. After a failure the wait starts at 1 second and doubles up to 60 minutes, with 20 % jitter. `Retry-After` is honoured up to 24 hours. A 429 or 5xx counts as a failure. Other 4xx answers, bodies that are too big and refused redirects are not tried again until the widget restarts.
- **Offline.** While Windows reports no network, requests fail at once with `Offline` and the stale copy is served. A network change clears the network backoff, but not a wait the server asked for. `OfflineMode`, the Privacy offline switch, blocks every request and cancels those in flight.
- **Errors.** A network error, even one in the middle of the body, comes back as Failed and never as an exception.
- **Logs.** The `Attempted` event logs the host, or a hash for user and secret URLs. Paths are never logged.

## Alerts

`AlertQueue` holds one current alert and the waiting ones. Alerts have four priorities: Overlay, Normal, High (prayer times) and Critical.

- **How long they show.** Overlay 1.5 seconds, Normal 4 seconds, High 8 seconds. Critical stays until it is dismissed, for at most 60 seconds. An alert can set its own time from 1 to 60 seconds.
- **Order.** An alert with the same source and key replaces the old one. A more urgent alert goes in front. Overlays never wait.
- **Quiet.** Alerts are quiet while a rule asks for it, while "Pause alerts" from quick settings runs (1 hour), or while Windows is in Do not disturb, if `alerts.followWindows` is on. Windows is checked when an alert is about to show. Normal and High alerts then go to the Missed list, overlays are dropped, and Critical alerts still show.
- **Hidden notch.** While the notch is hidden for full screen or by a rule, a Critical alert waits, but its sound plays on time.
- **Missed.** The Missed list keeps the last 20. The Missed alerts widget shows them, and `alerts.clearMissed` clears them.
- **Rate limit.** Alerts from `atap push` are limited to one every 5 seconds, with a burst of 3.
- **Sound.** `alerts.sound` is the Windows sound, a file on the PC, or none. Network paths are refused, and a pushed alert cannot name a file. `alerts.stopSound` stops it.
- **Timers.** The only one is a one-shot for the end of the current alert.

Nothing shows a Windows toast.

## Placement and monitors

`PlacementGeometry.Compute` is a pure function in Core. From a monitor's pixel bounds, its scale and the saved placement, it returns the window rectangle, which corners are rounded and whether the notch is horizontal or vertical.

- There are five positions: top left, top centre, top right, left edge and right edge. There is no bottom position.
- The notch sits flush on the screen edge and is rounded on the inner side.
- Where it meets the screen edge, the notch flares out into a curved shoulder at each end. On the top edge the shoulders are left and right. On a side edge they are above and below. The window grows by the shoulder room on those sides.
- The shoulder radius is 15 pixels in the idle and compact states and 21 pixels when expanded, at 100% scale. It springs along with the size change.
- The shoulder room is the same in every state. It fits the largest radius and its spring overshoot, which comes to 22 pixels at 100% scale. Only the radius animates inside it.
- The top left and top right positions keep the notch 24 pixels from the side at 100% scale. That is wider than the shoulder room, so the shoulders stay on the monitor.
- The offset is in pixels at 100% scale and grows with the monitor's scale. The notch and its shoulders stay on the monitor. When the offset pushes the notch against the side of the monitor, the notch stops one shoulder room short of that side.
- Size changes spring over 0.6 seconds on the website's curve, `cubic-bezier(.22, 1.12, .36, 1)`, which overshoots by less than half a percent. New content fades in 0.12 seconds after the shape starts to change. Nothing runs once the animation ends.
- Side positions are vertical and stack the compact slots top to bottom.

`NotchShape.Outline` is the matching pure function for the shape. The window draws it as one filled path, so there is no seam. The path is rebuilt only when the size or the shoulder radius changes, so nothing redraws while the notch is idle.

`MonitorResolver` picks the target monitors. The mode is primary, specific or all. If the chosen monitor is not connected, it falls back to the primary one.

`NotchHost` keeps one notch window per target monitor. It lists monitors with a stable id taken from the display device path and reacts to display changes when Windows reports them. Each window has its own hover state and is placed in physical pixels with `SetWindowPos`.

Full-screen detection is system-wide, from the foreground window event. In All mode every notch hides when any app goes full screen.

## Low-resource rules

- No polling loops. React to events.
- Nothing redraws or animates while the notch is idle. `IdleAnimationTests` fails on `RepeatBehavior="Forever"` anywhere but the Edit surface.
- Disabled widgets and features are never created, and their views are never parsed.
- Timers only where no event can do the job. Almost all of them are scheduler items, so they share wakes:
  - Every minute: the clock, the world clock, and waktu solat while it is live.
  - One-shot `At` items: the next prayer, midnight for the Hijri date, alarms, the focus timer, water reminders, push expiry, the end of "hide for 1 hour" and "pause alerts for 1 hour", and the 10 seconds a plug-in keeps the battery live.
  - Network refreshes: weather every 3 hours, rain warnings every 20 minutes, a failed prayer fetch again after 30 minutes. All are deferrable.
  - While shown: Wi-Fi signal every 30 seconds, timers every second in Expanded or Panel, and the resource meter every 2 seconds while its Settings page is open.
  - Short waits: the widget restart debounce (500 ms) and the shared source grace (30 s).
- The few timers outside the scheduler run only after an event: `NotchHost` waits 250 ms for display changes to settle, the hover delays (120 ms to open, 300 ms to close), the panel hotkey check every 50 ms while the key is held, the store debounces, and the alert end. The update check runs 30 seconds after start and then every 24 hours.

## Startup, safe mode, updates and logging

`Program.Main` runs these steps in order:

1. Velopack startup hooks (not in Store builds). The uninstall hook removes the `cli` folder from the user's PATH if it was added.
2. A single-instance guard (`Local\Atap.SingleInstance`). A second launch signals `Local\Atap.ShowSettings` and exits. The first instance then runs the command `settings.open`, and the event can do nothing else.
3. Crash logging for unhandled, UI-thread and unobserved task exceptions.
4. WPF startup.

UI-thread exceptions are logged and marked handled, so one failed handler does not close the notch. If more than 20 arrive within 10 seconds, Atap logs once, writes the crash marker and shuts down. A terminating exception writes the marker too. The next start is in safe mode.

Logs go to `%APPDATA%\Atap\logs\atap-yyyyMMdd.log`. Only the newest 7 files are kept. A day's file stops growing at about 1 MB. The user profile path is replaced with `%USERPROFILE%`, and personal content such as song titles is not logged.

`UpdateService` runs only in non-Store builds that came from a Velopack release, the installer or the portable zip. It checks GitHub Releases 30 seconds after start and then every 24 hours, on one timer. It downloads in the background and applies the update when the app exits.

The Store build (`-p:AtapChannel=Store`) defines `STORE`, which removes Velopack and the update service. It has its own lock file, `packages.store.lock.json`.

## Custom widgets and sync

- **JSON URL** (`Widgets/JsonUrl`, shown as "Value from a link") reads one value from a link you enter, through the shared HTTP client. Plain http works only for an address on your own network.
- **Home Assistant** (`Widgets/HomeAssistant`) reads one value from an MQTT broker you enter. `Platform/Mqtt` shares one connection per broker. Plain MQTT works only for an address on your own network, and TLS works for any host.
- **Sync** (`Features/Sync`) is off by default. It reads and writes a sync file in your OneDrive folder and makes no network call of its own. The OneDrive app uploads the file. The merge logic is in `Core/Sync`.
- **Script** (`Widgets/Script`) runs a local program you approve and reads its output with the shared code in `Core/Custom`. The spec lives only in machine state, approval is an HMAC with a per-instance key kept with Windows data protection, and runs use `ArgumentList` with no shell, inside a job object that ends the tree after 10 seconds. Output is capped at 64 KB, runs are at least 30 seconds apart, programs and folders on network or USB drives are refused, even through a link, and it never runs in safe mode. The layout editor never reuses an instance id, so a new widget cannot inherit an old approval.

## Local API and CLI

`LocalApiServer` listens on the named pipe `Atap.v1.<user SID>`. Its ACL lets only the current user in and denies network logons, so nothing off the PC can reach it. A request may be 8 KB and must finish in 2 seconds, and a client may send 20 a second. The server runs only while "Let programs on this PC show progress" is on. That is a Machine setting and is off by default.

The `atap` CLI has `ping`, `push`, `end`, `alert` and `--version`. Pushed items reach the notch through `PushHub` and the Push widget. Pushed alerts go through the alert rate limit.
