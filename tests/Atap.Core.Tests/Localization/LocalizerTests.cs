using System.Globalization;
using System.Text;
using Atap.Core.Localization;

namespace Atap.Core.Tests.Localization;

/// <summary>The minimal localizer's basics. The full tests come with the localization branch.</summary>
public class LocalizerTests
{
    private static readonly MemoryResources Resources = new(new()
    {
        ["Atap.Strings.solat.en.json"] = """{ "solat.nextIn": "in {minutes} min", "solat.only": "English only" }""",
        ["Atap.Strings.solat.ms.json"] = """{ "solat.nextIn": "dalam {minutes} min" }""",
    });

    [Fact]
    public void Lookup_UsesTheLanguageThenEnglishThenTheKey()
    {
        var text = new Localizer(Resources, Localizer.Malay);

        Assert.Equal("dalam 5 min", text.Format("solat.nextIn", ("minutes", 5)));
        Assert.Equal("English only", text["solat.only"]);
        Assert.Equal("solat.missing", text["solat.missing"]);
        Assert.Equal("nodot", text["nodot"]);
    }

    [Fact]
    public void MissingKey_IsReportedOnce()
    {
        var text = new Localizer(Resources, Localizer.English);
        var missing = new List<string>();
        text.MissingKey += (_, key) => missing.Add(key);

        _ = text["solat.missing"];
        _ = text["solat.missing"];

        Assert.Equal(["solat.missing"], missing);
    }

    [Fact]
    public void SetLanguage_RaisesOneIndexerChange()
    {
        var text = new Localizer(Resources, Localizer.English);
        var changed = new List<string?>();
        var languageChanged = 0;
        text.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        text.LanguageChanged += (_, _) => languageChanged++;

        text.SetLanguage(Localizer.Malay);
        text.SetLanguage(Localizer.Malay);

        Assert.Equal(1, changed.Count(n => n == "Item[]"));
        Assert.Equal(1, languageChanged);
        Assert.Equal("ms-MY", text.Culture.Name);
    }

    [Theory]
    [InlineData("system", "ms-MY", "ms")]
    [InlineData("system", "ms", "ms")]
    [InlineData("system", "ms-BN", "ms")]
    [InlineData("system", "en-GB", "en")]
    [InlineData("system", "zh-CN", "en")]
    [InlineData(null, "ms-MY", "ms")]
    [InlineData("en", "ms-MY", "en")]
    [InlineData("ms", "en-US", "ms")]
    public void ResolveChoice_PicksMalayForAnyMsCulture(string? choice, string windows, string expected)
    {
        Assert.Equal(expected, Localizer.ResolveChoice(choice, CultureInfo.GetCultureInfo(windows)));
    }

    [Theory]
    [InlineData("Atap.Strings.clock.en.json", true, "clock", "en")]
    [InlineData("Atap.Strings.waktu-solat.ms.json", true, "waktu-solat", "ms")]
    [InlineData("Atap.Strings.clock.json", false, "", "")]
    [InlineData("Atap.Data.holidays-2026.json", false, "", "")]
    public void TryParseResourceName_SplitsAreaAndLanguage(string name, bool ok, string area, string language)
    {
        Assert.Equal(ok, Localizer.TryParseResourceName(name, out var parsedArea, out var parsedLanguage));
        Assert.Equal(area, parsedArea);
        Assert.Equal(language, parsedLanguage);
    }

    private sealed class MemoryResources(Dictionary<string, string> files) : IStringResources
    {
        public IEnumerable<string> Names => files.Keys;

        public Stream? Open(string name) =>
            files.TryGetValue(name, out var json) ? new MemoryStream(Encoding.UTF8.GetBytes(json)) : null;
    }
}
