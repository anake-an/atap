using System.Text.Json.Serialization;
using Atap.Core.Storage;
using Microsoft.Extensions.Time.Testing;

namespace Atap.Core.Tests.Storage;

public sealed class WidgetStorageTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 7, 5, 9, TimeSpan.Zero));

    private AtapPaths Paths => new(Path.Combine(_folder.Path, "roaming"), Path.Combine(_folder.Path, "local"));

    public void Dispose() => _folder.Dispose();

    [Theory]
    [InlineData(DataScope.User, "roaming", "data")]
    [InlineData(DataScope.Machine, "local", "state")]
    public void SaveThenLoad_RoundTripsInTheScopeFolder(DataScope scope, string root, string area)
    {
        var storage = new WidgetStorage("json-url#nas", Paths, _time);

        storage.Save("items", new StoredItem("milk", 2), StorageTestJson.Default.StoredItem, scope);

        Assert.True(File.Exists(Path.Combine(_folder.Path, root, area, "json-url#nas", "items.json")));
        Assert.Equal(new StoredItem("milk", 2), storage.Load("items", StorageTestJson.Default.StoredItem, new StoredItem("none", 0), scope));
        // The other scope is a different file.
        var other = scope == DataScope.User ? DataScope.Machine : DataScope.User;
        Assert.Equal(new StoredItem("none", 0), storage.Load("items", StorageTestJson.Default.StoredItem, new StoredItem("none", 0), other));
    }

    [Fact]
    public void Load_Missing_GivesTheFallbackAndCreatesNoFolder()
    {
        var storage = new WidgetStorage("todo", Paths, _time);

        var StoredItem = storage.Load("items", StorageTestJson.Default.StoredItem, new StoredItem("fallback", 1));

        Assert.Equal(new StoredItem("fallback", 1), StoredItem);
        Assert.False(Directory.Exists(_folder.Path));
    }

    [Fact]
    public void Delete_RemovesOneFile_AndIgnoresMissingOnes()
    {
        var storage = new WidgetStorage("todo", Paths, _time);
        storage.Delete("items", DataScope.Machine);
        storage.Save("items", new StoredItem("milk", 2), StorageTestJson.Default.StoredItem, DataScope.Machine);
        storage.Save("other", new StoredItem("eggs", 1), StorageTestJson.Default.StoredItem, DataScope.Machine);

        storage.Delete("items", DataScope.Machine);

        Assert.False(File.Exists(Path.Combine(Paths.MachineState, "todo", "items.json")));
        Assert.True(File.Exists(Path.Combine(Paths.MachineState, "todo", "other.json")));
    }

    [Fact]
    public void Load_Corrupt_GivesTheFallbackAndKeepsTheFileAside()
    {
        var file = Path.Combine(Paths.UserData, "todo", "items.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{ \"name\": ");
        var storage = new WidgetStorage("todo", Paths, _time);

        var StoredItem = storage.Load("items", StorageTestJson.Default.StoredItem, new StoredItem("fallback", 1));

        Assert.Equal(new StoredItem("fallback", 1), StoredItem);
        Assert.False(File.Exists(file));
        Assert.Equal("{ \"name\": ", File.ReadAllText(Path.Combine(Paths.UserData, "todo", "items.corrupt-20261025-070509.json")));
    }

    [Fact]
    public void Load_Locked_GivesTheFallbackAndLeavesTheFile()
    {
        var storage = new WidgetStorage("todo", Paths, _time);
        storage.Save("items", new StoredItem("milk", 2), StorageTestJson.Default.StoredItem);
        var file = Path.Combine(Paths.UserData, "todo", "items.json");

        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(new StoredItem("fallback", 1), storage.Load("items", StorageTestJson.Default.StoredItem, new StoredItem("fallback", 1)));

        Assert.Equal(new StoredItem("milk", 2), storage.Load("items", StorageTestJson.Default.StoredItem, new StoredItem("fallback", 1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("..")]
    [InlineData("a..b")]
    [InlineData("sub/items")]
    [InlineData("sub\\items")]
    [InlineData("C:items")]
    public void BadFileNames_AreRefused(string name)
    {
        var storage = new WidgetStorage("todo", Paths, _time);

        Assert.Throws<ArgumentException>(() => storage.Save(name, new StoredItem("x", 1), StorageTestJson.Default.StoredItem));
        Assert.Throws<ArgumentException>(() => storage.Load(name, StorageTestJson.Default.StoredItem, new StoredItem("x", 1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("Todo")]
    [InlineData("todo#..")]
    [InlineData("../todo")]
    public void BadInstanceIds_AreRefused(string instanceId) =>
        Assert.Throws<ArgumentException>(() => new WidgetStorage(instanceId, Paths, _time));

    [Fact]
    public void DeleteAll_RemovesBothFoldersAndLeavesOtherInstances()
    {
        var storage = new WidgetStorage("todo", Paths, _time);
        var other = new WidgetStorage("todo#work", Paths, _time);
        storage.Save("items", new StoredItem("a", 1), StorageTestJson.Default.StoredItem);
        storage.Save("state", new StoredItem("b", 2), StorageTestJson.Default.StoredItem, DataScope.Machine);
        other.Save("items", new StoredItem("c", 3), StorageTestJson.Default.StoredItem);

        storage.DeleteAll();
        storage.DeleteAll();

        Assert.False(Directory.Exists(Path.Combine(Paths.UserData, "todo")));
        Assert.False(Directory.Exists(Path.Combine(Paths.MachineState, "todo")));
        Assert.Equal(new StoredItem("c", 3), other.Load("items", StorageTestJson.Default.StoredItem, new StoredItem("none", 0)));
    }

    [Fact]
    public void Save_ReplacesAtomically()
    {
        var storage = new WidgetStorage("todo", Paths, _time);

        storage.Save("items", new StoredItem("a", 1), StorageTestJson.Default.StoredItem);
        storage.Save("items", new StoredItem("b", 2), StorageTestJson.Default.StoredItem);

        var folder = Path.Combine(Paths.UserData, "todo");
        Assert.Equal(["items.json"], Directory.GetFiles(folder).Select(Path.GetFileName));
        Assert.Equal(new StoredItem("b", 2), storage.Load("items", StorageTestJson.Default.StoredItem, new StoredItem("none", 0)));
    }
}

public sealed record StoredItem(string Name, int Count);

[JsonSerializable(typeof(StoredItem))]
internal sealed partial class StorageTestJson : JsonSerializerContext;
