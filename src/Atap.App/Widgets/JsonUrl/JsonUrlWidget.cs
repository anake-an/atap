using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core;
using Atap.Core.Custom;
using Atap.Core.Localization;
using Atap.Core.Net;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Ui.Controls;
using Atap.Widgets.JsonUrl;

[assembly: AtapWidget(typeof(JsonUrlDescriptor))]

namespace Atap.Widgets.JsonUrl;

/// <summary>One value from any link that gives JSON. Constants only.</summary>
public sealed class JsonUrlDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "json-url";
    public const string UrlKey = "url";
    public const string TitleKey = "title";
    public const string ShowKey = "show";
    public const string PathKey = "path";
    public const string MaxKey = "max";
    public const string UnitKey = "unit";
    public const string AlertsKey = "alerts";
    public const string BadSideKey = "badSide";
    public const string WarnAtKey = "warnAt";
    public const string CriticalAtKey = "criticalAt";
    public const string IntervalKey = "interval";
    public const string TestKey = "test";
    public const string ItemLabelKey = "itemLabel";
    public const string ItemValueKey = "itemValue";
    public const string HeaderKey = "header";

    /// <summary>The Test button and preview in this widget's settings, from JsonUrlViews.xaml.</summary>
    public const string TesterEditorKey = "json-url.tester";

    public string TypeId => TypeIdValue;

    public string NameKey => "json-url.name";

    public string DescriptionKey => "json-url.description";

    public WidgetGroup Group => WidgetGroup.Custom;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Compact, NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(UrlKey, "json-url.url", SettingType.Url, null) { DescriptionKey = "json-url.urlHint", Required = true },
        new(TitleKey, "json-url.title", SettingType.String, "") { DescriptionKey = "json-url.titleHint", MaxLength = 40 },
        new(ShowKey, "json-url.show", SettingType.Choice, "number", ["number", "text", "progress", "list", "status"]),
        new(PathKey, "json-url.path", SettingType.String, "") { DescriptionKey = "json-url.pathHint", MaxLength = 512 },
        new(MaxKey, "json-url.max", SettingType.Double, 100.0) { DescriptionKey = "json-url.maxHint", VisibleWhen = (ShowKey, "progress") },
        new(UnitKey, "json-url.unit", SettingType.String, "") { DescriptionKey = "json-url.unitHint", MaxLength = ValueTemplateSpec.MaxUnitLength },
        new(AlertsKey, "json-url.alerts", SettingType.Bool, false) { DescriptionKey = "json-url.alertsHint" },
        new(BadSideKey, "json-url.badSide", SettingType.Choice, "above", ["above", "below"]) { VisibleWhen = (AlertsKey, true) },
        new(WarnAtKey, "json-url.warnAt", SettingType.Double, 80.0) { VisibleWhen = (AlertsKey, true) },
        new(CriticalAtKey, "json-url.criticalAt", SettingType.Double, 90.0) { VisibleWhen = (AlertsKey, true) },
        new(IntervalKey, "json-url.interval", SettingType.Int, 300)
        {
            DescriptionKey = "json-url.intervalHint", Min = 60, Max = 86_400, Step = 30, UnitKey = "json-url.seconds",
        },
        new(TestKey, "json-url.test", SettingType.Custom, null) { DescriptionKey = "json-url.testHint", EditorKey = TesterEditorKey },
        new(ItemLabelKey, "json-url.itemLabel", SettingType.String, "")
        {
            DescriptionKey = "json-url.itemLabelHint", MaxLength = 512, Advanced = true, VisibleWhen = (ShowKey, "list"),
        },
        new(ItemValueKey, "json-url.itemValue", SettingType.String, "")
        {
            DescriptionKey = "json-url.itemValueHint", MaxLength = 512, Advanced = true, VisibleWhen = (ShowKey, "list"),
        },
        new(HeaderKey, "json-url.header", SettingType.Secret, null) { DescriptionKey = "json-url.headerHint", Advanced = true },
    ];

    public WidgetCapabilities Capabilities { get; } = new(WidgetPermissions.Network | WidgetPermissions.UserUrls, [], "json-url.privacy");

    public bool AllowsMultipleInstances => true;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/JsonUrl/JsonUrlViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new JsonUrlWidget(context);
}

/// <summary>
/// Fetches the link every interval, at least 60 seconds, and maps the reply to a value with
/// <see cref="ValueTemplate"/>. A failed fetch or an unreadable reply keeps the last good value and marks it
/// out of date. Thresholds or status words at Warning or Critical make it live, so the compact notch shows it.
/// The sign-in header comes from the secret store and its replies stay in memory.
/// </summary>
public sealed class JsonUrlWidget : WidgetBase
{
    private readonly JsonUrlViewModel _viewModel;
    private JsonUrlSetup? _setup;
    private ValueView? _rendered;
    private ValueView? _good;
    private CancellationTokenSource? _stopping;
    private IDisposable? _refresh;

    public JsonUrlWidget(WidgetContext context)
        : base(context) => _viewModel = new JsonUrlViewModel(context.Text);

    public override object ViewModel => _viewModel;

    /// <summary>How often this instance fetches. Zero until it starts with a usable link.</summary>
    internal TimeSpan Interval => _setup?.Interval ?? TimeSpan.Zero;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _stopping = new CancellationTokenSource();
        _setup = JsonUrlSetup.Read(settings, Context.Secrets, out var problem);
        if (_setup is not { } setup)
        {
            _viewModel.ShowMessage(problem ?? "json-url.noLink");
            return Task.CompletedTask;
        }
        _viewModel.SetTitle(setup.Title);
        var slack = TimeSpan.FromSeconds(Math.Min(30, setup.Interval.TotalSeconds / 10));
        _refresh = Context.Scheduler.Every(setup.Interval, () => _ = LoadAsync(), new ScheduleOptions(slack, Deferrable: true));
        _ = LoadAsync();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        _refresh?.Dispose();
        _refresh = null;
        _stopping?.Cancel();
        _stopping?.Dispose();
        _stopping = null;
        _setup = null;
        _rendered = null;
        _good = null;
        SetIsLive(false);
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private async Task LoadAsync()
    {
        if (_stopping is not { } stopping || _setup is not { } setup)
            return;
        FetchResult result;
        try
        {
            // Half the interval, so a copy fetched just before the timer never makes it skip a refresh.
            result = await Context.Http.GetAsync(setup.Request(setup.Interval / 2), stopping.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (stopping.IsCancellationRequested)
            return;
        Apply(setup, result);
    }

    private void Apply(JsonUrlSetup setup, FetchResult result)
    {
        if (result.Status is FetchStatus.Failed or FetchStatus.Blocked)
        {
            if (_good is { } last)
                _viewModel.ShowValue(last with { Stale = true });
            else
                _viewModel.ShowMessage(JsonUrlSetup.FailureKey(result.Error));
        }
        else
        {
            var payload = PayloadReader.Read(result.Body.Span, PayloadFormat.Auto, PayloadReader.JsonUrlMaxBytes);
            var view = setup.Template.Render(payload, _rendered, Context.Text.Culture);
            _rendered = view;
            if (view.HasValue)
            {
                _good = view;
                // A Stale result is the cached reply, served because the fetch failed.
                _viewModel.ShowValue(result.Status == FetchStatus.Stale ? view with { Stale = true } : view);
            }
            else
            {
                _viewModel.ShowValue(_good is { } last ? last with { Stale = true } : view);
            }
        }
        SetIsLive(_viewModel.Value?.IsAlerting == true);
    }
}

public sealed class JsonUrlViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private string _title = string.Empty;
    private string _messageKey = "json-url.loading";
    private ValueView? _value;

    public JsonUrlViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public string Title => _title.Length > 0 ? _title : _text["json-url.name"];

    /// <summary>The value to show, maybe out of date, or null while there has never been one.</summary>
    public ValueView? Value => _value;

    public bool HasValue => _value is not null;

    public bool HasMessage => _value is null;

    public string Message => _text[_messageKey];

    public ValueLevel Level => _value?.Level ?? ValueLevel.Unknown;

    /// <summary>The value on the compact notch. A list shows its most urgent item.</summary>
    public string CompactText => _value switch
    {
        null => string.Empty,
        { Kind: ValueTemplateKind.List } list => list.Items.OrderByDescending(i => i.Level).FirstOrDefault() is { } item
            ? string.Join(" ", new[] { item.Label, item.Value }.Where(s => !string.IsNullOrEmpty(s)))
            : list.Text,
        { } view => view.DisplayText,
    };

    public string AccessibleSummary =>
        _text.Format("json-url.accessible", ("title", Title), ("value", _value is { } view ? ValueSpeech.Describe(view, _text) : Message));

    internal void SetTitle(string title)
    {
        _title = title;
        OnPropertyChanged(string.Empty);
    }

    internal void ShowMessage(string key)
    {
        _messageKey = key;
        _value = null;
        OnPropertyChanged(string.Empty);
    }

    internal void ShowValue(ValueView view)
    {
        _value = view;
        OnPropertyChanged(string.Empty);
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
