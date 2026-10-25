using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Composition;
using Atap.Core.Layout;
using Atap.Core.Localization;
using Atap.Core.Widgets;
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
        return new NetworkFeaturesViewModel(services.Catalog, services.Layout, services.Updates?.IsSupported, services.Text);
    }
}

/// <summary>One thing that goes online.</summary>
/// <param name="Name">What the user calls it.</param>
/// <param name="Detail">Where it connects, in plain words.</param>
/// <param name="Note">The privacy note its owner gave, if any.</param>
/// <param name="IsOn">True while it is on the notch (or, for update checks, while they run).</param>
/// <param name="StateText">"On" or "Off".</param>
internal sealed record NetworkUse(string Name, string Detail, string? Note, bool IsOn, string StateText)
{
    public bool HasNote => Note is not null;

    /// <summary>What a screen reader says for the row.</summary>
    public override string ToString() => $"{Name}, {StateText}. {Detail}";
}

/// <summary>Rebuilt when the layout changes, so the on and off states follow the notch.</summary>
internal sealed class NetworkFeaturesViewModel : ObservableObject, IDisposable
{
    private readonly WidgetCatalog _catalog;
    private readonly ILayoutService _layout;
    private readonly bool? _updateChecks;
    private readonly ILocalizer _text;
    private IReadOnlyList<NetworkUse> _items = [];

    /// <param name="catalog">Every widget type, enabled or not.</param>
    /// <param name="layout">Which instances the active profile has on.</param>
    /// <param name="updateChecks">Whether Atap checks for updates itself. Null in Store builds, where the Store updates Atap.</param>
    /// <param name="text">UI text.</param>
    public NetworkFeaturesViewModel(WidgetCatalog catalog, ILayoutService layout, bool? updateChecks, ILocalizer text)
    {
        _catalog = catalog;
        _layout = layout;
        _updateChecks = updateChecks;
        _text = text;
        Rebuild();
        _layout.Changed += OnChanged;
        _text.LanguageChanged += OnChanged;
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
            var parts = new List<string>();
            if (capabilities.NetworkHosts.Count > 0)
                parts.Add(_text.Format("networkuse.hosts", ("hosts", string.Join(", ", capabilities.NetworkHosts))));
            if ((capabilities.Permissions & WidgetPermissions.UserUrls) != 0)
                parts.Add(_text["networkuse.userUrls"]);
            var isOn = enabledTypes.Contains(descriptor.TypeId);
            items.Add(new NetworkUse(
                _text[descriptor.NameKey],
                string.Join(" ", parts),
                capabilities.PrivacyNoteKey is { } note ? _text[note] : null,
                isOn,
                _text[isOn ? "networkuse.on" : "networkuse.off"]));
        }
        if (_updateChecks is { } checks)
            items.Add(new NetworkUse(_text["networkuse.updates"], _text["networkuse.updatesDetail"], null, checks, _text[checks ? "networkuse.on" : "networkuse.off"]));
        Items = items;
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(EmptyText));
    }

    private void OnChanged(object? sender, EventArgs e) => Rebuild();
}
