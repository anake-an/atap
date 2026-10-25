namespace Atap.Core.Commands.Query;

/// <summary>What a unit measures. Units convert only within one category.</summary>
public enum UnitCategory
{
    /// <summary>Base unit: metre.</summary>
    Length,

    /// <summary>Base unit: kilogram.</summary>
    Mass,

    /// <summary>Base unit: kelvin.</summary>
    Temperature,

    /// <summary>Base unit: litre.</summary>
    Volume,

    /// <summary>Base unit: square metre.</summary>
    Area,

    /// <summary>Base unit: metre per second.</summary>
    Speed,

    /// <summary>Base unit: second.</summary>
    Time,

    /// <summary>Base unit: byte.</summary>
    Data,

    /// <summary>Base unit: pascal.</summary>
    Pressure,

    /// <summary>Base unit: joule.</summary>
    Energy,
}

/// <summary>
/// One unit in <see cref="UnitTable"/>. A value v in this unit is
/// (v + <see cref="Offset"/>) * <see cref="Numerator"/> / <see cref="Denominator"/> in the base unit of its category.
/// </summary>
/// <remarks>
/// The factor is a fraction so that units such as km/h (1000/3600 m/s) stay exact until the one division
/// a conversion does. Only temperatures have an offset.
/// </remarks>
public sealed class UnitDefinition
{
    internal UnitDefinition(
        string id, string symbol, UnitCategory category, decimal numerator, decimal denominator, decimal offset, string[] aliases)
    {
        Id = id;
        Symbol = symbol;
        Category = category;
        Numerator = numerator;
        Denominator = denominator;
        Offset = offset;
        Aliases = aliases;
    }

    /// <summary>Stable lowercase ASCII name, such as "km" or "c".</summary>
    public string Id { get; }

    /// <summary>Text to show, such as "km" or "°C". The same in English and Bahasa Melayu.</summary>
    public string Symbol { get; }

    public UnitCategory Category { get; }

    public decimal Numerator { get; }

    public decimal Denominator { get; }

    public decimal Offset { get; }

    /// <summary>Other names that find this unit, in English and Bahasa Melayu, besides <see cref="Id"/> and <see cref="Symbol"/>.</summary>
    public IReadOnlyList<string> Aliases { get; }

    public override string ToString() => Symbol;
}
