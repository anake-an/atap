namespace Atap.Tests.Composition;

public sealed class WidgetSettingsWatchTests
{
    private readonly Dictionary<string, string> _settings = new(StringComparer.Ordinal) { ["clock"] = "{}" };
    private readonly WidgetSettingsWatch _watch = new();

    [Fact]
    public void Changed_ListsOnlyInstancesWhoseSettingsChanged()
    {
        _settings["weather"] = "{}";
        _watch.Remember(["clock", "weather"], SettingsOf);

        _settings["weather"] = "{\"zone\":\"SGR01\"}";

        Assert.Equal(["weather"], _watch.Changed(["clock", "weather"], SettingsOf));
        Assert.Empty(_watch.Changed(["clock", "weather"], SettingsOf));
    }

    [Fact]
    public void NewInstance_RememberedAfterTheApply_RestartsOnItsFirstSettingsChange()
    {
        _watch.Remember(["clock"], SettingsOf);

        // The layout adds a widget. It is not an instance until the apply creates it.
        _settings["weather"] = "{}";
        Assert.Empty(_watch.Changed(["clock"], SettingsOf));
        _watch.Remember(["clock", "weather"], SettingsOf);

        _settings["weather"] = "{\"zone\":\"SGR01\"}";

        Assert.Equal(["weather"], _watch.Changed(["clock", "weather"], SettingsOf));
    }

    private string SettingsOf(string id) => _settings[id];
}
