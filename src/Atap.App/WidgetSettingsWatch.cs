namespace Atap;

/// <summary>
/// Remembers the settings of each widget instance, so a layout change restarts only the instances whose settings
/// changed. Call <see cref="Remember"/> after every apply as well, so instances the apply created are compared on
/// the next change. Before that they do not exist yet, and their first settings change would be missed.
/// </summary>
internal sealed class WidgetSettingsWatch
{
    private Dictionary<string, string> _known = new(StringComparer.Ordinal);

    /// <summary>Replaces what is remembered with the settings of <paramref name="instanceIds"/> now.</summary>
    public void Remember(IEnumerable<string> instanceIds, Func<string, string> settingsOf) =>
        _known = Snapshot(instanceIds, settingsOf);

    /// <summary>The instances whose settings differ from what was remembered. Then remembers the settings now.</summary>
    public List<string> Changed(IEnumerable<string> instanceIds, Func<string, string> settingsOf)
    {
        var now = Snapshot(instanceIds, settingsOf);
        var changed = new List<string>();
        foreach (var (id, json) in now)
        {
            if (_known.TryGetValue(id, out var before) && before != json)
                changed.Add(id);
        }
        _known = now;
        return changed;
    }

    private static Dictionary<string, string> Snapshot(IEnumerable<string> instanceIds, Func<string, string> settingsOf)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in instanceIds)
            snapshot[id] = settingsOf(id);
        return snapshot;
    }
}
