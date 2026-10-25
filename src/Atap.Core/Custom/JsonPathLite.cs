using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Atap.Core.Custom;

/// <summary>How a path evaluation ended.</summary>
public enum JsonPathOutcome
{
    /// <summary>At least one value matched.</summary>
    Found,

    /// <summary>The path exists up to a <c>[*]</c> step, but nothing matched from there, as with <c>$.items[*]</c> on an empty list.</summary>
    NoMatches,

    /// <summary>A step before any <c>[*]</c> found nothing, such as a missing name or an index past the end.</summary>
    Missing,
}

/// <summary>The values a path found in a JSON document.</summary>
/// <remarks>
/// The matches point into the caller's document, so read them before that document is disposed.
/// A <c>length()</c> result is a standalone number.
/// </remarks>
public sealed class JsonPathResult
{
    internal JsonPathResult(JsonPathOutcome outcome, IReadOnlyList<JsonElement> matches, int missingAt, JsonValueKind missingOn, bool truncated)
    {
        Outcome = outcome;
        Matches = matches;
        MissingAt = missingAt;
        MissingOn = missingOn;
        Truncated = truncated;
    }

    public JsonPathOutcome Outcome { get; }

    /// <summary>The values found, in document order. Empty unless <see cref="Outcome"/> is Found.</summary>
    public IReadOnlyList<JsonElement> Matches { get; }

    /// <summary>Position in the path of the step that found nothing, or -1.</summary>
    public int MissingAt { get; }

    /// <summary>The kind of value that step was applied to, such as Array for a name step. Undefined when unknown.</summary>
    public JsonValueKind MissingOn { get; }

    /// <summary>True when more than <see cref="JsonPathLite.MaxMatches"/> values matched and only the first ones were kept.</summary>
    public bool Truncated { get; }

    public bool Found => Outcome == JsonPathOutcome.Found;

    /// <summary>The first match, when there is one.</summary>
    public bool TryGetFirst(out JsonElement value)
    {
        value = Found ? Matches[0] : default;
        return Found;
    }
}

/// <summary>
/// A small subset of JSONPath for mapping fields from JSON URL, script and MQTT payloads.
/// </summary>
/// <remarks>
/// <para>Supported steps, after the root <c>$</c>:</para>
/// <list type="bullet">
/// <item><c>.name</c> for a member. Names may use letters, digits, <c>_</c>, <c>-</c>, <c>$</c> and any non-ASCII letter.</item>
/// <item><c>['name']</c> or <c>["name"]</c> for any member name. Inside quotes, <c>\'</c>, <c>\"</c> and <c>\\</c> are the only escapes.</item>
/// <item><c>[n]</c> for an array index. A negative index counts from the end, so <c>[-1]</c> is the last item.</item>
/// <item><c>[*]</c> or <c>.*</c> for every item of an array or every value of an object.</item>
/// <item><c>.length()</c> as the last step. It gives the item count of one array or object, the character count of one string,
/// or the number of matches when the path has a <c>[*]</c>.</item>
/// </list>
/// <para>Anything else, such as <c>..</c>, filters, slices and unions, is rejected with a <see cref="JsonPathError"/>.</para>
/// </remarks>
public sealed class JsonPathLite
{
    /// <summary>Longest accepted path, in characters, not counting spaces around it.</summary>
    public const int MaxLength = 512;

    /// <summary>Most steps in one path, <c>length()</c> included.</summary>
    public const int MaxSegments = 32;

    /// <summary>Most values one evaluation keeps. Later matches are dropped and <see cref="JsonPathResult.Truncated"/> is set.</summary>
    public const int MaxMatches = 10_000;

    private const string LengthFunction = "length";

    private readonly Segment[] _segments;
    private readonly int _lengthPosition;

    private JsonPathLite(string expression, Segment[] segments, int lengthPosition)
    {
        Expression = expression;
        _segments = segments;
        _lengthPosition = lengthPosition;
        IsDefinite = Array.TrueForAll(segments, s => s.Kind != SegmentKind.Wildcard);
    }

    /// <summary>The root path <c>$</c>, which returns the whole document.</summary>
    public static JsonPathLite Root { get; } = new("$", [], -1);

    /// <summary>The path text, without spaces around it.</summary>
    public string Expression { get; }

    /// <summary>True when the path has no <c>[*]</c>, so it finds at most one value.</summary>
    public bool IsDefinite { get; }

    /// <summary>True when the path ends with <c>length()</c>.</summary>
    public bool EndsWithLength => _lengthPosition >= 0;

    public override string ToString() => Expression;

    /// <summary>Parses a path. Never throws.</summary>
    public static bool TryParse(string? path, [NotNullWhen(true)] out JsonPathLite? result, [NotNullWhen(false)] out JsonPathError? error)
    {
        error = Parser.Parse(path, out result);
        return error is null;
    }

    /// <summary>Parses a path, or throws <see cref="JsonPathException"/>.</summary>
    public static JsonPathLite Parse(string path) =>
        TryParse(path, out var result, out var error) ? result : throw new JsonPathException(error);

    /// <summary>Finds the values this path selects. Never throws.</summary>
    public JsonPathResult Evaluate(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Undefined)
            return Missing(0, JsonValueKind.Undefined);

        List<JsonElement> current = [root];
        var truncated = false;
        var afterWildcard = false;

        foreach (var segment in _segments)
        {
            var next = new List<JsonElement>(current.Count);
            foreach (var node in current)
            {
                if (!Apply(segment, node, next))
                {
                    truncated = true;
                    break;
                }
            }

            if (segment.Kind == SegmentKind.Wildcard)
                afterWildcard = true;

            if (next.Count == 0)
            {
                if (afterWildcard)
                    return EndsWithLength ? Count(0, truncated) : NoMatches(truncated);
                return Missing(segment.Position, current.Count == 1 ? current[0].ValueKind : JsonValueKind.Undefined);
            }

            current = next;
        }

        if (!EndsWithLength)
            return new JsonPathResult(JsonPathOutcome.Found, current, -1, JsonValueKind.Undefined, truncated);

        if (!IsDefinite)
            return Count(current.Count, truncated);

        var single = current[0];
        return single.ValueKind switch
        {
            JsonValueKind.Array => Count(single.GetArrayLength(), truncated),
            JsonValueKind.Object => Count(single.EnumerateObject().Count(), truncated),
            JsonValueKind.String => Count(new StringInfo(JsonText.GetString(single)).LengthInTextElements, truncated),
            _ => Missing(_lengthPosition, single.ValueKind),
        };
    }

    /// <summary>Adds what one step finds under one node. Returns false when the match cap is reached.</summary>
    private static bool Apply(Segment segment, JsonElement node, List<JsonElement> into)
    {
        switch (segment.Kind)
        {
            case SegmentKind.Member:
                if (node.ValueKind == JsonValueKind.Object && TryGetMember(node, segment.Name!, out var member))
                    return Add(into, member);
                return true;

            case SegmentKind.Index:
                if (node.ValueKind != JsonValueKind.Array)
                    return true;
                var length = node.GetArrayLength();
                var index = segment.Index < 0 ? length + (long)segment.Index : segment.Index;
                return index < 0 || index >= length || Add(into, node[(int)index]);

            default:
                if (node.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in node.EnumerateArray())
                    {
                        if (!Add(into, item))
                            return false;
                    }
                }
                else if (node.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in node.EnumerateObject())
                    {
                        if (!Add(into, property.Value))
                            return false;
                    }
                }
                return true;
        }
    }

    /// <summary>
    /// Finds a member, the last one when a name repeats. System.Text.Json throws when another member's name
    /// has a broken surrogate escape, so that case falls back to comparing every name.
    /// </summary>
    private static bool TryGetMember(JsonElement node, string name, out JsonElement member)
    {
        try
        {
            return node.TryGetProperty(name, out member);
        }
        catch (InvalidOperationException)
        {
            var found = false;
            member = default;
            foreach (var property in node.EnumerateObject())
            {
                if (string.Equals(JsonText.GetName(property), name, StringComparison.Ordinal))
                {
                    member = property.Value;
                    found = true;
                }
            }
            return found;
        }
    }

    private static bool Add(List<JsonElement> into, JsonElement value)
    {
        if (into.Count >= MaxMatches)
            return false;
        into.Add(value);
        return true;
    }

    private static JsonPathResult Missing(int position, JsonValueKind on) =>
        new(JsonPathOutcome.Missing, [], position, on, truncated: false);

    private static JsonPathResult NoMatches(bool truncated) =>
        new(JsonPathOutcome.NoMatches, [], -1, JsonValueKind.Undefined, truncated);

    private static JsonPathResult Count(int count, bool truncated)
    {
        using var document = JsonDocument.Parse(count.ToString(CultureInfo.InvariantCulture));
        return new JsonPathResult(JsonPathOutcome.Found, [document.RootElement.Clone()], -1, JsonValueKind.Undefined, truncated);
    }

    private enum SegmentKind
    {
        Member,
        Index,
        Wildcard,
    }

    /// <param name="Position">Index of the step's first character in the text as given.</param>
    private readonly record struct Segment(SegmentKind Kind, string? Name, int Index, int Position);

    /// <summary>Hand-written scanner. Each method returns an error, or null and moves <see cref="_i"/> past what it read.</summary>
    private ref struct Parser
    {
        private readonly string _s;
        private readonly int _end;
        private int _i;

        private Parser(string s, int start, int end)
        {
            _s = s;
            _i = start;
            _end = end;
        }

        public static JsonPathError? Parse(string? path, out JsonPathLite? result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(path))
                return new JsonPathError(JsonPathErrorKind.Empty, 0);

            var start = 0;
            while (char.IsWhiteSpace(path[start]))
                start++;
            var end = path.Length;
            while (char.IsWhiteSpace(path[end - 1]))
                end--;

            if (end - start > MaxLength)
                return new JsonPathError(JsonPathErrorKind.TooLong, start + MaxLength);
            if (path[start] != '$')
                return new JsonPathError(JsonPathErrorKind.MissingRoot, start, path[start].ToString());

            // A lone surrogate cannot become a UTF-8 member name, so it is refused up front.
            for (var i = start; i < end; i++)
            {
                if (char.IsHighSurrogate(path[i]) && i + 1 < end && char.IsLowSurrogate(path[i + 1]))
                    i++;
                else if (char.IsSurrogate(path[i]))
                    return new JsonPathError(JsonPathErrorKind.UnexpectedCharacter, i, "\uFFFD");
            }

            var parser = new Parser(path, start + 1, end);
            var segments = new List<Segment>();
            var lengthPosition = -1;

            while (parser._i < parser._end)
            {
                if (segments.Count >= MaxSegments)
                    return new JsonPathError(JsonPathErrorKind.TooManySegments, parser._i);

                var position = parser._i;
                JsonPathError? error;
                Segment segment;
                bool isLength;
                if (parser.Peek() == '.')
                    error = parser.ReadDotStep(out segment, out isLength);
                else if (parser.Peek() == '[')
                {
                    error = parser.ReadBracketStep(out segment);
                    isLength = false;
                }
                else
                    return parser.Unexpected(position);

                if (error is not null)
                    return error;

                if (isLength)
                {
                    if (parser._i < parser._end)
                        return new JsonPathError(JsonPathErrorKind.LengthNotLast, position);
                    lengthPosition = position;
                }
                else
                {
                    segments.Add(segment);
                }
            }

            result = new JsonPathLite(path[start..end], [.. segments], lengthPosition);
            return null;
        }

        private readonly char Peek(int ahead = 0) => _i + ahead < _end ? _s[_i + ahead] : '\0';

        private readonly bool AtEnd(int ahead = 0) => _i + ahead >= _end;

        private readonly JsonPathError Unexpected(int position) =>
            position >= _end
                ? new JsonPathError(JsonPathErrorKind.UnexpectedEnd, position)
                : new JsonPathError(JsonPathErrorKind.UnexpectedCharacter, position, _s[position].ToString());

        /// <summary>Reads <c>.name</c>, <c>.*</c> or <c>.length()</c>.</summary>
        private JsonPathError? ReadDotStep(out Segment segment, out bool isLength)
        {
            var position = _i;
            segment = default;
            isLength = false;
            _i++;

            if (AtEnd())
                return new JsonPathError(JsonPathErrorKind.UnexpectedEnd, _i);
            if (Peek() == '.')
                return new JsonPathError(JsonPathErrorKind.RecursiveDescent, position, "..");
            if (Peek() == '*')
            {
                _i++;
                segment = new Segment(SegmentKind.Wildcard, null, 0, position);
                return null;
            }

            var nameStart = _i;
            while (!AtEnd() && IsNameChar(Peek()))
                _i++;
            if (_i == nameStart)
                return Unexpected(_i);
            var name = _s[nameStart.._i];

            if (Peek() == '(')
            {
                if (!string.Equals(name, LengthFunction, StringComparison.Ordinal))
                    return new JsonPathError(JsonPathErrorKind.UnknownFunction, nameStart, name);
                if (Peek(1) != ')')
                    return Unexpected(_i + 1);
                _i += 2;
                isLength = true;
                return null;
            }

            segment = new Segment(SegmentKind.Member, name, 0, position);
            return null;
        }

        /// <summary>Reads <c>['name']</c>, <c>["name"]</c>, <c>[n]</c> or <c>[*]</c>.</summary>
        private JsonPathError? ReadBracketStep(out Segment segment)
        {
            var position = _i;
            segment = default;
            _i++;
            SkipSpaces();

            if (AtEnd())
                return new JsonPathError(JsonPathErrorKind.UnexpectedEnd, _i);

            var c = Peek();
            JsonPathError? error = null;
            if (c is '?' or '(')
                return new JsonPathError(JsonPathErrorKind.Filter, position, c.ToString());
            if (c == ':')
                return new JsonPathError(JsonPathErrorKind.Slice, position);
            if (c is '\'' or '"')
            {
                error = ReadQuoted(out var name);
                segment = new Segment(SegmentKind.Member, name, 0, position);
            }
            else if (c == '*')
            {
                _i++;
                segment = new Segment(SegmentKind.Wildcard, null, 0, position);
            }
            else if (c == '-' || char.IsAsciiDigit(c))
            {
                error = ReadIndex(out var index);
                segment = new Segment(SegmentKind.Index, null, index, position);
            }
            else
            {
                return Unexpected(_i);
            }

            if (error is not null)
                return error;

            SkipSpaces();
            if (AtEnd())
                return new JsonPathError(JsonPathErrorKind.UnexpectedEnd, _i);
            switch (Peek())
            {
                case ']':
                    _i++;
                    return null;
                case ',':
                    return new JsonPathError(JsonPathErrorKind.Union, position);
                case ':':
                    return new JsonPathError(JsonPathErrorKind.Slice, position);
                default:
                    return Unexpected(_i);
            }
        }

        private JsonPathError? ReadQuoted(out string name)
        {
            var quote = Peek();
            var open = _i;
            var builder = new StringBuilder();
            name = string.Empty;
            _i++;

            while (!AtEnd())
            {
                var c = Peek();
                if (c == quote)
                {
                    _i++;
                    name = builder.ToString();
                    return null;
                }

                if (c == '\\')
                {
                    if (AtEnd(1))
                        break;
                    var escaped = Peek(1);
                    if (escaped is not ('\'' or '"' or '\\'))
                        return new JsonPathError(JsonPathErrorKind.InvalidEscape, _i, "\\" + escaped);
                    builder.Append(escaped);
                    _i += 2;
                    continue;
                }

                builder.Append(c);
                _i++;
            }

            return new JsonPathError(JsonPathErrorKind.UnterminatedName, open, quote.ToString());
        }

        private JsonPathError? ReadIndex(out int index)
        {
            var start = _i;
            index = 0;
            var negative = Peek() == '-';
            if (negative)
                _i++;

            var digitsStart = _i;
            long value = 0;
            var overflow = false;
            while (!AtEnd() && char.IsAsciiDigit(Peek()))
            {
                if (!overflow)
                {
                    value = value * 10 + (Peek() - '0');
                    overflow = value > int.MaxValue;
                }
                _i++;
            }

            var digits = _i - digitsStart;
            // "[1.5]", "[1e3]" and "[2a]" are not indexes, and "-0" has no meaning from the end.
            if (overflow || digits == 0 || (negative && value == 0) || (!AtEnd() && (Peek() == '.' || char.IsAsciiLetter(Peek()))))
            {
                var stop = _i;
                while (stop < _end && _s[stop] is not (']' or ',' or ':') && !char.IsWhiteSpace(_s[stop]))
                    stop++;
                return new JsonPathError(JsonPathErrorKind.InvalidIndex, start, _s[start..stop]);
            }

            index = negative ? -(int)value : (int)value;
            return null;
        }

        private void SkipSpaces()
        {
            while (!AtEnd() && char.IsWhiteSpace(Peek()))
                _i++;
        }

        private static bool IsNameChar(char c) =>
            char.IsAsciiLetterOrDigit(c)
            || c is '_' or '-' or '$'
            || (c > '\u007F' && !char.IsWhiteSpace(c) && !char.IsControl(c));
    }
}
