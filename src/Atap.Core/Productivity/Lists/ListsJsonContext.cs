using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Productivity.Lists;

/// <summary>Source-generated JSON for the to-do and notes files, in the same style as layout.json.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    Converters = [typeof(HourMinuteConverter)])]
[JsonSerializable(typeof(TodoDocument))]
[JsonSerializable(typeof(NotesDocument))]
public sealed partial class ListsJsonContext : JsonSerializerContext;
