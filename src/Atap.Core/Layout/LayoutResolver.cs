using System.Text.Json;

namespace Atap.Core.Layout;

/// <summary>The fields a profile can have its own copy of. Placement and widget settings are always global.</summary>
public enum LayoutField
{
    EnabledWidgets,
    States,
    CompactPriority,
}

/// <summary>
/// Profiles over the Default profile, which is the top level of layout.json. A profile field left out (null)
/// is inherited from the Default. The editor changes a profile through the *ToEdit methods: the first edit of
/// an inherited field copies it into the profile (copy on write), so the Default and other profiles stay as
/// they are. Unknown profile ids mean the Default, as in <see cref="ILayoutService.SetActiveProfile"/>.
/// </summary>
public static class LayoutResolver
{
    /// <summary>What <paramref name="profileId"/> shows, with inherited fields filled in from the Default.</summary>
    /// <param name="worksWithoutPlacement">
    /// True for instances that keep running without a placement, such as alarms. The Default's ones are added to a
    /// profile with its own widget list, so an alarm still rings while that profile is active.
    /// </param>
    public static EffectiveLayout Resolve(LayoutDocument document, string? profileId, Func<string, bool>? worksWithoutPlacement = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var profile = document.FindProfile(profileId);
        return profile is null
            ? EffectiveLayout.Snapshot(EffectiveLayout.DefaultProfileId, document.EnabledWidgets, document.CompactPriority, document.States)
            : EffectiveLayout.Snapshot(
                profile.Id,
                EnabledWidgetsOf(profile, document, worksWithoutPlacement),
                profile.CompactPriority ?? document.CompactPriority,
                profile.States ?? document.States);
    }

    /// <summary>The profile's own list followed by the Default's instances that work without a placement, or the Default's list when it has none.</summary>
    private static IEnumerable<string> EnabledWidgetsOf(LayoutProfile profile, LayoutDocument document, Func<string, bool>? worksWithoutPlacement)
    {
        if (profile.EnabledWidgets is not { } own)
            return document.EnabledWidgets;
        if (worksWithoutPlacement is null)
            return own;
        return own.Concat(document.EnabledWidgets.Where(id => !own.Contains(id, StringComparer.Ordinal) && worksWithoutPlacement(id)));
    }

    /// <summary>True when the profile has its own copy of <paramref name="field"/>. Always false for the Default.</summary>
    public static bool HasOwn(LayoutDocument document, string? profileId, LayoutField field)
    {
        ArgumentNullException.ThrowIfNull(document);
        var profile = document.FindProfile(profileId);
        return profile is not null && field switch
        {
            LayoutField.EnabledWidgets => profile.EnabledWidgets is not null,
            LayoutField.States => profile.States is not null,
            LayoutField.CompactPriority => profile.CompactPriority is not null,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };
    }

    /// <summary>
    /// The profile an edit goes to, or null for the Default. A profile that no longer exists throws, so a change
    /// queued for a profile deleted meanwhile is dropped instead of landing on the Default.
    /// </summary>
    private static LayoutProfile? ProfileToEdit(LayoutDocument document, string? profileId)
    {
        if (string.IsNullOrEmpty(profileId) || profileId == EffectiveLayout.DefaultProfileId)
            return null;
        return document.FindProfile(profileId) ?? throw new InvalidOperationException($"The profile '{profileId}' no longer exists.");
    }

    /// <summary>The enabled widgets to change for <paramref name="profileId"/>. A profile that inherits them gets its own copy first.</summary>
    public static List<string> EnabledWidgetsToEdit(LayoutDocument document, string? profileId)
    {
        ArgumentNullException.ThrowIfNull(document);
        var profile = ProfileToEdit(document, profileId);
        return profile is null ? document.EnabledWidgets : profile.EnabledWidgets ??= [.. document.EnabledWidgets];
    }

    /// <summary>The compact priority to change for <paramref name="profileId"/>. A profile that inherits it gets its own copy first.</summary>
    public static List<string> CompactPriorityToEdit(LayoutDocument document, string? profileId)
    {
        ArgumentNullException.ThrowIfNull(document);
        var profile = ProfileToEdit(document, profileId);
        return profile is null ? document.CompactPriority : profile.CompactPriority ??= [.. document.CompactPriority];
    }

    /// <summary>
    /// The states to change for <paramref name="profileId"/>. A profile that inherits them gets its own deep copy
    /// first, so moving a placement there never moves it in the Default.
    /// </summary>
    public static StateLayouts StatesToEdit(LayoutDocument document, string? profileId)
    {
        ArgumentNullException.ThrowIfNull(document);
        var profile = ProfileToEdit(document, profileId);
        return profile is null ? document.States : profile.States ??= Copy(document.States);
    }

    /// <summary>Drops the profile's own copy of <paramref name="field"/>, so it follows the Default again. Nothing happens for the Default.</summary>
    public static void Inherit(LayoutDocument document, string? profileId, LayoutField field)
    {
        ArgumentNullException.ThrowIfNull(document);
        var profile = document.FindProfile(profileId);
        if (profile is null)
            return;
        switch (field)
        {
            case LayoutField.EnabledWidgets:
                profile.EnabledWidgets = null;
                break;
            case LayoutField.States:
                profile.States = null;
                break;
            case LayoutField.CompactPriority:
                profile.CompactPriority = null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }
    }

    /// <summary>A deep copy through JSON, so fields this version does not know are copied too.</summary>
    private static StateLayouts Copy(StateLayouts states)
    {
        var copy = JsonSerializer.Deserialize<StateLayouts>(JsonSerializer.SerializeToUtf8Bytes(states, LayoutStore.JsonOptions), LayoutStore.JsonOptions) ?? new();
        copy.Normalize();
        return copy;
    }
}
