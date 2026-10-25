using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Atap.Core.Localization;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Guards;

/// <summary>
/// Copy rules for every string file in the app: English and Bahasa Melayu only, the same keys and
/// placeholders in both, keys that start with their area, and no em dash or semicolon in any value.
/// </summary>
public partial class StringsParityTests
{
    private static readonly List<string> Files = RepoPaths.Files(RepoPaths.App, "*.json")
        .Where(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(f)), "Strings", StringComparison.Ordinal))
        .ToList();

    [Fact]
    public void StringFilesExist()
    {
        Assert.NotEmpty(Files);
    }

    [Fact]
    public void OnlyEnglishAndMalayFilesExist()
    {
        foreach (var file in Files)
        {
            var name = Path.GetFileName(file);
            Assert.True(
                FileNamePattern().IsMatch(name),
                $"{RepoPaths.Relative(file)} must be named <area>.en.json or <area>.ms.json.");
        }
    }

    [Fact]
    public void EveryAreaHasBothLanguages()
    {
        foreach (var group in Files.GroupBy(AreaOf))
        {
            var languages = group.Select(LanguageOf).Order().ToList();
            Assert.True(languages.SequenceEqual(["en", "ms"]), $"Area '{group.Key}' has {string.Join(", ", languages)}; it needs en and ms once each.");
        }
    }

    [Fact]
    public void EnglishAndMalay_HaveTheSameKeysAndPlaceholders()
    {
        foreach (var group in Files.GroupBy(AreaOf))
        {
            var english = Read(group.Single(f => LanguageOf(f) == "en"));
            var malay = Read(group.Single(f => LanguageOf(f) == "ms"));

            Assert.True(english.Keys.Order().SequenceEqual(malay.Keys.Order()), $"Area '{group.Key}' has different keys in en and ms.");
            foreach (var (key, value) in english)
            {
                Assert.True(
                    Placeholders(value).SetEquals(Placeholders(malay[key])),
                    $"'{key}' has different placeholders in en and ms.");
            }
        }
    }

    [Fact]
    public void KeysStartWithTheirArea()
    {
        foreach (var file in Files)
        {
            var area = AreaOf(file);
            foreach (var key in Read(file).Keys)
                Assert.True(key.StartsWith(area + ".", StringComparison.Ordinal), $"'{key}' in {RepoPaths.Relative(file)} must start with '{area}.'.");
        }
    }

    [Fact]
    public void NoKeyIsDefinedTwice()
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Files.Where(f => LanguageOf(f) == "en"))
        {
            foreach (var key in Read(file).Keys)
                Assert.True(seen.TryAdd(key, file), $"'{key}' is in {RepoPaths.Relative(file)} and {RepoPaths.Relative(seen.GetValueOrDefault(key) ?? file)}.");
        }
    }

    [Fact]
    public void ValuesHaveNoEmDashOrSemicolon()
    {
        foreach (var file in Files)
        {
            foreach (var (key, value) in Read(file))
            {
                Assert.False(value.Contains('—'), $"'{key}' in {RepoPaths.Relative(file)} has an em dash.");
                Assert.False(value.Contains(';'), $"'{key}' in {RepoPaths.Relative(file)} has a semicolon.");
            }
        }
    }

    [Fact]
    public void AreaNamesAreUnique()
    {
        // The build fails on a duplicate too (two resources with one name), but this says which folders clash.
        foreach (var group in Files.GroupBy(f => Path.GetFileName(f)))
            Assert.True(group.Count() == 1, $"{group.Key} exists in {string.Join(", ", group.Select(RepoPaths.Relative))}.");
    }

    [Fact]
    public void EveryFileIsEmbeddedUnderItsAreaName()
    {
        var embedded = typeof(App).Assembly.GetManifestResourceNames().ToHashSet(StringComparer.Ordinal);

        foreach (var file in Files)
            Assert.Contains($"{Localizer.ResourcePrefix}{Path.GetFileName(file)}", embedded);
    }

    private static string AreaOf(string file) => Path.GetFileName(file).Split('.')[0];

    private static string LanguageOf(string file) => Path.GetFileName(file).Split('.')[^2];

    private static HashSet<string> Placeholders(string value) =>
        PlaceholderPattern().Matches(value).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>Reads a flat string file and fails on a duplicate key or a value that is not a string.</summary>
    private static Dictionary<string, string> Read(string file)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(File.ReadAllText(file)), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });
        Assert.True(reader.Read() && reader.TokenType == JsonTokenType.StartObject, $"{RepoPaths.Relative(file)} must be one JSON object.");
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var key = reader.GetString()!;
            Assert.True(reader.Read() && reader.TokenType == JsonTokenType.String, $"'{key}' in {RepoPaths.Relative(file)} must be a string.");
            Assert.True(values.TryAdd(key, reader.GetString()!), $"'{key}' is twice in {RepoPaths.Relative(file)}.");
        }
        return values;
    }

    [GeneratedRegex(@"^[a-z][a-z0-9-]*\.(en|ms)\.json$")]
    private static partial Regex FileNamePattern();

    [GeneratedRegex(@"\{([A-Za-z0-9]+)\}")]
    private static partial Regex PlaceholderPattern();
}
