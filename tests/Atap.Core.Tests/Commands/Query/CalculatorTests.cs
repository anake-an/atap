using Atap.Core.Commands.Query;

namespace Atap.Core.Tests.Commands.Query;

public class CalculatorTests
{
    // Input on the left, the shown result (QueryNumber.Format without grouping) on the right.
    private const string Results = """
        12*8                => 96
        2+3*4               => 14
        2*3+4               => 10
        (2+3)*4             => 20
        2*(3+4)             => 14
        ((1+2)*(3+4))       => 21
        10-4-3              => 3
        100/10/5            => 2
        7/2                 => 3.5
        2^10                => 1024
        2^3^2               => 512
        (2^3)^2             => 64
        -2^2                => -4
        (-2)^2              => 4
        2^-1                => 0.5
        0.5^-2              => 4
        0^0                 => 1
        -3                  => -3
        -(3+4)              => -7
        --5                 => 5
        +5                  => 5
        3*-2                => -6
        3--2                => 5
        -2*-3               => 6
        10 - -2             => 12
        50%                 => 0.5
        200 + 10%           => 220
        200 - 10%           => 180
        200 * 10%           => 20
        200 / 10%           => 2000
        10% + 5             => 5.1
        (200 + 10%) * 2     => 440
        200 + -10%          => 180
        50%^2               => 0.25
        0.1 + 0.2           => 0.3
        .5 + .25            => 0.75
        1/3*3               => 1
        2/3                 => 0.666666666666667
        1-1                 => 0
        -0                  => 0
        1,234.5 * 2         => 2469
        1,000,000 / 4       => 250000
        6 ÷ 3 × 2           => 4
        7 − 2               => 5
        = 12 * 8            => 96
        12*8=               => 96
        2^0.5               => 1.4142135623731
        9^0.5               => 3
        """;

    // Input on the left, the error and the position it points at on the right.
    private const string Errors = """
        12*x                            => InvalidCharacter 3
        abc                             => InvalidCharacter 0
        5 km                            => InvalidCharacter 2
        2 mod 3                         => InvalidCharacter 2
        1e5                             => InvalidCharacter 1
        sqrt(4)                         => InvalidCharacter 0
        12x8                            => InvalidCharacter 2
        1/0                             => DivisionByZero 1
        5/(2-2)                         => DivisionByZero 1
        0^-1                            => DivisionByZero 1
        (1+2                            => UnbalancedBrackets 0
        1+2)                            => UnbalancedBrackets 3
        ((1)                            => UnbalancedBrackets 0
        1+                              => Syntax 2
        *2                              => Syntax 0
        2 3                             => Syntax 2
        ()                              => Syntax 1
        2(3)                            => Syntax 1
        (2)3                            => Syntax 3
        5%%                             => Syntax 2
        %5                              => Syntax 0
        2**3                            => Syntax 2
        1.2.3                           => InvalidNumber 0
        1,23                            => InvalidNumber 0
        12,34,567                       => InvalidNumber 0
        1234,567                        => InvalidNumber 0
        1.5,000                         => InvalidNumber 0
        .                               => InvalidNumber 0
        99999999999999999999999999999   => Overflow 0
        79228162514264337593543950335*2 => Overflow -1
        2^100                           => Overflow -1
        0.5^-1000                       => Overflow 3
        (-8)^0.5                        => Undefined 4
        """;

    public static TheoryData<string, string> ResultCases => Table(Results);

    public static TheoryData<string, string> ErrorCases => Table(Errors);

    [Theory]
    [MemberData(nameof(ResultCases))]
    public void Evaluate_GivesTheExpectedResult(string input, string expected)
    {
        var result = Calculator.Evaluate(input);

        Assert.Equal(CalcError.None, result.Error);
        Assert.Equal(expected, QueryNumber.Format(result.Value, grouping: false));
    }

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public void Evaluate_ReportsTheError(string input, string expected)
    {
        var parts = expected.Split(' ');

        var result = Calculator.Evaluate(input);

        Assert.False(result.Success);
        Assert.Equal(Enum.Parse<CalcError>(parts[0]), result.Error);
        Assert.Equal(int.Parse(parts[1]), result.Position);
        Assert.Equal(0m, result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("=")]
    public void Evaluate_BlankInputIsEmpty(string? input)
    {
        Assert.Equal(CalcError.Empty, Calculator.Evaluate(input).Error);
    }

    [Fact]
    public void Evaluate_IgnoresSpacesAroundAndInside()
    {
        Assert.Equal(96m, Calculator.Evaluate("   12 *    8   ").Value);
        Assert.Equal(96m, Calculator.Evaluate("\t12\t*\t8").Value);
    }

    [Fact]
    public void Evaluate_IsExactOnDecimals()
    {
        Assert.Equal(0.3m, Calculator.Evaluate("0.1+0.2").Value);
        Assert.Equal(1.1m, Calculator.Evaluate("3.3/3").Value);
    }

    [Theory]
    [InlineData("42", true)]
    [InlineData("-5", true)]
    [InlineData("(7)", true)]
    [InlineData("+(-(3))", true)]
    [InlineData("1+1", false)]
    [InlineData("50%", false)]
    [InlineData("2^2", false)]
    public void Evaluate_MarksInputWithoutAnOperationAsTrivial(string input, bool trivial)
    {
        var result = Calculator.Evaluate(input);

        Assert.True(result.Success);
        Assert.Equal(trivial, result.IsTrivial);
    }

    [Fact]
    public void Evaluate_RejectsInputOverTheLengthLimit()
    {
        var longest = string.Concat(Enumerable.Repeat("1+", 127)) + "1";
        Assert.Equal(Calculator.MaxLength - 1, longest.Length);
        Assert.Equal(128m, Calculator.Evaluate(longest).Value);

        var tooLong = new string('1', Calculator.MaxLength + 1);
        Assert.Equal(CalcError.TooLong, Calculator.Evaluate(tooLong).Error);
    }

    [Fact]
    public void Evaluate_DeepBracketsDoNotOverflowTheStack()
    {
        var input = new string('(', 120) + "1" + new string(')', 120);

        Assert.Equal(1m, Calculator.Evaluate(input).Value);
    }

    [Fact]
    public void Evaluate_NeverThrowsOnRandomInput()
    {
        const string alphabet = "0123456789.,+-*/^%() =x×÷−";
        var random = new Random(20261025);

        for (var n = 0; n < 5000; n++)
        {
            var chars = new char[random.Next(1, 24)];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = alphabet[random.Next(alphabet.Length)];

            var input = new string(chars);
            var result = Calculator.Evaluate(input);

            Assert.True(result.Success || result.Value == 0, input);
        }
    }

    private static TheoryData<string, string> Table(string fixture)
    {
        var data = new TheoryData<string, string>();
        foreach (var line in fixture.Split('\n'))
        {
            var arrow = line.IndexOf(" => ", StringComparison.Ordinal);
            data.Add(line[..arrow].Trim(), line[(arrow + 4)..].Trim());
        }

        return data;
    }
}
