# Contributing to Atap

Thanks for helping. This page covers setup, the workflow and how to add a widget.

By taking part you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md). For questions, see [SUPPORT.md](SUPPORT.md). To report a security problem, see [SECURITY.md](SECURITY.md) and do not open a public issue.

## Dev setup

You need Windows 10 (2004, build 19041) or Windows 11 and the .NET 10 SDK (10.0.100 or later).

```
git clone https://github.com/anake-an/atap.git
cd atap
dotnet build
dotnet test
dotnet run --project src/Atap.App
```

To check the Store variant, run `dotnet build src/Atap.App/Atap.App.csproj -p:AtapChannel=Store`. It must build with 0 warnings.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the code is organised.

## Packages

Packages come from nuget.org only (see `nuget.config`). Versions are set in `Directory.Packages.props`, and every project has a `packages.lock.json` that pins the full set. The Store variant of Atap.App uses `packages.store.lock.json`. CI restores in locked mode, so a lock file that is out of date fails the build.

After you add, remove or update a package, refresh the lock files and commit them with your change:

```
dotnet restore Atap.sln --force-evaluate
dotnet restore src/Atap.App/Atap.App.csproj -p:AtapChannel=Store --force-evaluate
dotnet restore Atap.sln
```

The last line puts the normal build back in place after the Store restore.

Dependabot only updates `packages.lock.json`. Before merging a Dependabot package PR, check it out, run the three commands above and push the refreshed `packages.store.lock.json`. Otherwise CI fails at the Store build step.

## Workflow

1. Open or find an issue first for anything bigger than a small fix.
2. Branch from `main`. Use a short name such as `feat/weather-widget` or `fix/hover-flicker`.
3. Make small commits. Push your branch to your fork and open a pull request against `main`. The pull request template lists what to check.
4. CI must pass. The maintainer reviews the change and may ask for edits. See [GOVERNANCE.md](GOVERNANCE.md) for how decisions are made.

## Commit messages

Use [Conventional Commits](https://www.conventionalcommits.org/). Types in use:

- `feat`: a new feature
- `fix`: a bug fix
- `docs`: documentation only
- `ci`: workflows and automation
- `refactor`: a change that fixes nothing and adds nothing
- `test`: tests only
- `chore`: everything else, such as dependency updates

Example: `fix(placement): keep the notch on its monitor at 150% scale`

## Pull request checklist

- [ ] `dotnet build` passes with no warnings
- [ ] `dotnet test` passes
- [ ] Text is in both English and Bahasa Melayu
- [ ] A changelog fragment in `changelog.d/` (see [changelog.d/README.md](changelog.d/README.md))
- [ ] The UI is checked in light, dark and high contrast
- [ ] The change keeps Atap light on resources (see below)
- [ ] Docs are updated if behaviour changed
- [ ] [PRIVACY.md](PRIVACY.md) is updated if the change sends anything off the PC
- [ ] No secrets or personal data in the code, tests or logs

## The low-resource rule

Atap must stay cheap to run, especially on laptops.

- Be event-driven. Do not add polling loops.
- Do not redraw or animate while the notch is idle.
- Do not create disabled widgets.
- Add a timer only when no event can do the job. Keep it as slow as the feature allows, and stop it when it has nothing to do.

A change that breaks these will not be merged.

## Adding a widget

A new widget lives in its own folder. You do not edit any existing file. The clock in `src/Atap.App/Widgets/Clock` is the smallest example. It ticks once a minute through the shared scheduler.

1. **Make the folder.** Add `src/Atap.App/Widgets/<Name>/` with:
   - `<Name>Descriptor.cs`, the descriptor and its registration
   - `<Name>Widget.cs`, the widget
   - `<Name>ViewModel.cs`, what the views bind to
   - `<Name>Views.xaml`, the views
   - `Strings/<area>.en.json` and `Strings/<area>.ms.json`, the text
2. **Write the descriptor.** Implement `IWidgetDescriptor` and register it in the same file with `[assembly: AtapWidget(typeof(MyDescriptor))]`. Give it a type id that matches `^[a-z][a-z0-9-]{1,31}$`, string keys for its name and description, a `WidgetGroup`, its sizes and states, its settings, its capabilities and the URI of its views, such as `/Atap;component/Widgets/Weather/WeatherViews.xaml`. The descriptor is created at startup even when the widget is off. Keep it to constants: reach your widget, view model and any package only inside `Create`.
3. **Write the widget.** Derive from `WidgetBase`. Take everything from the `WidgetContext` it gets. Use `Context.Scheduler` for timers, never a timer of your own. Use `Context.Http` for network data and `Context.Ui` for events that arrive on other threads. Add anything you open to `Context.Lifetime`, so it is released when the widget stops. Call `SetIsLive` when the widget wants the compact notch. Start listening in `StartAsync`, which must finish within 10 seconds, and stop in `StopAsync`. Use events, not polling.
4. **Add views.** Put a `DataTemplate` per state in `<Name>Views.xaml`. The template receives a placement object, and your view model is in its `ViewModel` property. Set `AutomationProperties.Name` on the template root, usually from an `AccessibleSummary` on your view model. Give every button a name and a visible focus ring, which the kit controls such as `IconButton` already have. Use the theme brushes with DynamicResource, so high contrast can swap them, and text with `{loc:T <key>}`. The dictionary is merged only while an instance exists. The app tries these keys in order and uses the first one it finds:
   1. `<typeId>.<state>.<slot>`, for example `media.Compact.Left`
   2. `<typeId>.<state>.<size>`, for example `media.Expanded.Medium`
   3. `<typeId>.<state>`
   4. `<typeId>`

   Case matters. The state, slot and size are the C# enum names in PascalCase: `Idle`, `Compact`, `Expanded` and `Panel`, then `Left`, `Center` and `Right`, then `Small`, `Medium` and `Large`. A key such as `media.compact.left` is never found, and the notch shows the `widget.missing` view instead. Every key in the file must start with your type id.
5. **Add text.** Both string files hold the same keys, and every key starts with the area name, for example `weather.name`. Use named placeholders such as `{minutes}`. Write British English and natural Malaysian Malay. No em dashes or semicolons.
6. **Try it.** Add it to a view in the layout editor (Settings, then Edit layout). Or add the instance id to `enabledWidgets` and place it in a state in `%APPDATA%\Atap\layout.json`. A widget that allows several instances uses ids such as `json-url#sensor`.
7. **Test it.** Put pure logic in `src/Atap.Core/<Area>/` with tests in `tests/Atap.Core.Tests/<Area>/`. Put widget tests in `tests/Atap.App.Tests/Widgets/<Name>/`. The guard tests check your views and strings on every build.

A widget that needs a new NuGet package needs the maintainer to add it first, in a separate `chore/deps-*` pull request. Open an issue to ask.

## Coding style

Style rules are in `.editorconfig`. In short: 4 spaces in C#, 2 in XAML, JSON and project files, file-scoped namespaces, CRLF line endings and UTF-8. Your editor should pick these up on its own.
