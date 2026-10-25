namespace Atap.Core.Widgets;

/// <summary>
/// Registers a widget type. Put it in the widget's descriptor file:
/// <c>[assembly: AtapWidget(typeof(ClockDescriptor))]</c>. The app reads these attributes once at startup.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class AtapWidgetAttribute(Type descriptorType) : Attribute
{
    /// <summary>A public class with a parameterless constructor that implements <see cref="IWidgetDescriptor"/>.</summary>
    public Type DescriptorType { get; } = descriptorType;
}
