using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Atap.Ui.Controls;

namespace Atap.Ui.Theme;

/// <summary>
/// Swaps the theme brushes for the Windows high contrast colours while high contrast is on, and puts the usual
/// brushes back when it goes off. HighContrast.xaml says which colour each brush takes. Views use the brushes as
/// DynamicResource, so the notch and Settings follow at once without a restart.
/// </summary>
internal sealed class HighContrastTheme : IDisposable
{
    public static readonly Uri MapSource = new("/Atap;component/Ui/Theme/HighContrast.xaml", UriKind.Relative);

    private readonly ResourceDictionary _resources;
    private readonly Contrast _contrast;
    private readonly Dictionary<string, (ResourceDictionary Owner, object Brush)> _usual = new(StringComparer.Ordinal);

    public HighContrastTheme(ResourceDictionary resources, Contrast contrast)
    {
        _resources = resources;
        _contrast = contrast;
        _contrast.PropertyChanged += OnContrastChanged;
        Apply();
    }

    /// <summary>True while the high contrast brushes are in.</summary>
    public bool IsApplied => _usual.Count > 0;

    public void Dispose() => _contrast.PropertyChanged -= OnContrastChanged;

    /// <summary>Puts in the brushes for the current setting. Also picks up new colours after a switch between high contrast themes.</summary>
    public void Apply()
    {
        if (!_contrast.IsHigh)
        {
            foreach (var (key, usual) in _usual)
                usual.Owner[key] = usual.Brush;
            _usual.Clear();
            return;
        }

        // Read fresh each time: the colours are the Windows ones at the moment of reading.
        var map = (ResourceDictionary)Application.LoadComponent(MapSource);
        foreach (var key in map.Keys.OfType<string>())
        {
            if (map[key] is not Color colour || OwnerOf(_resources, key) is not { } owner)
                continue;
            if (!_usual.ContainsKey(key))
                _usual[key] = (owner, owner[key]);
            var brush = new SolidColorBrush(colour);
            brush.Freeze();
            owner[key] = brush;
        }
    }

    /// <summary>The dictionary that holds a key, searching merged dictionaries the way lookups do, the last one first.</summary>
    private static ResourceDictionary? OwnerOf(ResourceDictionary dictionary, string key)
    {
        if (dictionary.Contains(key))
            return dictionary;
        for (var i = dictionary.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            if (OwnerOf(dictionary.MergedDictionaries[i], key) is { } owner)
                return owner;
        }
        return null;
    }

    private void OnContrastChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(Contrast.IsHigh))
            Apply();
    }
}
