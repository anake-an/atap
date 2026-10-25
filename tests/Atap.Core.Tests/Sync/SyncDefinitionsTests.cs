using System.Text.Json;
using Atap.Core.Sync;
using Atap.Core.Widgets;

namespace Atap.Core.Tests.Sync;

public sealed class SyncDefinitionsTests
{
    [Fact]
    public void Scope_SyncsSyncedKeysOnly_AndKeepsMachineAndSecretWidgetFieldsHome()
    {
        var scope = SyncDefinitions.Scope(
            [
                new("clock.style", "x", SettingType.String, "plain"),
                new("display.monitor", "x", SettingType.String, string.Empty) { Scope = SettingScope.Machine },
                new("weather.key", "x", SettingType.Secret, null),
                new("sync.enabled", "x", SettingType.Bool, false),
            ],
            [("file-shelf", [new SettingDefinition("folder", "x", SettingType.Folder, string.Empty) { Scope = SettingScope.Machine }, new SettingDefinition("token", "x", SettingType.Secret, null)])]);

        Assert.True(scope.IsSynced("settings:clock.style"));
        Assert.False(scope.IsSynced("settings:display.monitor"));
        Assert.False(scope.IsSynced("settings:weather.key"));
        // Sync's own keys never sync, whatever the definition says.
        Assert.False(scope.IsSynced("settings:sync.enabled"));
        Assert.True(scope.IsMachineField("layout:widgetSettings:file-shelf", "folder"));
        Assert.True(scope.IsSecretField("layout:widgetSettings:file-shelf#two", "token"));
    }

    [Theory]
    [InlineData(SettingType.Int, "7.0", "7")]
    [InlineData(SettingType.Int, "500", "100")]
    [InlineData(SettingType.Bool, "true", "true")]
    [InlineData(SettingType.Time, "\"7:30\"", "\"07:30\"")]
    [InlineData(SettingType.StringList, "[\" a \",\"\"]", "[\"a\"]")]
    public void Check_ReturnsTheValueAsTheStoreHoldsIt(SettingType type, string remote, string expected)
    {
        var definition = new SettingDefinition("x.y", "x", type, null) { Max = 100 };

        var result = SyncDefinitions.Check(definition, JsonElement.Parse(remote));

        Assert.True(result.IsAccepted);
        Assert.Equal(expected, result.Value.GetRawText());
    }

    [Theory]
    [InlineData(SettingType.Int, "\"7\"")]
    [InlineData(SettingType.Bool, "1")]
    [InlineData(SettingType.Choice, "\"neon\"")]
    [InlineData(SettingType.Url, "\"ftp://example.com\"")]
    [InlineData(SettingType.Time, "\"\"")]
    public void Check_RefusesWhatDoesNotFit(SettingType type, string remote)
    {
        var definition = new SettingDefinition("x.y", "x", type, null, ["plain", "bold"]);

        Assert.False(SyncDefinitions.Check(definition, JsonElement.Parse(remote)).IsAccepted);
    }
}
