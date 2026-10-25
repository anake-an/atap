using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Atap.Core.Widgets;

/// <summary>Why a registered widget type was left out of the catalog.</summary>
public enum WidgetRejection
{
    /// <summary>The type is abstract, has no public parameterless constructor or does not implement <see cref="IWidgetDescriptor"/>.</summary>
    InvalidType,

    /// <summary>The descriptor's constructor threw.</summary>
    CreateFailed,

    /// <summary>The type id does not match <c>^[a-z][a-z0-9-]{1,31}$</c>.</summary>
    InvalidTypeId,

    /// <summary>Another descriptor already uses the type id. The first one wins.</summary>
    DuplicateTypeId,
}

/// <param name="Source">The descriptor type name, or its type id when it was created.</param>
public sealed record WidgetCatalogProblem(string Source, WidgetRejection Reason, Exception? Exception = null);

/// <summary>
/// Every widget type Atap knows, read once at startup. Holds descriptors only:
/// no widget is created here, so a disabled widget costs one small object.
/// </summary>
public sealed class WidgetCatalog
{
    private readonly Dictionary<string, IWidgetDescriptor> _byTypeId;

    private WidgetCatalog(List<IWidgetDescriptor> descriptors, List<WidgetCatalogProblem> problems)
    {
        Descriptors = descriptors;
        Problems = problems;
        _byTypeId = descriptors.ToDictionary(d => d.TypeId, StringComparer.Ordinal);
    }

    /// <summary>In registration order.</summary>
    public IReadOnlyList<IWidgetDescriptor> Descriptors { get; }

    /// <summary>Descriptors that were left out. The app logs them.</summary>
    public IReadOnlyList<WidgetCatalogProblem> Problems { get; }

    /// <summary>Reads every <see cref="AtapWidgetAttribute"/> on <paramref name="assembly"/> and creates the descriptors.</summary>
    public static WidgetCatalog Discover(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var descriptors = new List<IWidgetDescriptor>();
        var problems = new List<WidgetCatalogProblem>();
        foreach (var attribute in assembly.GetCustomAttributes<AtapWidgetAttribute>())
        {
            var type = attribute.DescriptorType;
            if (type is null || type.IsAbstract || !typeof(IWidgetDescriptor).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
            {
                problems.Add(new(type?.FullName ?? "(null)", WidgetRejection.InvalidType));
                continue;
            }

            try
            {
                descriptors.Add((IWidgetDescriptor)Activator.CreateInstance(type)!);
            }
            catch (Exception ex)
            {
                problems.Add(new(type.FullName ?? type.Name, WidgetRejection.CreateFailed, ex is TargetInvocationException { InnerException: { } inner } ? inner : ex));
            }
        }

        return Build(descriptors, problems);
    }

    /// <summary>Builds a catalog from descriptors that already exist. Invalid and duplicate type ids are left out.</summary>
    public static WidgetCatalog FromDescriptors(IEnumerable<IWidgetDescriptor> descriptors) => Build(descriptors, []);

    public bool TryGet(string typeId, [NotNullWhen(true)] out IWidgetDescriptor? descriptor) =>
        _byTypeId.TryGetValue(typeId, out descriptor);

    /// <summary>
    /// Finds the descriptor for an instance id. Fails when the id is malformed, the type is unknown,
    /// or the id has a name but the type allows only one instance.
    /// </summary>
    public bool TryResolve(string? instanceId, [NotNullWhen(true)] out IWidgetDescriptor? descriptor)
    {
        descriptor = null;
        if (!WidgetInstanceId.TryParse(instanceId, out var id) || !_byTypeId.TryGetValue(id.TypeId, out var found))
            return false;
        if (id.Name is not null && !found.AllowsMultipleInstances)
            return false;
        descriptor = found;
        return true;
    }

    private static WidgetCatalog Build(IEnumerable<IWidgetDescriptor> candidates, List<WidgetCatalogProblem> problems)
    {
        var accepted = new List<IWidgetDescriptor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var descriptor in candidates)
        {
            if (!WidgetInstanceId.IsValidTypeId(descriptor.TypeId))
                problems.Add(new(descriptor.TypeId ?? descriptor.GetType().Name, WidgetRejection.InvalidTypeId));
            else if (!seen.Add(descriptor.TypeId))
                problems.Add(new(descriptor.TypeId, WidgetRejection.DuplicateTypeId));
            else
                accepted.Add(descriptor);
        }
        return new WidgetCatalog(accepted, problems);
    }
}
