using System.Windows.Data;
using System.Windows.Markup;
using Atap.Core.Localization;

namespace Atap.Ui.Localization;

/// <summary>
/// Localized text in XAML: <c>Text="{loc:T media.previous}"</c> with
/// <c>xmlns:loc="clr-namespace:Atap.Ui.Localization"</c>. It binds to the localizer's indexer,
/// so the text follows a language switch without a restart.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class T(string key) : MarkupExtension
{
    public T()
        : this(string.Empty)
    {
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Localizer.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
