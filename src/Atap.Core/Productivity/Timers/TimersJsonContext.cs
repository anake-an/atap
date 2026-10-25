using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Productivity.Timers;

/// <summary>Source-generated JSON for the timer widgets' saved files, in the same style as layout.json.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    Converters = [typeof(HourMinuteConverter)])]
[JsonSerializable(typeof(FocusTimerState))]
[JsonSerializable(typeof(StopwatchState))]
[JsonSerializable(typeof(AlarmDocument))]
[JsonSerializable(typeof(WaterState))]
public sealed partial class TimersJsonContext : JsonSerializerContext;
