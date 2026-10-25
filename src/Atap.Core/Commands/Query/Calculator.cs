using System.Globalization;

namespace Atap.Core.Commands.Query;

/// <summary>Why <see cref="Calculator.Evaluate"/> gave no value.</summary>
public enum CalcError
{
    None,

    /// <summary>The input is blank.</summary>
    Empty,

    /// <summary>The input is longer than <see cref="Calculator.MaxLength"/>.</summary>
    TooLong,

    /// <summary>A character that is not a digit, operator, bracket or space, such as a letter.</summary>
    InvalidCharacter,

    /// <summary>A number that does not read, such as "1.2.3" or "1,23".</summary>
    InvalidNumber,

    /// <summary>An operator or number in the wrong place, such as "5 +", "2 3" or "()".</summary>
    Syntax,

    /// <summary>A bracket without its partner.</summary>
    UnbalancedBrackets,

    DivisionByZero,

    /// <summary>A number or result outside the range of <see cref="decimal"/>.</summary>
    Overflow,

    /// <summary>The result has no real value, such as a fractional power of a negative number.</summary>
    Undefined,
}

/// <summary>The outcome of <see cref="Calculator.Evaluate"/>.</summary>
/// <param name="Value">The result, or 0 when <paramref name="Error"/> is not None.</param>
/// <param name="Error">What went wrong, or None.</param>
/// <param name="Position">Index in the input where the problem was found, or -1.</param>
/// <param name="IsTrivial">
/// True when the input holds no operation, only a number that may be signed or in brackets.
/// The command bar has nothing to show for it.
/// </param>
public readonly record struct CalcResult(decimal Value, CalcError Error, int Position, bool IsTrivial)
{
    public bool Success => Error == CalcError.None;
}

/// <summary>
/// Arithmetic for the command bar. A shunting-yard parser on <see cref="decimal"/>, so 0.1 + 0.2 is 0.3.
/// Nothing is ever evaluated as code.
/// </summary>
/// <remarks>
/// Supported: + - * / ^, the symbols × ÷ and −, brackets, unary minus and plus, and postfix %.
/// Precedence from low to high is + and -, then * and /, then unary minus, then ^ (right to left), then %.
/// So -2^2 is -4 and 2^-1 is 0.5.
/// <para>
/// % divides by 100. On the right of + or - it is a share of the left side, so 200 + 10% is 220
/// and 200 - 10% is 180, as on a desk calculator.
/// </para>
/// <para>
/// Numbers use a dot for decimals and may group thousands with commas, as in Malaysia: 1,234.5.
/// One "=" may lead or trail the input. Letters, implicit multiplication and functions are rejected.
/// </para>
/// </remarks>
public static class Calculator
{
    /// <summary>Longest input that is evaluated.</summary>
    public const int MaxLength = 256;

    private enum Op : byte
    {
        Number,
        Add,
        Subtract,
        Multiply,
        Divide,
        Power,
        Negate,
        Plus,
        Percent,
        OpenBracket,
    }

    private readonly record struct Token(Op Op, decimal Value, int Position);

    private readonly record struct Operand(decimal Value, bool IsPercent);

    /// <summary>Evaluates <paramref name="input"/>. Never throws.</summary>
    public static CalcResult Evaluate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Failure(CalcError.Empty);
        if (input.Length > MaxLength)
            return Failure(CalcError.TooLong, MaxLength);

        var (start, end) = Bounds(input);
        if (start >= end)
            return Failure(CalcError.Empty);

        var output = new List<Token>();
        var operators = new Stack<Token>();
        var expectOperand = true;
        var afterPercent = false;
        var operations = 0;

        var i = start;
        while (i < end)
        {
            var c = input[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (IsNumberChar(c))
            {
                if (!expectOperand)
                    return Failure(CalcError.Syntax, i);

                var length = NumberLength(input.AsSpan(i, end - i));
                var error = TryParseNumber(input.AsSpan(i, length), out var number);
                if (error != CalcError.None)
                    return Failure(error, i);

                output.Add(new Token(Op.Number, number, i));
                expectOperand = false;
                afterPercent = false;
                i += length;
                continue;
            }

            switch (c)
            {
                case '(':
                    if (!expectOperand)
                        return Failure(CalcError.Syntax, i);
                    operators.Push(new Token(Op.OpenBracket, 0, i));
                    break;

                case ')':
                    if (expectOperand)
                        return Failure(CalcError.Syntax, i);
                    if (!PopToOpenBracket(operators, output))
                        return Failure(CalcError.UnbalancedBrackets, i);
                    afterPercent = false;
                    break;

                case '%':
                    if (expectOperand || afterPercent)
                        return Failure(CalcError.Syntax, i);
                    // Postfix and tighter than everything else, so it goes straight to the output.
                    output.Add(new Token(Op.Percent, 0, i));
                    operations++;
                    afterPercent = true;
                    break;

                default:
                    if (!TryReadOperator(c, out var op))
                        return Failure(CalcError.InvalidCharacter, i);

                    if (expectOperand)
                    {
                        if (op is not (Op.Add or Op.Subtract))
                            return Failure(CalcError.Syntax, i);
                        // A prefix operator never pops anything, and an operand is still expected.
                        operators.Push(new Token(op == Op.Subtract ? Op.Negate : Op.Plus, 0, i));
                        break;
                    }

                    PushBinary(new Token(op, 0, i), operators, output);
                    operations++;
                    expectOperand = true;
                    afterPercent = false;
                    break;
            }

            i++;
        }

        if (expectOperand)
            return Failure(CalcError.Syntax, end);

        while (operators.Count > 0)
        {
            var top = operators.Pop();
            if (top.Op == Op.OpenBracket)
                return Failure(CalcError.UnbalancedBrackets, top.Position);
            output.Add(top);
        }

        return Run(output, operations == 0);
    }

    private static CalcResult Run(List<Token> rpn, bool trivial)
    {
        var stack = new Stack<Operand>();
        try
        {
            foreach (var token in rpn)
            {
                switch (token.Op)
                {
                    case Op.Number:
                        stack.Push(new Operand(token.Value, false));
                        break;

                    case Op.Negate:
                        var negated = stack.Pop();
                        stack.Push(negated with { Value = -negated.Value });
                        break;

                    case Op.Plus:
                        break;

                    case Op.Percent:
                        stack.Push(new Operand(stack.Pop().Value / 100, true));
                        break;

                    default:
                        var right = stack.Pop();
                        var left = stack.Pop();
                        var error = Apply(token.Op, left, right, out var value);
                        if (error != CalcError.None)
                            return Failure(error, token.Position);
                        stack.Push(new Operand(value, false));
                        break;
                }
            }
        }
        catch (OverflowException)
        {
            return Failure(CalcError.Overflow);
        }

        var result = stack.Pop().Value;
        // Drop the sign of a negative zero, so it never shows as "-0".
        if (result == 0)
            result = 0m;
        return new CalcResult(result, CalcError.None, -1, trivial);
    }

    private static CalcError Apply(Op op, Operand left, Operand right, out decimal value)
    {
        value = 0;
        switch (op)
        {
            case Op.Add:
                value = left.Value + (right.IsPercent ? left.Value * right.Value : right.Value);
                return CalcError.None;
            case Op.Subtract:
                value = left.Value - (right.IsPercent ? left.Value * right.Value : right.Value);
                return CalcError.None;
            case Op.Multiply:
                value = left.Value * right.Value;
                return CalcError.None;
            case Op.Divide:
                if (right.Value == 0)
                    return CalcError.DivisionByZero;
                value = left.Value / right.Value;
                return CalcError.None;
            case Op.Power:
                return Power(left.Value, right.Value, out value);
            default:
                throw new InvalidOperationException($"Unexpected operator {op}.");
        }
    }

    /// <summary>
    /// Whole exponents are exact, by repeated squaring. Fractional ones go through <see cref="Math.Pow"/>
    /// and keep about 15 significant digits. 0^0 is 1.
    /// </summary>
    private static CalcError Power(decimal x, decimal y, out decimal value)
    {
        value = 0;
        if (y == decimal.Truncate(y) && Math.Abs(y) <= long.MaxValue)
        {
            var n = (long)y;
            if (x == 0)
            {
                if (n < 0)
                    return CalcError.DivisionByZero;
                value = n == 0 ? 1 : 0;
                return CalcError.None;
            }

            var result = 1m;
            var factor = x;
            var remaining = (ulong)Math.Abs(n);
            while (remaining > 0)
            {
                if ((remaining & 1) == 1)
                    result *= factor;
                remaining >>= 1;
                // Squaring only when bits remain keeps a true overflow and a needless one apart.
                if (remaining > 0)
                    factor *= factor;
            }

            if (n >= 0)
            {
                value = result;
                return CalcError.None;
            }

            // A power so small that it rounded to 0 has a reciprocal too big for decimal.
            if (result == 0)
                return CalcError.Overflow;
            value = 1 / result;
            return CalcError.None;
        }

        if (x < 0)
            return CalcError.Undefined;

        var real = Math.Pow((double)x, (double)y);
        if (!double.IsFinite(real) || Math.Abs(real) >= (double)decimal.MaxValue)
            return CalcError.Overflow;
        value = (decimal)real;
        return CalcError.None;
    }

    private static void PushBinary(Token op, Stack<Token> operators, List<Token> output)
    {
        var precedence = Precedence(op.Op);
        var rightToLeft = op.Op == Op.Power;
        while (operators.Count > 0)
        {
            var top = operators.Peek();
            if (top.Op == Op.OpenBracket)
                break;
            var topPrecedence = Precedence(top.Op);
            if (topPrecedence < precedence || (topPrecedence == precedence && rightToLeft))
                break;
            output.Add(operators.Pop());
        }

        operators.Push(op);
    }

    private static bool PopToOpenBracket(Stack<Token> operators, List<Token> output)
    {
        while (operators.Count > 0)
        {
            var top = operators.Pop();
            if (top.Op == Op.OpenBracket)
                return true;
            output.Add(top);
        }

        return false;
    }

    private static int Precedence(Op op) => op switch
    {
        Op.Add or Op.Subtract => 1,
        Op.Multiply or Op.Divide => 2,
        Op.Negate or Op.Plus => 3,
        Op.Power => 4,
        _ => 0,
    };

    private static bool TryReadOperator(char c, out Op op)
    {
        op = c switch
        {
            '+' => Op.Add,
            '-' or '−' => Op.Subtract,
            '*' or '×' => Op.Multiply,
            '/' or '÷' => Op.Divide,
            '^' => Op.Power,
            _ => Op.Number,
        };
        return op != Op.Number;
    }

    /// <summary>Skips spaces and one leading and one trailing "=".</summary>
    private static (int Start, int End) Bounds(string input)
    {
        var start = 0;
        var end = input.Length;
        while (start < end && char.IsWhiteSpace(input[start]))
            start++;
        while (end > start && char.IsWhiteSpace(input[end - 1]))
            end--;
        if (start < end && input[start] == '=')
            start++;
        if (end > start && input[end - 1] == '=')
            end--;
        return (start, end);
    }

    private static CalcResult Failure(CalcError error, int position = -1) => new(0, error, position, false);

    internal static bool IsNumberChar(char c) => c is (>= '0' and <= '9') or '.' or ',';

    /// <summary>Length of the run of digits, dots and commas at the start of <paramref name="text"/>.</summary>
    internal static int NumberLength(ReadOnlySpan<char> text)
    {
        var length = 0;
        while (length < text.Length && IsNumberChar(text[length]))
            length++;
        return length;
    }

    /// <summary>
    /// Reads an unsigned number with a dot for decimals and optional comma groups of three, such as 1,234.5.
    /// Returns <see cref="CalcError.InvalidNumber"/> for a bad shape and <see cref="CalcError.Overflow"/> when too big.
    /// </summary>
    internal static CalcError TryParseNumber(ReadOnlySpan<char> text, out decimal value)
    {
        value = 0;
        var dot = text.IndexOf('.');
        var whole = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? ReadOnlySpan<char>.Empty : text[(dot + 1)..];
        if (whole.Length + fraction.Length == 0 || fraction.ContainsAny('.', ','))
            return CalcError.InvalidNumber;

        Span<char> digits = stackalloc char[text.Length];
        var count = 0;
        var group = 0;
        var grouped = false;
        foreach (var c in whole)
        {
            if (c == ',')
            {
                // The first group has 1 to 3 digits and every later group exactly 3.
                if (group == 0 || (grouped ? group != 3 : group > 3))
                    return CalcError.InvalidNumber;
                grouped = true;
                group = 0;
                continue;
            }

            digits[count++] = c;
            group++;
        }

        if (grouped && group != 3)
            return CalcError.InvalidNumber;

        if (dot >= 0)
        {
            digits[count++] = '.';
            fraction.CopyTo(digits[count..]);
            count += fraction.Length;
        }

        return decimal.TryParse(digits[..count], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
            ? CalcError.None
            : CalcError.Overflow;
    }
}
