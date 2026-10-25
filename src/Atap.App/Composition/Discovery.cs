using System.Reflection;
using Atap.Core.Widgets;

namespace Atap.Composition;

/// <summary>
/// Reads the registration attributes once at startup. Only descriptors are created here, never widgets,
/// features or cards, so nothing heavy loads for what is turned off.
/// </summary>
internal static class Discovery
{
    public static WidgetCatalog Widgets(Assembly assembly) => WidgetCatalog.Discover(assembly);

    /// <summary>Feature descriptors in registration order. Broken or duplicate ones go to <paramref name="problems"/>.</summary>
    public static IReadOnlyList<IFeatureDescriptor> Features(Assembly assembly, List<string> problems)
    {
        var features = new List<IFeatureDescriptor>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in assembly.GetCustomAttributes<AtapFeatureAttribute>())
        {
            if (TryCreate<IFeatureDescriptor>(attribute.DescriptorType, problems) is not { } feature)
                continue;
            if (string.IsNullOrWhiteSpace(feature.Id) || !ids.Add(feature.Id))
            {
                problems.Add($"Feature '{feature.Id}' from {attribute.DescriptorType.FullName} has an empty or duplicate id.");
                continue;
            }
            features.Add(feature);
        }
        return features;
    }

    /// <summary>Card types only. Cards are created when the settings window opens.</summary>
    public static IReadOnlyList<Type> SettingsCards(Assembly assembly) =>
        assembly.GetCustomAttributes<AtapSettingsCardAttribute>()
            .Select(a => a.CardType)
            .Where(t => t is { IsAbstract: false } && typeof(ISettingsCard).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Distinct()
            .ToList();

    private static T? TryCreate<T>(Type? type, List<string> problems)
        where T : class
    {
        if (type is null || type.IsAbstract || !typeof(T).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
        {
            problems.Add($"{type?.FullName ?? "(null)"} is not a valid {typeof(T).Name}.");
            return null;
        }
        try
        {
            return (T)Activator.CreateInstance(type)!;
        }
        catch (Exception ex)
        {
            problems.Add($"{type.FullName} could not be created: {(ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message}");
            return null;
        }
    }
}
