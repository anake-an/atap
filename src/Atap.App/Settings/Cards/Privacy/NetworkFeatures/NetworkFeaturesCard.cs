using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Composition;
using Atap.Core.Layout;
using Atap.Core.Localization;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Features.Ai;
using Atap.Settings.Cards.Privacy.NetworkFeatures;

[assembly: AtapSettingsCard(typeof(NetworkFeaturesCard))]

namespace Atap.Settings.Cards.Privacy.NetworkFeatures;

/// <summary>What uses the internet, built from each widget type's declared capabilities, and whether it is on.</summary>
internal sealed class NetworkFeaturesCard : ISettingsCard
{
    public string Id => "networkFeatures";

    public string SectionId => SettingsSections.Privacy;

    public int Order => 10;

    public string TitleKey => "networkuse.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/Privacy/NetworkFeatures/NetworkFeaturesViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context)
    {
        var services = context.Services;
        return new NetworkFeaturesViewModel(services.Catalog, services.Layout, services.Updates?.IsSupported, services.Text, services.Settings);
    }
}

/// <summary>One thing that goes online.</summary>
/// <param name="Name">What the user calls it.</param>
/// <param name="Detail">Where it goes online, in plain words.</param>
/// <param name="Note">The privacy note its owner gave, if any.</param>
/// <param name="IsOn">True while it is on the notch (or, for update checks, while they run).</param>
/// <param name="StateText">"On" or "Off".</param>
/// <param name="TurnOnHint">Where to turn it on, while it is off and there is a place for that. Null otherwise.</param>
internal sealed record NetworkUse(string Name, string Detail, string? Note, bool IsOn, string StateText, string? TurnOnHint = null)
{
    public bool HasNote => Note is not null;

    public bool HasTurnOnHint => TurnOnHint is not null;

    /// <summary>What a screen reader says for the row.</summary>
    public override string ToString() => TurnOnHint is null ? $"{Name}, {StateText}. {Detail}." : $"{Name}, {StateText}. {Detail}. {TurnOnHint}.";
}

/// <summary>Rebuilt when the layout changes, so the on and off states follow the notch.</summary>
internal sealed class NetworkFeaturesViewModel : ObservableObject, IDisposable
{
    private readonly WidgetCatalog _catalog;
    private readonly ILayoutService _layout;
    private readonly bool? _updateChecks;
    private readonly ILocalizer _text;
    private readonly IAppSettingsView? _settings;
    private IReadOnlyList<NetworkUse> _items = [];

    /// <param name="catalog">Every widget type, enabled or not.</param>
    /// <param name="layout">Which instances the active profile has on.</param>
    /// <param name="updateChecks">Whether Atap checks for updates itself. Null in Store builds, where the Store updates Atap.</param>
    /// <param name="text">UI text.</param>
    /// <param name="settings">Whether Ask AI is on. Ask AI is left out of the list when null.</param>
    public NetworkFeaturesViewModel(WidgetCatalog catalog, ILayoutService layout, bool? updateChecks, ILocalizer text, IAppSettingsView? settings = null)
    {
        _catalog = catalog;
        _layout = layout;
        _updateChecks = updateChecks;
        _text = text;
        _settings = settings;
        Rebuild();
        _layout.Changed += OnChanged;
        _text.LanguageChanged += OnChanged;
        if (_settings is not null)
            _settings.Changed += OnSettingsChanged;
    }

    public IReadOnlyList<NetworkUse> Items
    {
        get => _items;
        private set
        {
            if (SetProperty(ref _items, value))
                OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsEmpty => _items.Count == 0;

    public string Hint => _text["networkuse.hint"];

    public string EmptyText => _text["networkuse.none"];

    public void Dispose()
    {
        _layout.Changed -= OnChanged;
        _text.LanguageChanged -= OnChanged;
        if (_settings is not null)
            _settings.Changed -= OnSettingsChanged;
    }

    /// <summary>A widget type is listed when its capabilities allow the network or name a host.</summary>
    internal static bool UsesNetwork(WidgetCapabilities capabilities) =>
        (capabilities.Permissions & (WidgetPermissions.Network | WidgetPermissions.UserUrls)) != 0 || capabilities.NetworkHosts.Count > 0;

    private void Rebuild()
    {
        var enabledTypes = _layout.Active.EnabledWidgets
            .Select(id => WidgetInstanceId.TryParse(id, out var parsed) ? parsed.TypeId : null)
            .Where(t => t is not null)
            .ToHashSet(StringComparer.Ordinal);

        var items = new List<NetworkUse>();
        foreach (var descriptor in _catalog.Descriptors.Where(d => UsesNetwork(d.Capabilities)))
        {
            var capabilities = descriptor.Capabilities;
            var isOn = enabledTypes.Contains(descriptor.TypeId);
            items.Add(new NetworkUse(
                _text[descriptor.NameKey],
                Where(capabilities),
                capabilities.PrivacyNoteKey is { } note ? _text[note] : null,
                isOn,
                _text[isOn ? "networkuse.on" : "networkuse.off"],
                // Widgets are turned on from the What it shows page.
                isOn ? null : _text["networkuse.turnOn"]));
        }
        if (_settings is not null)
        {
            // Ask AI is turned on from its card on the Home page.
            var askAi = _settings.Values.GetBool(AiDescriptor.EnabledKey);
            items.Add(new NetworkUse(_text["networkuse.askAi"], _text["networkuse.askAiDetail"], null, askAi,
                _text[askAi ? "networkuse.on" : "networkuse.off"], askAi ? null : _text["networkuse.askAiTurnOn"]));
        }
        if (_updateChecks is { } checks)
            items.Add(new NetworkUse(_text["networkuse.updates"], _text["networkuse.updatesDetail"], null, checks, _text[checks ? "networkuse.on" : "networkuse.off"]));
        Items = items;
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>"Uses the internet: " and the named sites, addresses the user types in, or both.</summary>
    private string Where(WidgetCapabilities capabilities)
    {
        var hosts = string.Join(", ", capabilities.NetworkHosts);
        var typed = (capabilities.Permissions & WidgetPermissions.UserUrls) != 0;
        if (hosts.Length == 0)
            return _text[typed ? "networkuse.userUrls" : "networkuse.some"];
        return _text.Format(typed ? "networkuse.hostsAndUserUrls" : "networkuse.hosts", ("hosts", hosts));
    }

    private void OnChanged(object? sender, EventArgs e) => Rebuild();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(AiDescriptor.EnabledKey))
            Rebuild();
    }
}
