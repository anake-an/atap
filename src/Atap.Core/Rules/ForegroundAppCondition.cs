namespace Atap.Core.Rules;

/// <summary>
/// Matches while the foreground window belongs to one of <see cref="Apps"/>.
/// Names are compared without their folder, ignoring case, and with or without ".exe",
/// so "VALORANT.exe", "valorant" and "C:\Riot Games\VALORANT\live\VALORANT.exe" are the same app.
/// </summary>
public sealed class ForegroundAppCondition : RuleCondition
{
    private const string ExeSuffix = ".exe";

    public override ConditionKind Kind => ConditionKind.ForegroundApp;

    /// <summary>Executable names, for example "valorant.exe".</summary>
    public List<string> Apps { get; set; } = [];

    public override bool IsValid => Apps?.Exists(app => Normalize(app).Length > 0) ?? false;

    public override bool Matches(RuleContext context)
    {
        if (Apps is null || string.IsNullOrWhiteSpace(context.ForegroundProcessName))
            return false;

        foreach (var app in Apps)
        {
            if (NameMatches(app, context.ForegroundProcessName))
                return true;
        }
        return false;
    }

    /// <summary>Whether an app name from a rule and a running process name are the same executable.</summary>
    public static bool NameMatches(string? configured, string? processName)
    {
        var left = Normalize(configured);
        return left.Length > 0 && left.Equals(Normalize(processName), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The file name without its folder, quotes, surrounding spaces or ".exe".</summary>
    private static ReadOnlySpan<char> Normalize(string? name)
    {
        if (name is null)
            return [];

        var span = name.AsSpan().Trim().Trim('"');
        var slash = span.LastIndexOfAny('\\', '/');
        if (slash >= 0)
            span = span[(slash + 1)..];
        if (span.EndsWith(ExeSuffix, StringComparison.OrdinalIgnoreCase))
            span = span[..^ExeSuffix.Length];
        return span.Trim();
    }
}
