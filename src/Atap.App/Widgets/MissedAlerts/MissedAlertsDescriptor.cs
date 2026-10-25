using Atap.Core;
using Atap.Core.Widgets;
using Atap.Widgets.MissedAlerts;

[assembly: AtapWidget(typeof(MissedAlertsDescriptor))]

namespace Atap.Widgets.MissedAlerts;

/// <summary>The alerts that came while Atap was quiet or hidden, in the panel. Created at startup whether it is enabled or not, so it holds constants only.</summary>
public sealed class MissedAlertsDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "missed-alerts";

    public string TypeId => TypeIdValue;

    public string NameKey => "missed-alerts.name";

    public string DescriptionKey => "missed-alerts.description";

    public WidgetGroup Group => WidgetGroup.System;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Medium, WidgetSize.Large];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/MissedAlerts/MissedAlertsViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new MissedAlertsWidget(context);
}
