using System.Text;

namespace Atap.Core.Custom;

/// <summary>What is wrong with an MQTT topic or topic filter.</summary>
public enum MqttTopicProblem
{
    None,

    Empty,

    /// <summary>Over <see cref="MqttTopic.MaxBytes"/> UTF-8 bytes.</summary>
    TooLong,

    /// <summary>A NUL, another control character, or a lone surrogate.</summary>
    InvalidCharacter,

    /// <summary>A topic name, as opposed to a filter, holds <c>+</c> or <c>#</c>.</summary>
    WildcardInName,

    /// <summary><c>#</c> is not a whole level at the end, as in <c>a/b#</c> or <c>a/#/b</c>.</summary>
    BadMultiLevelWildcard,

    /// <summary><c>+</c> is not a whole level, as in <c>a/b+</c>.</summary>
    BadSingleLevelWildcard,
}

/// <summary>MQTT topic rules from the MQTT 3.1.1 and 5 specifications. Pure string checks.</summary>
public static class MqttTopic
{
    public const int MaxBytes = 65_535;

    /// <summary>Checks a subscription filter, where <c>+</c> and <c>#</c> are allowed as whole levels.</summary>
    public static MqttTopicProblem ValidateFilter(string? filter) => Validate(filter, allowWildcards: true);

    /// <summary>Checks a topic name as it arrives in a message.</summary>
    public static MqttTopicProblem ValidateName(string? topic) => Validate(topic, allowWildcards: false);

    /// <summary>
    /// True when <paramref name="topic"/> matches <paramref name="filter"/>. Levels compare case-sensitively.
    /// <c>+</c> matches one level and <c>#</c> matches the rest, including the parent level, so <c>a/#</c> matches <c>a</c>.
    /// Topics that start with <c>$</c> never match a filter that starts with a wildcard. Invalid input never matches.
    /// </summary>
    public static bool Matches(string? filter, string? topic)
    {
        if (ValidateFilter(filter) != MqttTopicProblem.None || ValidateName(topic) != MqttTopicProblem.None)
            return false;
        if (topic![0] == '$' && filter![0] is '+' or '#')
            return false;

        var filterLevels = filter!.Split('/');
        var topicLevels = topic.Split('/');
        for (var i = 0; i < filterLevels.Length; i++)
        {
            if (filterLevels[i] == "#")
                return true;
            if (i >= topicLevels.Length)
                return false;
            if (filterLevels[i] != "+" && !string.Equals(filterLevels[i], topicLevels[i], StringComparison.Ordinal))
                return false;
        }

        return filterLevels.Length == topicLevels.Length;
    }

    private static MqttTopicProblem Validate(string? value, bool allowWildcards)
    {
        if (string.IsNullOrEmpty(value))
            return MqttTopicProblem.Empty;
        // Each UTF-16 character takes at most 3 UTF-8 bytes, so most topics skip the exact count.
        if ((long)value.Length * 3 > MaxBytes && Encoding.UTF8.GetByteCount(value) > MaxBytes)
            return MqttTopicProblem.TooLong;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }
            if (char.IsSurrogate(c) || char.IsControl(c))
                return MqttTopicProblem.InvalidCharacter;

            if (c == '#')
            {
                if (!allowWildcards)
                    return MqttTopicProblem.WildcardInName;
                if (i != value.Length - 1 || (i > 0 && value[i - 1] != '/'))
                    return MqttTopicProblem.BadMultiLevelWildcard;
            }
            else if (c == '+')
            {
                if (!allowWildcards)
                    return MqttTopicProblem.WildcardInName;
                if ((i > 0 && value[i - 1] != '/') || (i < value.Length - 1 && value[i + 1] != '/'))
                    return MqttTopicProblem.BadSingleLevelWildcard;
            }
        }

        return MqttTopicProblem.None;
    }
}

/// <summary>
/// Maps the messages on one MQTT topic to a payload for <see cref="ValueTemplate"/>.
/// The template's value path then picks the field, so <c>$</c> takes a plain payload such as <c>72.5</c>
/// and <c>$.temperature</c> takes a field from a JSON payload.
/// </summary>
/// <param name="Topic">The topic filter, such as <c>homeassistant/sensor/nas_cpu/state</c>. Wildcards are allowed.</param>
/// <param name="Format">How the payload is read.</param>
public sealed record MqttMapping(string Topic, PayloadFormat Format = PayloadFormat.Auto)
{
    /// <summary>Largest payload read, in bytes. Larger payloads give <see cref="PayloadStatus.TooLarge"/>.</summary>
    public const int MaxPayloadBytes = 64 * 1024;

    /// <summary>
    /// Whole payloads that mean there is no value, matched without regard to case or surrounding spaces, plain or as a
    /// JSON string. Home Assistant publishes these when a sensor cannot be read. They give <see cref="PayloadStatus.Empty"/>
    /// in every format, so <see cref="PayloadFormat.Json"/> does not report them as bad JSON.
    /// For a field inside a JSON payload, set <see cref="ValueTemplateSpec.NoValueWords"/> on the template.
    /// </summary>
    public static IReadOnlyList<string> NoValueStates => StatusWords.DefaultNoValue;

    public MqttTopicProblem Validate() => MqttTopic.ValidateFilter(Topic);

    public bool Matches(string topic) => MqttTopic.Matches(Topic, topic);

    /// <summary>Reads <paramref name="payload"/> when <paramref name="topic"/> matches. Returns false for another topic.</summary>
    public bool TryMap(string topic, ReadOnlySpan<byte> payload, out PayloadReadResult result)
    {
        if (!Matches(topic))
        {
            result = default;
            return false;
        }

        result = Read(payload);
        return true;
    }

    /// <summary>Reads a payload with this mapping's format and the MQTT size cap. Never throws.</summary>
    public PayloadReadResult Read(ReadOnlySpan<byte> payload) =>
        WithoutNoValueStates(PayloadReader.Read(payload, Format, MaxPayloadBytes, NoValueStates));

    /// <inheritdoc cref="Read(ReadOnlySpan{byte})"/>
    public PayloadReadResult Read(string? payload) =>
        WithoutNoValueStates(PayloadReader.Read(payload, Format, MaxPayloadBytes, NoValueStates));

    /// <summary>Plain words are caught before parsing. This catches the same words sent as a JSON string, such as <c>"unavailable"</c>.</summary>
    private static PayloadReadResult WithoutNoValueStates(PayloadReadResult result)
    {
        if (result.Status != PayloadStatus.Ok || result.Root.ValueKind != System.Text.Json.JsonValueKind.String)
            return result;

        var text = JsonText.GetString(result.Root).Trim();
        foreach (var state in NoValueStates)
        {
            if (string.Equals(text, state, StringComparison.OrdinalIgnoreCase))
                return new PayloadReadResult(PayloadStatus.Empty, PayloadReader.Null);
        }
        return result;
    }
}
