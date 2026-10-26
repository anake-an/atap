namespace Atap.Core.Ai;

/// <summary>
/// The service, model and key one question goes to. Not a record on purpose: the key stays out of
/// <see cref="ToString"/>, equality and anything else that prints or compares it. Only the dialects read it.
/// </summary>
public sealed class AiAccount
{
    /// <param name="provider">The service.</param>
    /// <param name="model">The model id. See <see cref="AiModels"/>.</param>
    /// <param name="key">The key, or null for your own server without one.</param>
    /// <param name="server">Your own server's address, such as http://localhost:11434/v1. Ignored for the services.</param>
    public AiAccount(AiProvider provider, string model, string? key, Uri? server = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        Provider = provider;
        Model = model;
        Key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        BaseUrl = AiProviders.BaseUrl(provider, server);
    }

    public AiProvider Provider { get; }

    public string Model { get; }

    /// <summary>Where requests go. Fixed for each service.</summary>
    public Uri BaseUrl { get; }

    public bool HasKey => Key is not null;

    /// <summary>Read by the dialects only, to put the key in a header.</summary>
    internal string? Key { get; }

    /// <summary>The service and model. Never the key.</summary>
    public override string ToString() => $"{AiProviders.IdOf(Provider)} {Model}";
}
