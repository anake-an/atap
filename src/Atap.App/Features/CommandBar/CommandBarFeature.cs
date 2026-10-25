using System.Diagnostics;
using System.Windows;
using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Localization;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Features.CommandBar;
using Atap.Platform.Apps;

[assembly: AtapFeature(typeof(CommandBarDescriptor))]

namespace Atap.Features.CommandBar;

/// <summary>
/// The command bar: Ctrl+Alt+K by default, or "Command bar" in the notch's menu. Type to run any Atap action, do a
/// quick sum, convert units, open an app or search the web. Off in safe mode.
/// </summary>
internal sealed class CommandBarDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "commandBar";
    public const string SearchKey = "commandbar.search";

    /// <summary>Search pages by setting value, with <c>{q}</c> where the query goes.</summary>
    public static readonly IReadOnlyDictionary<string, string> SearchPages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["google"] = "https://www.google.com/search?q={q}",
        ["bing"] = "https://www.bing.com/search?q={q}",
        ["duckduckgo"] = "https://duckduckgo.com/?q={q}",
    };

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(SearchKey, "commandbar.search", SettingType.Choice, "google", ["google", "bing", "duckduckgo"])
        {
            SectionId = SettingsSections.Behavior,
            DescriptionKey = "commandbar.searchHint",
        },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [];

    public bool IsEnabled(SettingValues settings, AppMode mode) => mode != AppMode.Safe;

    public IAppFeature Create(FeatureContext context) =>
        new CommandBarFeature(context.Commands, context.Text, context.Settings, new ShellAppsFolder(), new CommandBarShell());
}

/// <summary>Opens and closes the command bar window. Tests pass their own.</summary>
internal interface ICommandBarShell : IDisposable
{
    bool IsOpen { get; }

    void Show(CommandBarViewModel viewModel);

    void Close();
}

/// <summary>Registers <c>commandbar.open</c> and makes a fresh command bar for each open. Holds no timer.</summary>
internal sealed class CommandBarFeature(ICommandRegistry commands, ILocalizer text, IAppSettingsView settings, IAppCatalog apps, ICommandBarShell shell) : IAppFeature
{
    private IDisposable? _registration;

    public Task StartAsync()
    {
        _registration = commands.Register(new AtapCommand(CommandBarViewModel.OpenCommandId, "commandbar.open", _ =>
        {
            Toggle();
            return Task.CompletedTask;
        })
        {
            MenuGroup = Features.SettingsWindow.SettingsWindowDescriptor.MenuGroup,
            MenuOrder = -85,
            ShowInCommandBar = false,
        });
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _registration?.Dispose();
        _registration = null;
        shell.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>Opens the command bar, or closes it when it is open.</summary>
    public CommandBarViewModel? Toggle()
    {
        if (shell.IsOpen)
        {
            shell.Close();
            return null;
        }
        var search = CommandBarDescriptor.SearchPages.GetValueOrDefault(settings.Values.GetString(CommandBarDescriptor.SearchKey))
            ?? CommandBarDescriptor.SearchPages["google"];
        var viewModel = new CommandBarViewModel(commands, text, apps, OpenUrl, Copy, search);
        shell.Show(viewModel);
        _ = viewModel.LoadAppsAsync();
        return viewModel;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Command bar: opening the browser failed: {ex.Message}");
        }
    }

    private static bool Copy(string value)
    {
        try
        {
            Clipboard.SetText(value);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
