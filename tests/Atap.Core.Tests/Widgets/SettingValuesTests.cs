using System.Text.Json;
using Atap.Core.Widgets;

namespace Atap.Core.Tests.Widgets;

public class SettingValuesTests
{
    private static SettingValues Values(string json, params SettingDefinition[] definitions) =>
        new(definitions, JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json));

    [Fact]
    public void GetInt_ClampsToMinAndMax()
    {
        var definition = new SettingDefinition("n", "n", SettingType.Int, 5) { Min = 1, Max = 10 };

        Assert.Equal(10, Values("""{ "n": 99 }""", definition).GetInt("n"));
        Assert.Equal(1, Values("""{ "n": -4 }""", definition).GetInt("n"));
        Assert.Equal(5, Values("""{ "n": "seven" }""", definition).GetInt("n"));
    }

    [Fact]
    public void GetDouble_ReadsNumbersAndClamps()
    {
        var definition = new SettingDefinition("d", "d", SettingType.Double, 1.5) { Max = 2 };

        Assert.Equal(1.25, Values("""{ "d": 1.25 }""", definition).GetDouble("d"));
        Assert.Equal(2, Values("""{ "d": 3 }""", definition).GetDouble("d"));
        Assert.Equal(1.5, Values("{}", definition).GetDouble("d"));
    }

    [Fact]
    public void GetTime_ReadsHoursAndMinutes()
    {
        var definition = new SettingDefinition("t", "t", SettingType.Time, "07:30");

        Assert.Equal(new TimeOnly(22, 5), Values("""{ "t": "22:05" }""", definition).GetTime("t"));
        Assert.Equal(new TimeOnly(7, 30), Values("""{ "t": "25:00" }""", definition).GetTime("t"));
        Assert.Equal(new TimeOnly(7, 30), Values("""{ "t": "7pm" }""", definition).GetTime("t"));
    }

    [Fact]
    public void GetDuration_ReadsSeconds()
    {
        var definition = new SettingDefinition("p", "p", SettingType.Duration, TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromSeconds(90), Values("""{ "p": 90 }""", definition).GetDuration("p"));
        Assert.Equal(TimeSpan.FromMinutes(5), Values("""{ "p": -1 }""", definition).GetDuration("p"));
    }

    [Fact]
    public void GetStringList_SkipsNonStringsAndCapsTheCount()
    {
        var definition = new SettingDefinition("l", "l", SettingType.StringList, null) { MaxItems = 2 };

        Assert.Equal(["a", "c"], Values("""{ "l": [ "a", 1, "c", "d" ] }""", definition).GetStringList("l"));
        Assert.Empty(Values("""{ "l": "a" }""", definition).GetStringList("l"));
    }

    [Fact]
    public void GetSecretRef_ReturnsOnlyReferences()
    {
        var definition = new SettingDefinition("s", "s", SettingType.Secret, null);

        Assert.Equal("secret:7f2a", Values("""{ "s": "secret:7f2a" }""", definition).GetSecretRef("s"));
        Assert.Null(Values("""{ "s": "https://example.com/token" }""", definition).GetSecretRef("s"));
        Assert.Null(Values("""{ "s": "secret:" }""", definition).GetSecretRef("s"));
    }

    [Fact]
    public void GetString_RejectsChoicesOutsideTheList()
    {
        var definition = new SettingDefinition("c", "c", SettingType.Choice, "a", ["a", "b"]);

        Assert.Equal("b", Values("""{ "c": "b" }""", definition).GetString("c"));
        Assert.Equal("a", Values("""{ "c": "z" }""", definition).GetString("c"));
    }

    [Fact]
    public void Constructor_IgnoresDuplicateDefinitions()
    {
        var values = Values("{}", new SettingDefinition("k", "k", SettingType.Bool, true), new SettingDefinition("k", "k", SettingType.Bool, false));

        Assert.True(values.GetBool("k"));
    }
}
