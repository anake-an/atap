using System.Text.Json.Serialization.Metadata;

namespace Atap.Core.Storage;

/// <summary>Where a file lives.</summary>
public enum DataScope
{
    /// <summary>User data (to-dos, notes): %APPDATA%\Atap\data\&lt;instanceId&gt;\.</summary>
    User,

    /// <summary>Machine state (timer end times, approvals): %LOCALAPPDATA%\AtapData\state\&lt;instanceId&gt;\. Never synced.</summary>
    Machine,
}

/// <summary>JSON files that belong to one widget instance. Deleting the instance deletes them.</summary>
public interface IWidgetStorage
{
    /// <summary>Returns <paramref name="fallback"/> when the file is missing or does not parse.</summary>
    T Load<T>(string name, JsonTypeInfo<T> type, T fallback, DataScope scope = DataScope.User);

    /// <summary>Writes atomically: a temp file, then a rename.</summary>
    void Save<T>(string name, T value, JsonTypeInfo<T> type, DataScope scope = DataScope.User);

    /// <summary>Deletes one file. Nothing happens when it is not there.</summary>
    void Delete(string name, DataScope scope = DataScope.User);

    void DeleteAll();
}
