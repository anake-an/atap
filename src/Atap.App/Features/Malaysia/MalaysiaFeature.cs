using Atap.Composition;
using Atap.Core.Malaysia;
using Atap.Core.Widgets;
using Atap.Features.Malaysia;

[assembly: AtapFeature(typeof(MalaysiaDescriptor))]

namespace Atap.Features.Malaysia;

/// <summary>
/// The state, zone and Hijri offset that every Malaysia widget reads. State and zone are picked on the prayer
/// times card, so they have no section of their own. Nothing runs: the widgets read the values themselves.
/// </summary>
internal sealed class MalaysiaDescriptor : IFeatureDescriptor
{
    public string Id => MalaysiaKeys.FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(MalaysiaKeys.State, "malaysia.state", SettingType.String, string.Empty),
        new(MalaysiaKeys.Zone, "malaysia.zone", SettingType.String, string.Empty),
        new(MalaysiaKeys.HijriOffset, "malaysia.hijriOffset", SettingType.Int, 0)
        {
            SectionId = SettingsSections.Malaysia,
            DescriptionKey = "malaysia.hijriOffsetHint",
            Min = -HijriDate.MaxOffsetDays,
            Max = HijriDate.MaxOffsetDays,
            Step = 1,
            UnitKey = "malaysia.days",
        },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [];

    public bool IsEnabled(SettingValues settings, AppMode mode) => false;

    public IAppFeature Create(FeatureContext context) => throw new NotSupportedException("The Malaysia feature only holds settings.");
}
