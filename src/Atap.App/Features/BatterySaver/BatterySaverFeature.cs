using Atap.Composition;
using Atap.Core.Power;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Features.BatterySaver;
using Atap.Notch;
using Atap.Platform.Power;

[assembly: AtapFeature(typeof(BatterySaverDescriptor))]

namespace Atap.Features.BatterySaver;

/// <summary>
/// Battery saver: when it is on, refreshes that can wait come four times less often, the notch stops animating
/// and Windows runs Atap in efficiency mode. The settings decide when it is on, on battery by default. The
/// scheduler and web data follow it in AppServices. Runs in safe mode too.
/// </summary>
internal sealed class BatterySaverDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "batterySaver";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(BatterySaverPolicy.ModeKey, "saver.mode", SettingType.Choice,
            BatterySaverPolicy.ToSettingValue(BatterySaverPolicy.DefaultMode), BatterySaverPolicy.ModeChoices)
        {
            SectionId = SettingsSections.Behavior,
            DescriptionKey = "saver.modeHint",
        },
        new(BatterySaverPolicy.PercentKey, "saver.percent", SettingType.Int, BatterySaverPolicy.DefaultPercent)
        {
            SectionId = SettingsSections.Behavior,
            Min = 5,
            Max = 50,
            Step = 5,
            UnitKey = "saver.percentUnit",
            VisibleWhen = (BatterySaverPolicy.ModeKey, BatterySaverPolicy.ToSettingValue(SaverMode.BelowPercent)),
        },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [];

    public bool IsEnabled(SettingValues settings, AppMode mode) => true;

    public IAppFeature Create(FeatureContext context) =>
        new BatterySaverFeature(context.Power, context.Notch, new ProcessEfficiency(), FeatureId);
}

/// <summary>Follows <see cref="IPowerState.SaverActive"/> with the notch's less motion flag and efficiency mode. Holds no timer.</summary>
internal sealed class BatterySaverFeature(IPowerState power, INotchControl notch, IProcessEfficiency efficiency, string ownerId) : IAppFeature
{
    private bool _efficient;

    public Task StartAsync()
    {
        power.Changed += OnPowerChanged;
        Apply();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        power.Changed -= OnPowerChanged;
        if (_efficient)
            efficiency.Set(false);
        _efficient = false;
        // The feature host clears the less motion flag once the feature has stopped.
        return ValueTask.CompletedTask;
    }

    private void OnPowerChanged(object? sender, EventArgs e) => Apply();

    private void Apply()
    {
        var on = power.SaverActive;
        notch.SetFlag(NotchFlag.LessMotion, ownerId, on);
        if (on == _efficient)
            return;
        // A refusal (older Windows) still counts, so it is not asked again on every power event.
        efficiency.Set(on);
        _efficient = on;
    }
}
