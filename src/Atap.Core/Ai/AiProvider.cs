namespace Atap.Core.Ai;

/// <summary>The services Ask AI can use. Each one has a fixed address, except the user's own server.</summary>
public enum AiProvider
{
    /// <summary>Claude, from Anthropic.</summary>
    Anthropic,

    /// <summary>ChatGPT, from OpenAI.</summary>
    OpenAi,

    /// <summary>Gemini, from Google.</summary>
    Gemini,
    OpenRouter,

    /// <summary>A server the user runs, such as Ollama, that speaks the OpenAI chat format.</summary>
    Custom,
}

/// <summary>Setting ids, addresses and key pages for each <see cref="AiProvider"/>.</summary>
public static class AiProviders
{
    /// <summary>The setting ids, in the order Settings lists them.</summary>
    public static IReadOnlyList<string> Ids { get; } = ["anthropic", "openai", "gemini", "openrouter", "custom"];

    /// <summary>The address "your own server" starts with: Ollama on this PC.</summary>
    public static readonly Uri DefaultServer = new("http://localhost:11434/v1");

    public static string IdOf(AiProvider provider) => provider switch
    {
        AiProvider.Anthropic => "anthropic",
        AiProvider.OpenAi => "openai",
        AiProvider.Gemini => "gemini",
        AiProvider.OpenRouter => "openrouter",
        _ => "custom",
    };

    /// <summary>The provider for a setting id. False for anything else.</summary>
    public static bool TryParse(string? id, out AiProvider provider)
    {
        provider = id switch
        {
            "anthropic" => AiProvider.Anthropic,
            "openai" => AiProvider.OpenAi,
            "gemini" => AiProvider.Gemini,
            "openrouter" => AiProvider.OpenRouter,
            "custom" => AiProvider.Custom,
            _ => (AiProvider)(-1),
        };
        return Enum.IsDefined(provider);
    }

    /// <summary>Where requests go. The fixed address for a service, or <paramref name="server"/> for your own server.</summary>
    public static Uri BaseUrl(AiProvider provider, Uri? server = null) => provider switch
    {
        AiProvider.Anthropic => new Uri("https://api.anthropic.com/v1"),
        AiProvider.OpenAi => new Uri("https://api.openai.com/v1"),
        AiProvider.Gemini => new Uri("https://generativelanguage.googleapis.com/v1beta"),
        AiProvider.OpenRouter => new Uri("https://openrouter.ai/api/v1"),
        _ => server ?? DefaultServer,
    };

    /// <summary>The page where the user makes a key. Null for your own server.</summary>
    public static Uri? KeyPage(AiProvider provider) => provider switch
    {
        AiProvider.Anthropic => new Uri("https://console.anthropic.com/settings/keys"),
        AiProvider.OpenAi => new Uri("https://platform.openai.com/api-keys"),
        AiProvider.Gemini => new Uri("https://aistudio.google.com/app/apikey"),
        AiProvider.OpenRouter => new Uri("https://openrouter.ai/settings/keys"),
        _ => null,
    };

    /// <summary>True when the service needs a key. Your own server may work without one.</summary>
    public static bool NeedsKey(AiProvider provider) => provider != AiProvider.Custom;

    /// <summary>
    /// The service a pasted key seems to be for, from how it starts, or null when it gives no hint. Used only to
    /// warn that the key may belong to another service. The key is never stored or sent by this.
    /// </summary>
    public static AiProvider? GuessFromKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        if (key.StartsWith("sk-ant-", StringComparison.Ordinal))
            return AiProvider.Anthropic;
        if (key.StartsWith("sk-or-", StringComparison.Ordinal))
            return AiProvider.OpenRouter;
        if (key.StartsWith("AIza", StringComparison.Ordinal))
            return AiProvider.Gemini;
        if (key.StartsWith("sk-", StringComparison.Ordinal))
            return AiProvider.OpenAi;
        return null;
    }
}
