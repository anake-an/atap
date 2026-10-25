using System.Text.Json.Nodes;
using Atap.Platform.Secrets;
using Microsoft.Extensions.Time.Testing;

namespace Atap.Tests.Platform.Secrets;

/// <summary>
/// The real Windows data protection, for the user running the tests, on a secrets file in a temp folder.
/// Plain tests: DPAPI needs no UI thread.
/// </summary>
public sealed class DpapiSecretStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Atap.Tests", Guid.NewGuid().ToString("N"));

    private string SecretsPath => Path.Combine(_folder, "secrets.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Put_ThenGet_RoundTrips_AlsoInANewInstance()
    {
        var store = Store();
        Assert.True(store.IsAvailable);

        var reference = store.Put("sk-test-1234 kunci rahsia é");

        Assert.Matches("^secret:[0-9a-f]{16}$", reference);
        Assert.Equal("sk-test-1234 kunci rahsia é", store.Get(reference));
        Assert.Equal("sk-test-1234 kunci rahsia é", Store().Get(reference));
    }

    [Fact]
    public void TheFile_NeverHoldsTheValue()
    {
        var reference = Store().Put("plain-token-value");

        var json = File.ReadAllText(SecretsPath);
        Assert.DoesNotContain("plain-token-value", json, StringComparison.Ordinal);
        var root = JsonNode.Parse(json)!;
        Assert.Equal(DpapiSecretStore.CurrentSchemaVersion, root["schemaVersion"]!.GetValue<int>());
        Assert.NotNull(root["secrets"]![reference["secret:".Length..]]);
        Assert.False(File.Exists(SecretsPath + ".tmp"));
    }

    [Fact]
    public void EachPut_GetsItsOwnReference()
    {
        var store = Store();

        var first = store.Put("same");
        var second = store.Put("same");

        Assert.NotEqual(first, second);
        Assert.Equal("same", store.Get(first));
        Assert.Equal("same", store.Get(second));
    }

    [Fact]
    public void Delete_RemovesOnlyThatSecret()
    {
        var store = Store();
        var keep = store.Put("keep");
        var drop = store.Put("drop");

        store.Delete(drop);
        store.Delete(drop);
        store.Delete("secret:0000000000000000");

        Assert.Null(store.Get(drop));
        Assert.Equal("keep", store.Get(keep));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain-token-value")]
    [InlineData("secret:")]
    [InlineData("Secret:7f2a")]
    [InlineData("secret:../../x")]
    [InlineData("secret:7f2a")]
    public void References_ThatDoNotResolve_GiveNull(string reference)
    {
        var store = Store();
        store.Put("something");

        Assert.Null(store.Get(reference));
    }

    [Fact]
    public void Get_WithNoFile_GivesNullAndCreatesNothing()
    {
        var store = Store();

        Assert.Null(store.Get("secret:7f2a7f2a7f2a7f2a"));
        store.Delete("secret:7f2a7f2a7f2a7f2a");

        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void ABlobMovedToAnotherId_DoesNotOpen()
    {
        var store = Store();
        var reference = store.Put("bound to its id");
        var id = reference["secret:".Length..];
        var root = JsonNode.Parse(File.ReadAllText(SecretsPath))!.AsObject();
        root["secrets"]!["0123456789abcdef"] = root["secrets"]![id]!.GetValue<string>();
        File.WriteAllText(SecretsPath, root.ToJsonString());

        Assert.Null(store.Get("secret:0123456789abcdef"));
        Assert.Equal("bound to its id", store.Get(reference));
    }

    [Fact]
    public void ADamagedBlob_GivesNull()
    {
        var store = Store();
        var reference = store.Put("value");
        var id = reference["secret:".Length..];
        var root = JsonNode.Parse(File.ReadAllText(SecretsPath))!.AsObject();
        root["secrets"]![id] = "AAAA";
        File.WriteAllText(SecretsPath, root.ToJsonString());

        Assert.Null(store.Get(reference));
    }

    [Fact]
    public void ACorruptFile_IsKeptAside_AndANewOneStarted()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SecretsPath, "{ not json");
        var store = Store();

        Assert.Null(store.Get("secret:7f2a7f2a7f2a7f2a"));
        var reference = store.Put("fresh");

        Assert.Equal("fresh", store.Get(reference));
        Assert.Equal("{ not json", File.ReadAllText(Path.Combine(_folder, "secrets.corrupt-20261025-120000.json")));
    }

    [Fact]
    public void AFileFromANewerAtap_IsReadButNeverWritten()
    {
        var reference = Store().Put("from before");
        var root = JsonNode.Parse(File.ReadAllText(SecretsPath))!.AsObject();
        root["schemaVersion"] = 2;
        File.WriteAllText(SecretsPath, root.ToJsonString());
        var newer = File.ReadAllText(SecretsPath);
        var store = Store();

        Assert.Equal("from before", store.Get(reference));
        Assert.Throws<InvalidOperationException>(() => store.Put("new"));
        store.Delete(reference);

        Assert.Equal(newer, File.ReadAllText(SecretsPath));
    }

    [Fact]
    public void ParallelPuts_AllSurvive()
    {
        var store = Store();

        var references = Enumerable.Range(0, 20).AsParallel().Select(i => (i, store.Put($"value {i}"))).ToList();

        foreach (var (i, reference) in references)
            Assert.Equal($"value {i}", store.Get(reference));
    }

    private DpapiSecretStore Store() =>
        new(SecretsPath, new FakeTimeProvider(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero)));
}
