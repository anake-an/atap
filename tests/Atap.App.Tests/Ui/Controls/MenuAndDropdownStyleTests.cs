using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Ui.Controls;

/// <summary>Dropdowns and menus use Atap's own Windows 11 look, never the old Windows one.</summary>
public sealed class MenuAndDropdownStyleTests
{
    private static readonly Uri SettingsStyles = new("/Atap;component/Ui/Theme/Settings.xaml", UriKind.Relative);

    [Fact]
    public void Dropdowns_HaveTheirOwnTemplate_WithARoundedSeeThroughList() => Sta.Run(() =>
    {
        var styles = (ResourceDictionary)Application.LoadComponent(SettingsStyles);
        var combo = new ComboBox { Style = (Style)styles["Settings.ComboBox"], ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0 };
        using var host = new KitHost(combo);

        var popup = Assert.IsType<Popup>(combo.Template.FindName("PART_Popup", combo));
        Assert.True(popup.AllowsTransparency);
        Assert.Equal("Settings.ComboBoxItem", KeyOf(styles, combo.ItemContainerStyle));
    });

    [Fact]
    public void Menus_HaveTheirOwnTemplate() => Sta.Run(() =>
    {
        var item = new MenuItem { Header = "Settings", IsChecked = true };
        using var host = new KitHost(item);

        Assert.IsType<Style>(Application.Current.FindResource(typeof(ContextMenu)));
        Assert.NotNull(item.Template.FindName("PART_Popup", item));
        Assert.Equal(Visibility.Visible, Assert.IsAssignableFrom<UIElement>(item.Template.FindName("Tick", item)).Visibility);
    });

    private static string? KeyOf(ResourceDictionary dictionary, Style style) =>
        dictionary.Keys.OfType<string>().FirstOrDefault(key => ReferenceEquals(dictionary[key], style));
}
