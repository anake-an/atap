using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core;
using Atap.Core.Localization;
using Atap.Core.LocalApi;
using Atap.Core.Shared;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Platform.Ipc;
using Atap.Widgets.Push;

[assembly: AtapWidget(typeof(PushDescriptor))]

namespace Atap.Widgets.Push;

/// <summary>
/// Progress that programs on this PC push through the local API, such as a build or a backup. Up to 5 at a time,
/// each ending at its time to live. Created at startup whether it is enabled or not, so it holds constants only.
/// </summary>
public sealed class PushDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "push";

    public string TypeId => TypeIdValue;

    public string NameKey => "push.name";

    public string DescriptionKey => "push.description";

    public WidgetGroup Group => WidgetGroup.Productivity;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Compact, NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Push/PushViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new PushWidget(context);
}

/// <summary>Shows the shared <see cref="PushHub"/>. Live while anything is pushed, so it can take the compact notch.</summary>
public sealed class PushWidget(WidgetContext context) : WidgetBase(context)
{
    private readonly PushViewModel _viewModel = new(context.Text);
    private Lease<PushHub>? _hub;

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _hub = Context.Shared.Acquire<PushHub>();
        _hub.Value.Changed += OnHubChanged;
        Show();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        if (_hub is not null)
        {
            _hub.Value.Changed -= OnHubChanged;
            _hub.Dispose();
            _hub = null;
        }
        _viewModel.Show([]);
        SetIsLive(false);
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void OnHubChanged(object? sender, EventArgs e) => Show();

    private void Show()
    {
        var items = _hub?.Value.Activities.Items ?? [];
        _viewModel.Show(items.OrderByDescending(a => a.UpdatedAt).ToList());
        SetIsLive(items.Count > 0);
    }
}

/// <summary>One pushed activity as the notch shows it.</summary>
public sealed record PushItemViewModel(string Title, string? Detail, double? Progress, string Glyph, Brush? Accent, string StateText)
{
    public bool HasDetail => Detail is not null;

    public bool HasProgress => Progress is not null;

    /// <summary>"40%" while running with progress, otherwise the state, such as "Done".</summary>
    public string Short => Progress is { } progress && StateText.Length == 0
        ? progress.ToString("0", CultureInfo.CurrentCulture) + "%"
        : StateText;
}

public sealed class PushViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private IReadOnlyList<PushActivity> _activities = [];

    public PushViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<PushItemViewModel> Items { get; private set; } = [];

    public PushItemViewModel? Latest => Items.Count > 0 ? Items[0] : null;

    public bool HasItems => Items.Count > 0;

    public string AccessibleSummary => Latest is { } latest
        ? _text.Format("push.accessible", ("title", latest.Title), ("status", latest.Short.Length > 0 ? latest.Short : _text["push.state.running"]))
        : _text["push.empty"];

    public void Show(IReadOnlyList<PushActivity> activities)
    {
        _activities = activities;
        Items = activities.Select(ToItem).ToList();
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(Latest));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(AccessibleSummary));
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => Show(_activities);

    private PushItemViewModel ToItem(PushActivity activity)
    {
        var detail = activity.Subtitle;
        if (activity.Source is { } source)
        {
            var from = _text.Format("push.from", ("source", source));
            detail = detail is null ? from : $"{detail} · {from}";
        }
        var state = activity.State switch
        {
            PushState.Done => _text["push.state.done"],
            PushState.Failed => _text["push.state.failed"],
            _ => activity.Progress is null ? _text["push.state.running"] : string.Empty,
        };
        return new PushItemViewModel(activity.Title, detail, activity.Progress, Glyph(activity), Accent(activity), state);
    }

    /// <summary>The icon font glyph for an activity. Done and failed show a tick and a cross whatever the icon.</summary>
    public static string Glyph(PushActivity activity) => activity.State switch
    {
        PushState.Done => "",
        PushState.Failed => "",
        _ => activity.Icon switch
        {
            PushIcon.Check => "",
            PushIcon.Warning => "",
            PushIcon.Error => "",
            PushIcon.Download => "",
            PushIcon.Upload => "",
            PushIcon.Sync => "",
            PushIcon.Backup => "",
            PushIcon.Server => "",
            PushIcon.Build => "",
            PushIcon.Timer => "",
            PushIcon.Bell => "",
            PushIcon.Terminal => "",
            _ => "",
        },
    };

    private static Brush? Accent(PushActivity activity)
    {
        var key = activity.State switch
        {
            PushState.Failed => "SystemAccentBrush",
            _ => activity.Accent switch
            {
                PushAccent.Amber => "ProductivityAccentBrush",
                PushAccent.Coral => "SystemAccentBrush",
                PushAccent.Blue => "CustomAccentBrush",
                PushAccent.Violet => "MalaysiaAccentBrush",
                _ => "MediaAccentBrush",
            },
        };
        return Application.Current?.TryFindResource(key) as Brush;
    }
}
