using Atap.Core.Sync;
using static Atap.Core.Tests.Sync.SyncTestData;

namespace Atap.Core.Tests.Sync;

public sealed class SyncValuesTests
{
    [Theory]
    [InlineData("""{"a":1,"b":[true,null,"x"]}""", """{ "b": [ true, null, "x" ], "a": 1 }""")]
    [InlineData("1", "1.0")]
    [InlineData("1", "1e0")]
    [InlineData("100", "1E2")]
    [InlineData("1.5", "1.50")]
    [InlineData("0.1", "1e-1")]
    [InlineData("\"A\"", "\"\\u0041\"")]
    [InlineData("""{"a":1,"a":2}""", """{"a":2}""")]
    public void Hash_IgnoresOrderSpellingAndEscaping(string first, string second)
    {
        Assert.Equal(SyncValues.Hash(Json(first)), SyncValues.Hash(Json(second)));
    }

    [Theory]
    [InlineData("1", "2")]
    [InlineData("1", "\"1\"")]
    [InlineData("[1,2]", "[2,1]")]
    [InlineData("""{"a":1}""", """{"A":1}""")]
    [InlineData("null", "false")]
    [InlineData("\"ms\"", "\"en\"")]
    public void Hash_DiffersForDifferentValues(string first, string second)
    {
        Assert.NotEqual(SyncValues.Hash(Json(first)), SyncValues.Hash(Json(second)));
    }

    [Fact]
    public void Hash_IsLowercaseSha256Hex()
    {
        var hash = SyncValues.Hash(Json("""{"use24Hour":true}"""));

        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Strip_RemovesSecretReferencesAtAnyDepth()
    {
        var value = Json("""
            { "icsUrl": "secret:7f2a", "minutes": 5,
              "headers": [{ "name": "Authorization", "value": "SECRET:9b1c" }, "secret:aa", "plain"],
              "nested": { "token": "Secret:cc", "keep": "secretary" } }
            """);

        var stripped = SyncValues.Strip(value);

        Assert.True(SameJson("""
            { "minutes": 5, "headers": [{ "name": "Authorization" }, "plain"], "nested": { "keep": "secretary" } }
            """, stripped));
    }

    [Fact]
    public void Strip_WholeValueIsASecret_GivesNull()
    {
        Assert.Null(SyncValues.Strip(Json("\"secret:7f2a\"")));
    }

    [Fact]
    public void Strip_RemovesMachineFieldsAtTheTopOnly()
    {
        var value = Json("""{ "folder": "C:\\Users\\aniq\\Desktop", "maxItems": 10, "inner": { "folder": "kept" } }""");

        var stripped = SyncValues.Strip(value, name => name == "folder");

        Assert.True(SameJson("""{ "maxItems": 10, "inner": { "folder": "kept" } }""", stripped));
    }

    [Fact]
    public void Strip_NothingToRemove_GivesTheSameValue()
    {
        var value = Json("""{"use24Hour":true}""");

        Assert.Equal(value.GetRawText(), Raw(SyncValues.Strip(value)));
    }

    [Fact]
    public void Restore_PutsLocalSecretsAndMachineFieldsBack()
    {
        var local = Json("""
            { "icsUrl": "secret:local1", "minutes": 5, "folder": "D:\\Shelf",
              "headers": [{ "name": "Authorization", "value": "secret:local2" }] }
            """);
        var remote = Json("""{ "minutes": 10, "headers": [{ "name": "Authorization" }, { "name": "X-Extra" }] }""");

        var restored = SyncValues.Restore(remote, local, name => name == "folder");

        Assert.True(SameJson("""
            { "minutes": 10, "headers": [{ "name": "Authorization", "value": "secret:local2" }, { "name": "X-Extra" }],
              "icsUrl": "secret:local1", "folder": "D:\\Shelf" }
            """, restored));
    }

    [Fact]
    public void Restore_ReorderedArray_KeepsEachSecretWithItsOwnItem()
    {
        var local = Json("""
            { "headers": [{ "name": "Authorization", "value": "secret:token" }, { "name": "X-Trace" }, { "name": "X-Key", "value": "secret:key" }] }
            """);
        var remote = Json("""{ "headers": [{ "name": "X-Trace" }, { "name": "X-Key" }, { "name": "Authorization" }] }""");

        var restored = SyncValues.Restore(remote, local);

        Assert.True(SameJson("""
            { "headers": [{ "name": "X-Trace" }, { "name": "X-Key", "value": "secret:key" }, { "name": "Authorization", "value": "secret:token" }] }
            """, restored));
    }

    [Fact]
    public void Restore_ArrayItemThatChangedOnAnotherPc_IsTakenWithoutTheSecret()
    {
        var local = Json("""{ "headers": [{ "name": "Authorization", "value": "secret:token" }] }""");
        var remote = Json("""{ "headers": [{ "name": "X-Trace" }] }""");

        var restored = SyncValues.Restore(remote, local);

        Assert.True(SameJson("""{ "headers": [{ "name": "X-Trace" }] }""", restored));
    }

    [Theory]
    [InlineData("""{ "url": "https://attacker.example/api", "minutes": 5 }""")]
    [InlineData("""{ "url": "https://nas.local/api", "minutes": 5, "baseUrl": "https://attacker.example" }""")]
    [InlineData("""{ "url": "https://nas.local/api", "minutes": 5, "port": 8443 }""")]
    [InlineData("""{ "url": "https://nas.local/api", "minutes": 5, "notes": "https://attacker.example" }""")]
    public void Restore_WhenWhereItConnectsChanged_LeavesTheSecretsOut(string remoteJson)
    {
        // Someone who can write the sync folder points the widget at their own server: the token must not follow.
        var local = Json("""{ "url": "https://nas.local/api", "minutes": 5, "token": "secret:t1", "key": "plain-key", "folder": "D:\\Shelf" }""");

        var restored = SyncValues.Restore(Json(remoteJson), local, name => name is "folder" or "key", name => name == "key");

        Assert.False(restored.TryGetProperty("token", out _));
        Assert.False(restored.TryGetProperty("key", out _));
        Assert.Equal("D:\\Shelf", restored.GetProperty("folder").GetString());
    }

    [Fact]
    public void Restore_WhenOnlyOtherFieldsChanged_KeepsTheSecrets()
    {
        var local = Json("""{ "url": "https://nas.local/api", "minutes": 5, "token": "secret:t1", "auth": { "header": "secret:t2" } }""");
        var remote = Json("""{ "url": "https://nas.local/api", "minutes": 15, "auth": { } }""");

        var restored = SyncValues.Restore(remote, local);

        Assert.Equal("secret:t1", restored.GetProperty("token").GetString());
        Assert.Equal("secret:t2", restored.GetProperty("auth").GetProperty("header").GetString());
        Assert.Equal(15, restored.GetProperty("minutes").GetInt32());
    }

    [Fact]
    public void Restore_BareSecretInAnUnchangedArray_StaysWhenAnotherFieldChanges()
    {
        var local = Json("""{ "keys": ["secret:a", "plain", "secret:b"], "minutes": 5 }""");
        var remote = Json("""{ "keys": ["plain"], "minutes": 10 }""");

        var restored = SyncValues.Restore(remote, local);

        Assert.True(SameJson("""{ "keys": ["secret:a", "plain", "secret:b"], "minutes": 10 }""", restored));
    }

    [Theory]
    [InlineData("apiUrl", true)]
    [InlineData("base_url", true)]
    [InlineData("Host", true)]
    [InlineData("serverAddress", true)]
    [InlineData("description", false)]
    [InlineData("minutes", false)]
    public void SameDestinations_NoticesFieldsNamedLikeAnAddress(string field, bool counts)
    {
        var a = Json($$"""{ "{{field}}": "one" }""");
        var b = Json($$"""{ "{{field}}": "two" }""");

        Assert.Equal(!counts, SyncValues.SameDestinations(a, b));
    }

    [Fact]
    public void Restore_SameItemsTwice_KeepTheirOrder()
    {
        var local = Json("""{ "headers": [{ "name": "A" }, { "name": "A", "value": "secret:2" }] }""");
        var remote = Json("""{ "headers": [{ "name": "A" }, { "name": "A" }] }""");

        var restored = SyncValues.Restore(remote, local);

        Assert.True(SameJson("""{ "headers": [{ "name": "A" }, { "name": "A", "value": "secret:2" }] }""", restored));
    }

    [Fact]
    public void Restore_KeepsARemotePlainValueOverALocalReference()
    {
        var restored = SyncValues.Restore(Json("""{ "icsUrl": "https://example.com/cal.ics" }"""), Json("""{ "icsUrl": "secret:local1" }"""));

        Assert.True(SameJson("""{ "icsUrl": "https://example.com/cal.ics" }""", restored));
    }

    [Fact]
    public void Restore_WithoutLocalValue_GivesTheRemoteValue()
    {
        var remote = Json("""{ "minutes": 10 }""");

        Assert.Equal(remote.GetRawText(), SyncValues.Restore(remote, null).GetRawText());
    }

    [Fact]
    public void Restore_DifferentShapes_GivesTheRemoteValue()
    {
        var restored = SyncValues.Restore(Json("[1,2]"), Json("""{ "token": "secret:x" }"""));

        Assert.True(SameJson("[1,2]", restored));
    }

    [Fact]
    public void IsSecretReference_OnlyForStringsWithThePrefix()
    {
        Assert.True(SyncValues.IsSecretReference(Json("\"secret:1\"")));
        Assert.True(SyncValues.IsSecretReference(Json("\"SECRET:1\"")));
        Assert.False(SyncValues.IsSecretReference(Json("\"secretary\"")));
        Assert.False(SyncValues.IsSecretReference(Json("""{"secret:1":1}""")));
    }
}
