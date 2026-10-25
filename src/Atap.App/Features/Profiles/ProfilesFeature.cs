using Atap.Composition;
using Atap.Core.Layout;
using Atap.Core.Net;
using Atap.Core.Notch;
using Atap.Core.Power;
using Atap.Core.Rules;
using Atap.Core.Scheduling;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Features.Profiles;
using Atap.Notch;

[assembly: AtapFeature(typeof(ProfilesDescriptor))]

namespace Atap.Features.Profiles;

/// <summary>Applies the rules in settings.json: which profile is active, and hide, slim and quiet.</summary>
internal sealed class ProfilesDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "profiles";

    /// <summary>The profile the user picked. Empty means Automatic: the rules decide, or Default.</summary>
    public const string ManualKey = "profiles.manual";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(ManualKey, "profiles.manual", SettingType.String, "") { Scope = SettingScope.Machine },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [];

    public bool IsEnabled(SettingValues settings, AppMode mode) => mode == AppMode.Normal;

    public IAppFeature Create(FeatureContext context) =>
        new ProfilesFeature(context.Settings, context.Layout, context.Notch, context.Scheduler, context.Time, context.Foreground, context.Network, context.Power);
}

/// <summary>
/// Resolves the rules again when an input they use changes, and only subscribes to those inputs: exe names only
/// with an app rule, the Wi-Fi name (which needs location) only with a Wi-Fi rule, power only with a power rule.
/// A time window arms one scheduler At for its next boundary. Hide, slim and quiet apply at once. The profile
/// waits for 1.5 stable seconds and for the layout editor to close.
/// </summary>
internal sealed class ProfilesFeature(
    IAppSettings settings,
    ILayoutService layout,
    INotchControl notch,
    IScheduler scheduler,
    TimeProvider time,
    IForegroundState foreground,
    INetworkState network,
    IPowerState power) : IAppFeature
{
    private ProfileSwitchDebouncer? _debouncer;
    private IDisposable? _boundary;
    private IDisposable? _processNames;
    private IDisposable? _ssid;
    private bool _powerWatched;
    private bool _evaluating;
    private bool _again;

    public RuleOutcome? Outcome { get; private set; }

    public Task StartAsync()
    {
        _debouncer = new ProfileSwitchDebouncer(scheduler, time, layout.ActiveProfileId, layout.SetActiveProfile);
        _debouncer.SetEditing(notch.IsSet(NotchFlag.Editing));
        settings.Changed += OnSettingsChanged;
        layout.Changed += OnInputChanged;
        notch.Changed += OnNotchChanged;
        Evaluate(startup: true);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        settings.Changed -= OnSettingsChanged;
        layout.Changed -= OnInputChanged;
        notch.Changed -= OnNotchChanged;
        Watch(new HashSet<ConditionKind>());
        _boundary?.Dispose();
        _boundary = null;
        _debouncer?.Dispose();
        _debouncer = null;
        notch.SetFlag(NotchFlag.Hide, ProfilesDescriptor.FeatureId, false);
        notch.SetFlag(NotchFlag.Slim, ProfilesDescriptor.FeatureId, false);
        notch.SetFlag(NotchFlag.QuietAlerts, ProfilesDescriptor.FeatureId, false);
        return ValueTask.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(SettingsChangedEventArgs.RulesKey) || e.Keys.Contains(ProfilesDescriptor.ManualKey))
            Evaluate(startup: false);
    }

    private void OnInputChanged(object? sender, EventArgs e) => Evaluate(startup: false);

    private void OnNotchChanged(object? sender, EventArgs e) => _debouncer?.SetEditing(notch.IsSet(NotchFlag.Editing));

    private void Evaluate(bool startup)
    {
        if (_debouncer is not { } debouncer)
            return;
        // Taking a lease can raise Changed at once. That asks for one more pass instead of nesting.
        if (_evaluating)
        {
            _again = true;
            return;
        }
        _evaluating = true;
        try
        {
            do
            {
                _again = false;
                var outcome = Resolve();
                Outcome = outcome;
                Watch(outcome.InputsUsed);
                if (_again)
                    continue;

                notch.SetFlag(NotchFlag.Hide, ProfilesDescriptor.FeatureId, outcome.Hide);
                notch.SetFlag(NotchFlag.Slim, ProfilesDescriptor.FeatureId, outcome.Slim);
                notch.SetFlag(NotchFlag.QuietAlerts, ProfilesDescriptor.FeatureId, outcome.QuietAlerts);

                _boundary?.Dispose();
                _boundary = outcome.NextTimeBoundary is { } boundary ? scheduler.At(boundary, () => Evaluate(startup: false)) : null;

                // At startup the first profile applies at once, unless the editor is open.
                if (startup && outcome.ProfileId != layout.ActiveProfileId && !notch.IsSet(NotchFlag.Editing))
                    layout.SetActiveProfile(outcome.ProfileId);
                if (startup)
                    _debouncer = debouncer = Restart(debouncer);
                debouncer.Propose(outcome.ProfileId);
            }
            while (_again);
        }
        finally
        {
            _evaluating = false;
        }
    }

    private ProfileSwitchDebouncer Restart(ProfileSwitchDebouncer old)
    {
        if (old.Current == layout.ActiveProfileId)
            return old;
        old.Dispose();
        var fresh = new ProfileSwitchDebouncer(scheduler, time, layout.ActiveProfileId, layout.SetActiveProfile);
        fresh.SetEditing(notch.IsSet(NotchFlag.Editing));
        return fresh;
    }

    private RuleOutcome Resolve()
    {
        var rules = RuleJson.Parse(settings.Rules.ValueKind == System.Text.Json.JsonValueKind.Array ? settings.Rules.GetRawText() : null) ?? [];
        var profileIds = layout.Document.Profiles.Select(p => p.Id).Where(id => id.Length > 0).ToHashSet(StringComparer.Ordinal);
        var manual = settings.Values.GetString(ProfilesDescriptor.ManualKey);
        var context = new RuleContext
        {
            Now = time.GetUtcNow(),
            TimeZone = TimeZoneInfo.Local,
            ForegroundProcessName = _processNames is null ? null : foreground.ProcessName,
            WifiSsid = _ssid is null ? null : network.WifiSsid,
            OnBattery = power.OnBattery,
            BatteryPercent = power.BatteryPercent,
            EnergySaverOn = power.EnergySaverOn,
        };
        return RuleResolver.Resolve(rules, context, string.IsNullOrWhiteSpace(manual) ? null : manual, profileIds);
    }

    /// <summary>Holds the leases and events for exactly the inputs in use.</summary>
    private void Watch(IReadOnlySet<ConditionKind> inputs)
    {
        if (inputs.Contains(ConditionKind.ForegroundApp))
        {
            if (_processNames is null)
            {
                foreground.Changed += OnInputChanged;
                _processNames = foreground.WantProcessNames();
                _again = true;
            }
        }
        else if (_processNames is not null)
        {
            foreground.Changed -= OnInputChanged;
            _processNames.Dispose();
            _processNames = null;
        }

        if (inputs.Contains(ConditionKind.Wifi))
        {
            if (_ssid is null)
            {
                network.Changed += OnInputChanged;
                _ssid = network.WantSsid();
                _again = true;
            }
        }
        else if (_ssid is not null)
        {
            network.Changed -= OnInputChanged;
            _ssid.Dispose();
            _ssid = null;
        }

        if (inputs.Contains(ConditionKind.Power) != _powerWatched)
        {
            _powerWatched = !_powerWatched;
            if (_powerWatched)
                power.Changed += OnInputChanged;
            else
                power.Changed -= OnInputChanged;
        }
    }
}
