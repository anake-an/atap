using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Productivity.Student;

/// <summary>Source-generated JSON for the timetable and assignments files, in the same style as layout.json.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    Converters = [typeof(HourMinuteConverter)])]
[JsonSerializable(typeof(TimetableDocument))]
[JsonSerializable(typeof(AssignmentDocument))]
public sealed partial class StudentJsonContext : JsonSerializerContext;
