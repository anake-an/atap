using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Layout;
using Atap.Core.Localization;
using Atap.Core.Placement;
using Atap.Notch;
using Atap.Settings.Cards.Position;

[assembly: AtapSettingsCard(typeof(PositionCard))]

namespace Atap.Settings.Cards.Position;

/// <summary>Where the notch sits: a picture for each of the five spots, which screen, and Edit layout.</summary>
internal sealed class PositionCard : ISettingsCard
{
    public string Id => "position";

    public string SectionId => SettingsSections.Position;

    public int Order => 10;

    public string TitleKey => "position.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/Position/PositionViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context)
    {
        var services = context.Services;
        return new PositionCardViewModel(services.Layout, new NotchHostMonitors(services.NotchHost), services.Commands, services.Text);
    }
}

/// <summary>The connected monitors, as the notch host last saw them.</summary>
internal interface IMonitorList
{
    IReadOnlyList<DisplayMonitor> Monitors { get; }

    /// <summary>Raised on the UI thread when the monitors or the placement change.</summary>
    event EventHandler? Changed;
}

internal sealed class NotchHostMonitors(NotchHost host) : IMonitorList
{
    public IReadOnlyList<DisplayMonitor> Monitors => host.Monitors;

    public event EventHandler? Changed
    {
        add => host.Changed += value;
        remove => host.Changed -= value;
    }
}

/// <summary>One spot in the picture picker. The picture is a small screen with the notch drawn where it would sit.</summary>
internal sealed record AnchorOption(NotchAnchor Anchor, string Label)
{
    public bool IsVertical => Anchor is NotchAnchor.LeftCenter or NotchAnchor.RightCenter;

    public double NotchWidth => IsVertical ? 6 : 30;

    public double NotchHeight => IsVertical ? 22 : 6;

    public HorizontalAlignment Horizontal => Anchor switch
    {
        NotchAnchor.TopLeft or NotchAnchor.LeftCenter => HorizontalAlignment.Left,
        NotchAnchor.TopRight or NotchAnchor.RightCenter => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Center,
    };

    public VerticalAlignment Vertical => IsVertical ? VerticalAlignment.Center : VerticalAlignment.Top;

    /// <summary>The notch keeps a small gap from the corner on the top edge, as it does on screen.</summary>
    public Thickness NotchMargin => Anchor switch
    {
        NotchAnchor.TopLeft => new Thickness(10, 0, 0, 0),
        NotchAnchor.TopRight => new Thickness(0, 0, 10, 0),
        _ => new Thickness(0),
    };

    public override string ToString() => Label;
}

/// <summary>A choice in "Which screen".</summary>
internal sealed record ScreenOption(string Id, string Label, MonitorSelection? Selection)
{
    public override string ToString() => Label;
}

/// <summary>
/// Writes the placement through <see cref="ILayoutService"/>, so the notch host moves the windows and
/// saves layout.json. Follows changes made anywhere else, such as the notch menu.
/// </summary>
internal sealed class PositionCardViewModel : ObservableObject, IDisposable
{
    public const string EditLayoutCommandId = NotchMenuViewModel.EditLayoutCommandId;

    private static readonly (NotchAnchor Anchor, string Key)[] AnchorKeys =
    [
        (NotchAnchor.TopLeft, "notch.anchor.topLeft"),
        (NotchAnchor.TopCenter, "notch.anchor.topCenter"),
        (NotchAnchor.TopRight, "notch.anchor.topRight"),
        (NotchAnchor.LeftCenter, "notch.anchor.leftCenter"),
        (NotchAnchor.RightCenter, "notch.anchor.rightCenter"),
    ];

    private readonly ILayoutService _layout;
    private readonly IMonitorList _monitors;
    private readonly ICommandRegistry _commands;
    private readonly ILocalizer _text;
    private IReadOnlyList<AnchorOption> _anchors = [];
    private IReadOnlyList<ScreenOption> _screens = [];
    private string? _messageKey;

    public PositionCardViewModel(ILayoutService layout, IMonitorList monitors, ICommandRegistry commands, ILocalizer text)
    {
        _layout = layout;
        _monitors = monitors;
        _commands = commands;
        _text = text;
        EditLayoutCommand = new AsyncRelayCommand(() => _commands.TryRunAsync(EditLayoutCommandId));
        Rebuild();
        _layout.Changed += OnChanged;
        _monitors.Changed += OnChanged;
        _commands.Changed += OnCommandsChanged;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<AnchorOption> Anchors
    {
        get => _anchors;
        private set => SetProperty(ref _anchors, value);
    }

    public AnchorOption? SelectedAnchor
    {
        get => _anchors.FirstOrDefault(a => a.Anchor == _layout.Document.Placement.Anchor);
        set
        {
            if (value is null || value.Anchor == _layout.Document.Placement.Anchor)
                return;
            // The offset runs along the old edge and means nothing on the new one.
            Save(document =>
            {
                document.Placement.Anchor = value.Anchor;
                document.Placement.Offset = 0;
            });
        }
    }

    public string AnchorsName => _text["position.anchors"];

    public IReadOnlyList<ScreenOption> Screens
    {
        get => _screens;
        private set => SetProperty(ref _screens, value);
    }

    public ScreenOption? SelectedScreen
    {
        get => _screens.FirstOrDefault(s => s.Id == CurrentScreenId());
        set
        {
            if (value?.Selection is not { } selection || value.Id == CurrentScreenId())
                return;
            Save(document => document.Placement.Monitor = selection);
        }
    }

    public string ScreenLabel => _text["position.screen"];

    public string ScreenHint => _text["position.screenHint"];

    public bool CanEditLayout => _commands.Contains(EditLayoutCommandId);

    public IAsyncRelayCommand EditLayoutCommand { get; }

    /// <summary>Shown when a change could not be saved.</summary>
    public string? Message => _messageKey is null ? null : _text[_messageKey];

    public bool HasMessage => _messageKey is not null;

    public void Dispose()
    {
        _layout.Changed -= OnChanged;
        _monitors.Changed -= OnChanged;
        _commands.Changed -= OnCommandsChanged;
        _text.LanguageChanged -= OnLanguageChanged;
    }

    private void Save(Action<LayoutDocument> change)
    {
        var result = _layout.Update(change);
        _messageKey = result == LayoutWriteResult.Applied ? null : "position.notSaved";
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasMessage));
        Refresh();
    }

    private string CurrentScreenId()
    {
        var selection = _layout.Document.Placement.Monitor;
        return selection.Mode switch
        {
            MonitorMode.All => "all",
            MonitorMode.Specific => $"monitor:{selection.DeviceId}",
            _ => "primary",
        };
    }

    private void Rebuild()
    {
        Anchors = AnchorKeys.Select(a => new AnchorOption(a.Anchor, _text[a.Key])).ToList();

        var screens = new List<ScreenOption> { new("primary", _text["position.screen.primary"], MonitorSelection.Primary()) };
        foreach (var monitor in _monitors.Monitors)
            screens.Add(new($"monitor:{monitor.DeviceId}", monitor.FriendlyName, MonitorSelection.Specific(monitor)));

        // The chosen monitor is unplugged: the notch is on the main screen for now, but the choice stays visible.
        var selection = _layout.Document.Placement.Monitor;
        if (selection.Mode == MonitorMode.Specific && MonitorResolver.Find(selection.DeviceId, _monitors.Monitors) is null)
        {
            var name = string.IsNullOrWhiteSpace(selection.FriendlyName) ? _text["notch.monitor.chosen"] : selection.FriendlyName;
            screens.Add(new($"monitor:{selection.DeviceId}", _text.Format("position.screen.notConnected", ("name", name)), Selection: null));
        }
        screens.Add(new("all", _text["position.screen.all"], MonitorSelection.All()));
        // A new list makes the box drop its selection, so it is kept when the same screens are listed. Moving
        // the notch to another spot left "Which screen" blank before.
        if (!screens.Select(s => (s.Id, s.Label)).SequenceEqual(_screens.Select(s => (s.Id, s.Label))))
            Screens = screens;
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(SelectedAnchor));
        OnPropertyChanged(nameof(SelectedScreen));
    }

    private void OnChanged(object? sender, EventArgs e) => Rebuild();

    private void OnCommandsChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(CanEditLayout));

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Rebuild();
        OnPropertyChanged(nameof(AnchorsName));
        OnPropertyChanged(nameof(ScreenLabel));
        OnPropertyChanged(nameof(ScreenHint));
        OnPropertyChanged(nameof(Message));
    }
}
