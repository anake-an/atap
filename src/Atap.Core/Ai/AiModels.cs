namespace Atap.Core.Ai;

/// <summary>Fast is quick and cheap. Smarter takes longer and costs more.</summary>
public enum AiSpeed
{
    Fast,
    Smarter,
}

/// <summary>
/// The model ids Atap picks for "Fast" and "Smarter" on each service. This is the only place they live, so a
/// newer model is a one line change. Any other id can be typed under Advanced.
/// </summary>
public static class AiModels
{
    /// <summary>Longest model id accepted. Ids are short names such as "claude-haiku-5-5".</summary>
    public const int MaxIdLength = 128;

    public static string Default(AiProvider provider, AiSpeed speed) => (provider, speed) switch
    {
        (AiProvider.Anthropic, AiSpeed.Fast) => "claude-haiku-5-5",
        (AiProvider.Anthropic, _) => "claude-sonnet-5-5",
        (AiProvider.OpenAi, AiSpeed.Fast) => "gpt-5-mini",
        (AiProvider.OpenAi, _) => "gpt-5",
        (AiProvider.Gemini, AiSpeed.Fast) => "gemini-2.5-flash",
        (AiProvider.Gemini, _) => "gemini-2.5-pro",
        (AiProvider.OpenRouter, AiSpeed.Fast) => "openai/gpt-5-mini",
        (AiProvider.OpenRouter, _) => "anthropic/claude-sonnet-5-5",
        (_, AiSpeed.Fast) => "llama3.2",
        _ => "llama3.1",
    };

    /// <summary>
    /// True for an id made of letters, digits and <c>. _ - : / @</c>, starting with a letter or digit. Nothing that
    /// could change the address or a header gets through, and Gemini's ids may not hold a slash.
    /// </summary>
    public static bool IsValidId(string? id, AiProvider provider)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength || !char.IsAsciiLetterOrDigit(id[0]))
            return false;
        if (id.Contains("..", StringComparison.Ordinal))
            return false;
        foreach (var c in id)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ':' or '@')
                continue;
            if (c == '/' && provider != AiProvider.Gemini)
                continue;
            return false;
        }
        return true;
    }
}
