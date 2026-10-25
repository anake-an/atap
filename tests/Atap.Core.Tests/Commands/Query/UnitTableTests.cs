using Atap.Core.Commands.Query;

namespace Atap.Core.Tests.Commands.Query;

public class UnitTableTests
{
    private static readonly string[] ConnectorWords = ["to", "in", "into", "ke", "dalam"];

    [Fact]
    public void Ids_AreUniqueLowercaseAscii()
    {
        var ids = UnitTable.All.Select(u => u.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Matches("^[a-z0-9/]+$", id));
    }

    [Fact]
    public void EveryName_FindsItsOwnUnit()
    {
        // A name shared by two units would find the first one for both, so this also proves no name is shared.
        foreach (var unit in UnitTable.All)
        {
            Assert.Same(unit, UnitTable.Find(unit.Id));
            Assert.Same(unit, UnitTable.Find(unit.Symbol));
            Assert.Same(unit, UnitTable.Find(unit.Symbol.ToUpperInvariant()));
            foreach (var alias in unit.Aliases)
                Assert.Same(unit, UnitTable.Find(alias));
        }
    }

    [Fact]
    public void NoName_HoldsAConnectorWord()
    {
        foreach (var unit in UnitTable.All)
        {
            foreach (var name in unit.Aliases.Append(unit.Symbol))
            {
                var words = name.Split(' ');
                Assert.True(words.Length == 1 || !words.Intersect(ConnectorWords).Any(), $"{unit.Id}: {name}");
            }
        }
    }

    [Fact]
    public void EveryCategory_HasOneBaseUnit()
    {
        foreach (var category in Enum.GetValues<UnitCategory>())
        {
            var units = UnitTable.All.Where(u => u.Category == category).ToList();

            Assert.NotEmpty(units);
            Assert.Single(units, u => u.Numerator == u.Denominator && u.Offset == 0);
            Assert.All(units, u => Assert.True(u.Numerator > 0 && u.Denominator > 0, u.Id));
        }
    }

    [Theory]
    [InlineData("°C", "c")]
    [InlineData("ºC", "c")]
    [InlineData("celsius", "c")]
    [InlineData("m²", "m2")]
    [InlineData("m^2", "m2")]
    [InlineData("sq   m", "m2")]
    [InlineData("  KM  ", "km")]
    [InlineData("meter persegi", "m2")]
    [InlineData("kaki persegi", "ft2")]
    [InlineData("km/j", "km/h")]
    [InlineData("in", "in")]
    [InlineData("gelas", null)]
    [InlineData("usd", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Find_NormalizesNames(string? name, string? id)
    {
        Assert.Equal(id, UnitTable.Find(name)?.Id);
    }
}
