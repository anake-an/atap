using Atap.Core;
using Atap.Core.Layout;
using Atap.Core.Widgets;
using Atap.Widgets.Media;

[assembly: AtapWidget(typeof(MediaDescriptor))]

namespace Atap.Widgets.Media;

/// <summary>
/// Now playing. Created at startup whether media is enabled or not, so it holds constants only:
/// the media session types are reached only through <see cref="Create"/>.
/// </summary>
public sealed class MediaDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = DefaultLayout.MediaWidgetId;

    public string TypeId => TypeIdValue;

    public string NameKey => "media.name";

    public string DescriptionKey => "media.description";

    public WidgetGroup Group => WidgetGroup.Media;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Compact, NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Media/MediaViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new MediaWidget(context);
}
