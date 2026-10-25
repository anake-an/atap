using Atap.Core;
using Atap.Core.Layout;
using Atap.Core.Widgets;
using Atap.Widgets.Clock;

[assembly: AtapWidget(typeof(ClockDescriptor))]

namespace Atap.Widgets.Clock;

/// <summary>The clock. Created at startup whether the clock is enabled or not, so it holds constants only.</summary>
public sealed class ClockDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = DefaultLayout.ClockWidgetId;
    public const string Use24HourKey = "use24Hour";

    public string TypeId => TypeIdValue;

    public string NameKey => "clock.name";

    public string DescriptionKey => "clock.description";

    public WidgetGroup Group => WidgetGroup.System;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Idle, NotchState.Expanded];

    public IReadOnlyList<SettingDefinition> Settings { get; } =
        [new SettingDefinition(Use24HourKey, "clock.use24Hour", SettingType.Bool, true)];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Clock/ClockViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new ClockWidget(context);
}
