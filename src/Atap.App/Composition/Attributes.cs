namespace Atap.Composition;

/// <summary>
/// Registers an app feature. Put it in the feature's descriptor file:
/// <c>[assembly: AtapFeature(typeof(HotkeysFeature))]</c>. Read once at startup.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class AtapFeatureAttribute(Type descriptorType) : Attribute
{
    /// <summary>A class with a parameterless constructor that implements <see cref="IFeatureDescriptor"/>.</summary>
    public Type DescriptorType { get; } = descriptorType;
}

/// <summary>
/// Registers a settings card. Put it in the card's folder:
/// <c>[assembly: AtapSettingsCard(typeof(VersionCard))]</c>. Cards are created only while the settings window is open.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class AtapSettingsCardAttribute(Type cardType) : Attribute
{
    /// <summary>A class with a parameterless constructor that implements <see cref="ISettingsCard"/>.</summary>
    public Type CardType { get; } = cardType;
}
