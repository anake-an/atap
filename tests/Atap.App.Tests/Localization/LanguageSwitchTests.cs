using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using Atap.Core.Localization;
using Atap.Features.Language;
using Atap.Tests.Infrastructure;
using Xunit;

namespace Atap.Tests.Localization;

public class LanguageSwitchTests
{
    private static Localizer NewText(string language) =>
        new(new AssemblyStringResources(typeof(App).Assembly), language);

    [Fact]
    public void BoundText_ChangesLanguageWithoutARestart() => Sta.Run(() =>
    {
        var text = NewText(Localizer.English);
        var block = new TextBlock();
        block.SetBinding(TextBlock.TextProperty, new Binding("[language.choice]") { Source = text, Mode = BindingMode.OneWay });

        Assert.Equal("Language", block.Text);
        text.SetLanguage(Localizer.Malay);
        Assert.Equal("Bahasa", block.Text);
        text.SetLanguage(Localizer.English);
        Assert.Equal("Language", block.Text);
    });

    [Fact]
    public void Apply_SwitchesTextAndTheUiCulture() => Sta.Run(() =>
    {
        var text = NewText(Localizer.English);
        var ui = CultureInfo.CurrentUICulture;
        var defaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            Assert.Equal(Localizer.Malay, LanguageFeature.Apply(text, LanguageFeatureDescriptor.SameAsWindows, CultureInfo.GetCultureInfo("ms-MY")));
            Assert.Equal(Localizer.Malay, text.Language);
            Assert.Equal("ms-MY", CultureInfo.CurrentUICulture.Name);

            Assert.Equal(Localizer.English, LanguageFeature.Apply(text, Localizer.English, CultureInfo.GetCultureInfo("ms-MY")));
            Assert.Equal("en-MY", CultureInfo.CurrentUICulture.Name);
        }
        finally
        {
            CultureInfo.CurrentUICulture = ui;
            CultureInfo.DefaultThreadCurrentUICulture = defaultUi;
        }
    });

    [Fact]
    public void Descriptor_DeclaresTheChoiceInGeneral()
    {
        var descriptor = new LanguageFeatureDescriptor();
        var setting = Assert.Single(descriptor.Settings);
        Assert.Equal("language.choice", setting.Key);
        Assert.Equal(new[] { "system", "en", "ms" }, setting.Choices);
        Assert.Equal("system", setting.DefaultValue);
        Assert.Equal("general", setting.SectionId);
        Assert.Empty(descriptor.EnabledBy);
    }

    [Fact]
    public void EveryChoiceHasALabelInBothLanguages() => Sta.Run(() =>
    {
        foreach (var language in new[] { Localizer.English, Localizer.Malay })
        {
            var text = NewText(language);
            foreach (var choice in new LanguageFeatureDescriptor().Settings[0].Choices!)
                Assert.NotEqual($"language.choice.{choice}", text[$"language.choice.{choice}"]);
        }
    });
}
