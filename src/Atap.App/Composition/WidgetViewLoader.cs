using System.Windows;
using Atap.Core.Widgets;

namespace Atap.Composition;

/// <summary>Merges a widget type's views while at least one of its instances exists.</summary>
internal interface IWidgetViews
{
    /// <summary>Merges the type's dictionary on the first call. Dispose to release; the last release removes it.</summary>
    IDisposable Acquire(IWidgetDescriptor descriptor);
}

/// <summary>
/// Merges each widget type's <see cref="IWidgetDescriptor.ViewsSource"/> into the app resources when
/// its first instance is created, and removes it after the last one is gone. So the views of a
/// disabled widget are never parsed.
/// </summary>
internal sealed class WidgetViewLoader(ResourceDictionary target) : IWidgetViews
{
    private readonly Dictionary<string, Entry> _loaded = new(StringComparer.Ordinal);

    /// <summary>Type ids whose views are merged right now.</summary>
    public IReadOnlyCollection<string> LoadedTypeIds => _loaded.Keys;

    public IDisposable Acquire(IWidgetDescriptor descriptor)
    {
        if (!_loaded.TryGetValue(descriptor.TypeId, out var entry))
        {
            var dictionary = (ResourceDictionary)Application.LoadComponent(descriptor.ViewsSource);
            target.MergedDictionaries.Add(dictionary);
            entry = new Entry(dictionary);
            _loaded[descriptor.TypeId] = entry;
        }
        entry.Count++;
        return new Release(this, descriptor.TypeId, entry);
    }

    private void Unload(string typeId, Entry entry)
    {
        entry.Count--;
        if (entry.Count > 0 || !_loaded.TryGetValue(typeId, out var current) || current != entry)
            return;
        _loaded.Remove(typeId);
        target.MergedDictionaries.Remove(entry.Dictionary);
    }

    private sealed class Entry(ResourceDictionary dictionary)
    {
        public ResourceDictionary Dictionary { get; } = dictionary;

        public int Count { get; set; }
    }

    private sealed class Release(WidgetViewLoader owner, string typeId, Entry entry) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            owner.Unload(typeId, entry);
        }
    }
}
