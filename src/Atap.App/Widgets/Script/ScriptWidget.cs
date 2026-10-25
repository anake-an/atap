using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Composition;
using Atap.Core;
using Atap.Core.Custom;
using Atap.Core.Localization;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Widgets.Script;

[assembly: AtapWidget(typeof(ScriptDescriptor))]

namespace Atap.Widgets.Script;

/// <summary>Runs a program the user approved on this PC and shows what it prints. Constants only.</summary>
public sealed class ScriptDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "script";

    /// <summary>The editor for the program. It writes to machine state, never to this setting.</summary>
    public const string ProgramKey = "program";
    public const string IntervalKey = "interval";
    public const string ShowKey = "show";
    public const string PathKey = "path";
    public const string UnitKey = "unit";

    public const string ShowText = "text";
    public const string ShowNumber = "number";
    public const string ShowProgress = "progress";
    public const string ShowList = "list";
    public const string ShowStatus = "status";

    public const int DefaultIntervalSeconds = 60;

    public string TypeId => TypeIdValue;

    public string NameKey => "script.name";

    public string DescriptionKey => "script.description";

    public WidgetGroup Group => WidgetGroup.Custom;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Compact, NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(ProgramKey, "script.program", SettingType.Custom, null)
        {
            DescriptionKey = "script.programHint",
            EditorKey = "script.editor",
            Scope = SettingScope.Machine,
        },
        new(IntervalKey, "script.interval", SettingType.Duration, DefaultIntervalSeconds)
        {
            Min = ScriptSpec.MinInterval.TotalSeconds,
            Max = TimeSpan.FromDays(1).TotalSeconds,
        },
        new(ShowKey, "script.show", SettingType.Choice, ShowText, [ShowText, ShowNumber, ShowProgress, ShowList, ShowStatus]),
        new(UnitKey, "script.unit", SettingType.String, string.Empty)
        {
            DescriptionKey = "script.unitHint",
            MaxLength = ValueTemplateSpec.MaxUnitLength,
            Advanced = true,
        },
        new(PathKey, "script.path", SettingType.String, "$")
        {
            DescriptionKey = "script.pathHint",
            MaxLength = 256,
            Advanced = true,
        },
    ];

    public WidgetCapabilities Capabilities { get; } = new(WidgetPermissions.RunsPrograms, [], "script.privacy");

    public bool AllowsMultipleInstances => true;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Script/ScriptViews.xaml", UriKind.Relative);

    // A mode that was never set counts as safe mode, so nothing runs before startup has decided.
    public IWidget Create(WidgetContext context) => new ScriptWidget(context, new ScriptProcessRunner(), () => SafeMode.Current != AppMode.Normal);
}

/// <summary>
/// Every interval, at least <see cref="ScriptSpec.MinInterval"/>, reads the spec from machine state, checks its approval
/// under this PC's key, and only then runs it and shows what it printed. Nothing runs in safe mode, without a spec, or
/// when the spec on disk is not the one approved. Two runs are never less than 30 seconds apart, even across restarts,
/// except the run right after Approve. Goes live while the value is at Warning or Critical.
/// </summary>
public sealed class ScriptWidget : WidgetBase
{
    private readonly IScriptRunner _runner;
    private readonly Func<bool> _isSafeMode;
    private readonly ScriptStore _store;
    private readonly ScriptViewModel _viewModel;
    private ValueTemplate? _template;
    private ValueView? _last;
    private CancellationTokenSource? _stopping;
    private IDisposable? _timer;
    private IDisposable? _catchUp;
    private bool _running;

    internal ScriptWidget(WidgetContext context, IScriptRunner runner, Func<bool> isSafeMode)
        : base(context)
    {
        _runner = runner;
        _isSafeMode = isSafeMode;
        _store = new ScriptStore(context.InstanceId, context.Storage, context.Secrets);
        var name = WidgetInstanceId.TryParse(context.InstanceId, out var id) ? id.Name : null;
        _viewModel = new ScriptViewModel(context.Text, name);
    }

    public override object ViewModel => _viewModel;

    /// <summary>The interval from the settings, never under <see cref="ScriptSpec.MinInterval"/>.</summary>
    public static TimeSpan IntervalFrom(SettingValues settings)
    {
        var interval = settings.GetDuration(ScriptDescriptor.IntervalKey);
        return interval < ScriptSpec.MinInterval ? ScriptSpec.MinInterval : interval;
    }

    /// <summary>The template from the settings, or null when the value path is not valid.</summary>
    internal static ValueTemplate? TemplateFrom(SettingValues settings)
    {
        var kind = settings.GetString(ScriptDescriptor.ShowKey) switch
        {
            ScriptDescriptor.ShowNumber => ValueTemplateKind.Number,
            ScriptDescriptor.ShowProgress => ValueTemplateKind.Progress,
            ScriptDescriptor.ShowList => ValueTemplateKind.List,
            ScriptDescriptor.ShowStatus => ValueTemplateKind.StatusDot,
            _ => ValueTemplateKind.Text,
        };
        var path = settings.GetString(ScriptDescriptor.PathKey).Trim();
        var unit = settings.GetString(ScriptDescriptor.UnitKey).Trim();
        var spec = new ValueTemplateSpec(kind, path.Length > 0 ? path : "$") { Unit = unit.Length > 0 ? unit : null };
        return ValueTemplate.TryCreate(spec, out var template, out _) ? template : null;
    }

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _stopping = new CancellationTokenSource();
        if (_isSafeMode())
        {
            _viewModel.ShowMessage("script.safeMode");
            return Task.CompletedTask;
        }
        _template = TemplateFrom(settings);
        if (_template is null)
        {
            _viewModel.ShowMessage("script.badTemplate");
            Context.Lifetime.ReportStatus(WidgetStatus.NeedsSetup, "script.badTemplate");
            return Task.CompletedTask;
        }

        ScriptStore.Changed += OnStoreChanged;
        // The timer runs even before approval, so an approval made while Settings is closed is picked up too.
        _timer = Context.Scheduler.Every(IntervalFrom(settings), Tick, new ScheduleOptions(TimeSpan.FromSeconds(5), Deferrable: true));
        Tick();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        ScriptStore.Changed -= OnStoreChanged;
        _timer?.Dispose();
        _timer = null;
        _catchUp?.Dispose();
        _catchUp = null;
        // Stops a run in progress, with everything it started.
        _stopping?.Cancel();
        _stopping?.Dispose();
        _stopping = null;
        _template = null;
        _last = null;
        SetIsLive(false);
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void OnStoreChanged(object? sender, string instanceId)
    {
        if (string.Equals(instanceId, Context.InstanceId, StringComparison.Ordinal))
            _ = RunOnceAsync(afterApproval: true);
    }

    private void Tick() => _ = RunOnceAsync(afterApproval: false);

    /// <param name="afterApproval">The user just pressed Approve, so the run comes at once even within 30 seconds of the last.</param>
    private async Task RunOnceAsync(bool afterApproval)
    {
        if (_stopping is not { } stopping || _template is not { } template || _running)
            return;
        if (_isSafeMode())
        {
            ShowWithoutRun("script.safeMode");
            return;
        }

        // Read from disk each time, so a spec changed outside the editor never runs on an old approval.
        var state = _store.Load();
        if (ScriptStore.SpecOf(state) is not { } spec)
        {
            ShowWithoutRun("script.notSetUp");
            Context.Lifetime.ReportStatus(WidgetStatus.NeedsSetup, "script.notSetUp");
            return;
        }
        if (!_store.IsApproved(spec, state))
        {
            ShowWithoutRun("script.needsApproval");
            Context.Lifetime.ReportStatus(WidgetStatus.NeedsApproval, "script.needsApproval");
            return;
        }
        Context.Lifetime.ReportStatus(WidgetStatus.Running);

        // Starts, restarts and settings changes would each run at once. The last start is kept on disk, so runs stay
        // at least 30 seconds apart across all of them. Only Approve runs at once.
        var now = Context.Time.GetUtcNow();
        if (!afterApproval && _store.LoadLastRun() is { } last && now >= last && now - last < ScriptSpec.MinInterval)
        {
            _catchUp?.Dispose();
            _catchUp = Context.Scheduler.At(last + ScriptSpec.MinInterval, Tick);
            return;
        }
        _store.SaveLastRun(now);

        _running = true;
        ScriptRunResult result;
        try
        {
            result = await _runner.RunAsync(spec, stopping.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Context.Lifetime.ReportError(ex);
            return;
        }
        finally
        {
            _running = false;
        }
        if (stopping.IsCancellationRequested)
            return;
        Show(result, template);
    }

    private void ShowWithoutRun(string messageKey)
    {
        _last = null;
        _viewModel.ShowMessage(messageKey);
        SetIsLive(false);
    }

    private void Show(ScriptRunResult result, ValueTemplate template)
    {
        switch (result.Status)
        {
            case ScriptRunStatus.Done when result.Output.IsEmpty && result.ExitCode is not (null or 0):
                ShowFailure("script.failed");
                break;
            case ScriptRunStatus.Done:
                var payload = PayloadReader.Read(result.Output.Span, PayloadFormat.Auto, ScriptSpec.MaxOutputBytes);
                _last = template.Render(payload, _last);
                _viewModel.ShowValue(_last);
                break;
            case ScriptRunStatus.TooMuchOutput:
                ShowFailure("script.tooMuch");
                break;
            case ScriptRunStatus.TimedOut:
                ShowFailure("script.timedOut");
                break;
            case ScriptRunStatus.NetworkDrive:
                ShowFailure("script.networkDrive");
                break;
            default:
                ShowFailure("script.couldNotStart");
                break;
        }
        SetIsLive(_viewModel.IsAlerting);
    }

    /// <summary>Says what went wrong, under the last good value marked out of date when there is one.</summary>
    private void ShowFailure(string messageKey) =>
        _viewModel.ShowFailure(messageKey, _last is { HasValue: true } last ? last with { Stale = true } : null);
}

public sealed class ScriptViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private readonly string? _instanceName;
    private string? _messageKey = "script.waiting";
    private ValueView? _value;

    public ScriptViewModel(ILocalizer text, string? instanceName)
    {
        _text = text;
        _instanceName = instanceName;
        _text.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The instance's name, such as "backup" for "script#backup", or "Script".</summary>
    public string Title => _instanceName ?? _text["script.name"];

    /// <summary>What the program printed, as the chosen template shows it. Null before the first run.</summary>
    public ValueView? Value => _value;

    public bool HasValue => _value is not null;

    public string Message => _messageKey is null ? string.Empty : _text[_messageKey];

    public bool HasMessage => _messageKey is not null;

    /// <summary>"72%" on the notch while the value needs attention.</summary>
    public string CompactText => _value is { HasValue: true } view ? view.DisplayText : Title;

    public bool IsAlerting => _value is { IsAlerting: true, Stale: false };

    public string AccessibleSummary => _text.Format(
        "script.accessible",
        ("name", Title),
        ("value", _value is { HasValue: true } view ? view.DisplayText : Message));

    /// <summary>The message key shown right now, for tests.</summary>
    internal string? MessageKey => _messageKey;

    internal void ShowMessage(string key)
    {
        _messageKey = key;
        _value = null;
        OnPropertyChanged(string.Empty);
    }

    internal void ShowValue(ValueView view)
    {
        _messageKey = null;
        _value = view;
        OnPropertyChanged(string.Empty);
    }

    internal void ShowFailure(string key, ValueView? stale)
    {
        _messageKey = key;
        _value = stale;
        OnPropertyChanged(string.Empty);
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
