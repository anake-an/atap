using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Atap.Ui.Controls;

/// <summary>Half of a height as a corner radius, so a bar of any height has fully round ends.</summary>
public sealed class PillRadiusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double height && double.IsFinite(height) && height > 0 ? new CornerRadius(height / 2) : new CornerRadius(0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
