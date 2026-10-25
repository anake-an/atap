using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core;
using Atap.Core.Localization;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Platform.Toggles;
using Atap.Widgets.QuickToggles;

[assembly: AtapWidget(typeof(QuickTogglesDescriptor))]

namespace Atap.Widgets.QuickToggles;

/// <summary>Dark mode, Bluetooth, microphone mute and the slim notch. Constants only.</summary>
public sealed class QuickTogglesDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "quick-toggles";

    public string TypeId => TypeIdValue;

    public string NameKey => "toggles.name";

    public string DescriptionKey => "toggles.description";

    public WidgetGroup Group => WidgetGroup.System;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/QuickToggles/QuickTogglesViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new QuickTogglesWidget(context, [new DarkModeToggle(), new BluetoothToggle(), new MicMuteToggle()]);
}

/// <summary>Reads each toggle when shown. A toggle Windows does not have, or a missing command, is hidden.</summary>
public sealed class QuickTogglesWidget : WidgetBase
{
    public const string SlimCommand = "notch.size.slim";
    public const string NormalCommand = "notch.size.normal";
    public const string SizeKey = "notch.size";

    private static readonly Dictionary<string, (string Label, string Icon)> Look = new(StringComparer.Ordinal)
    {
        ["darkMode"] = ("toggles.darkMode", "icon.moon"),
        ["bluetooth"] = ("toggles.bluetooth", "icon.bolt"),
        ["micMute"] = ("toggles.micMute", "icon.mic"),
        ["slim"] = ("toggles.slim", "icon.minus"),
    };

    private readonly IReadOnlyList<ISystemToggle> _toggles;
    private readonly QuickTogglesViewModel _viewModel;
    private bool _refreshing;

    internal QuickTogglesWidget(WidgetContext context, IReadOnlyList<ISystemToggle> toggles)
        : base(context)
    {
        _toggles = toggles;
        _viewModel = new QuickTogglesViewModel(context.Text);
    }

    public override object ViewModel => _viewModel;

    public override async Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        Context.Visibility.Changed += OnVisibilityChanged;
        Context.App.Changed += OnAppChanged;
        await RefreshAsync();
    }

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnVisibilityChanged;
        Context.App.Changed -= OnAppChanged;
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    internal Task RefreshAsync() => Refresh(null);

    private async void OnVisibilityChanged(object? sender, EventArgs e)
    {
        if (Context.Visibility.Value is WidgetVisibility.Expanded or WidgetVisibility.Panel)
            await Refresh(null);
    }

    private async void OnAppChanged(object? sender, Core.Settings.SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(SizeKey))
            await Refresh(null);
    }

    private async Task Refresh(string? failedId)
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            var tiles = new List<ToggleTile>();
            foreach (var toggle in _toggles)
            {
                if (await toggle.ReadAsync() is { } on)
                    tiles.Add(Tile(toggle.Id, on, failedId == toggle.Id, () => SetAsync(toggle, !on)));
            }
            if (Context.Commands.Contains(SlimCommand) && Context.Commands.Contains(NormalCommand))
            {
                var slim = Context.App.Values.GetString(SizeKey) == "slim";
                tiles.Add(Tile("slim", slim, false, () => Context.Commands.TryRunAsync(slim ? NormalCommand : SlimCommand)));
            }
            _viewModel.Show(tiles);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task SetAsync(ISystemToggle toggle, bool on)
    {
        var done = await toggle.SetAsync(on);
        await Refresh(done ? null : toggle.Id);
    }

    private ToggleTile Tile(string id, bool on, bool failed, Func<Task> flip)
    {
        var (label, icon) = Look[id];
        return new ToggleTile(id, label, icon, on, failed, new AsyncRelayCommand(flip));
    }
}

/// <summary>One toggle. <see cref="Failed"/> when Windows refused the last change.</summary>
public sealed record ToggleTile(string Id, string LabelKey, string IconKey, bool IsOn, bool Failed, IAsyncRelayCommand Flip)
{
    public string Label { get; init; } = string.Empty;
}

public sealed class QuickTogglesViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private IReadOnlyList<ToggleTile> _tiles = [];

    public QuickTogglesViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<ToggleTile> Tiles { get; private set; } = [];

    public bool HasFailure => Tiles.Any(t => t.Failed);

    public string AccessibleSummary => _text.Format("toggles.accessible",
        ("on", string.Join(", ", Tiles.Where(t => t.IsOn).Select(t => t.Label))));

    public void Show(IReadOnlyList<ToggleTile> tiles)
    {
        _tiles = tiles;
        Tiles = tiles.Select(t => t with { Label = _text[t.LabelKey] }).ToList();
        OnPropertyChanged(string.Empty);
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => Show(_tiles);
}
