using Atap.Core.Widgets;

namespace Atap.Core;

/// <summary>Decides which live widget owns the compact notch.</summary>
public static class CompactArbiter
{
    /// <summary>
    /// Returns the first widget in <paramref name="priority"/> (instance ids) that is live and whose
    /// type supports the compact state, or null when none is (the notch then shows Idle).
    /// Widgets missing from the priority list never take compact.
    /// </summary>
    public static IWidget? SelectOwner(IEnumerable<IWidget> widgets, IEnumerable<string> priority)
    {
        var candidates = widgets
            .Where(w => w.IsLive && w.Descriptor.SupportedStates.Contains(NotchState.Compact))
            .ToList();
        if (candidates.Count == 0)
            return null;

        foreach (var id in priority)
        {
            var match = candidates.Find(w => string.Equals(w.Id, id, StringComparison.Ordinal));
            if (match is not null)
                return match;
        }

        return null;
    }
}
