using System.ComponentModel;
using System.Windows;

namespace Atap.Ui.Controls;

/// <summary>
/// Whether Windows high contrast is on, for the UI kit's triggers:
/// <c>{Binding IsHigh, Source={x:Static controls:Contrast.Current}}</c>. It follows the Windows setting live,
/// so a template switches to system colours without a restart.
/// </summary>
public sealed class Contrast : INotifyPropertyChanged
{
    private bool? _override;

    private Contrast() => SystemParameters.StaticPropertyChanged += OnSystemChanged;

    public static Contrast Current { get; } = new();

    public bool IsHigh => _override ?? SystemParameters.HighContrast;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Tests force high contrast on or off with this. Null follows Windows again.</summary>
    internal bool? Override
    {
        get => _override;
        set
        {
            if (_override == value)
                return;
            _override = value;
            Raise();
        }
    }

    private void OnSystemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Raise();
    }

    private void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHigh)));
}
