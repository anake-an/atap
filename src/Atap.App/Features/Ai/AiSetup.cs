using System.IO;
using System.Security.Cryptography;
using Atap.Core.Ai;
using Atap.Core.Settings;
using Atap.Core.Storage;
using Atap.Core.Widgets;

namespace Atap.Features.Ai;

/// <summary>What is missing before a question can go.</summary>
internal enum AiSetupProblem
{
    None,
    NoKey,

    /// <summary>The key was saved on another PC or for another Windows user, so it does not open here.</summary>
    KeyFromOtherPc,
    BadServer,
}

/// <summary>How a key save went.</summary>
internal enum AiKeySave
{
    Saved,
    Empty,

    /// <summary>Spaces or other letters a key never has.</summary>
    NotAKey,

    /// <summary>Windows data protection cannot store it on this PC.</summary>
    Unavailable,

    /// <summary>The settings file could not be written. The old key stays.</summary>
    NotSaved,
}

/// <summary>
/// Reads the Ask AI settings into an <see cref="AiAccount"/>, and saves and removes keys. Keys live in the
/// secret store with Windows data protection, and settings hold only the "secret:" reference. Every ai.*
/// setting is Machine, so none of it syncs.
/// </summary>
internal static class AiSetup
{
    /// <summary>The longest key accepted. Real keys are well under this.</summary>
    public const int MaxKeyLength = 512;

    public static AiProvider Provider(SettingValues values) =>
        AiProviders.TryParse(values.GetString(AiDescriptor.ProviderKey), out var provider) ? provider : AiProvider.Anthropic;

    public static AiSpeed Speed(SettingValues values) =>
        values.GetString(AiDescriptor.SpeedKey) == "smarter" ? AiSpeed.Smarter : AiSpeed.Fast;

    /// <summary>The model typed under Advanced when it is a valid id, else Fast or Smarter for the service.</summary>
    public static string Model(SettingValues values, AiProvider provider)
    {
        var typed = values.GetString(AiDescriptor.ModelKey).Trim();
        return typed.Length > 0 && AiModels.IsValidId(typed, provider) ? typed : AiModels.Default(provider, Speed(values));
    }

    /// <summary>Your own server's address, or null when what is stored cannot be used.</summary>
    public static Uri? Server(SettingValues values) =>
        Uri.TryCreate(values.GetString(AiDescriptor.ServerKey).Trim(), UriKind.Absolute, out var url) && AiTransport.IsValidServer(url) ? url : null;

    /// <summary>The account for the chosen service, or null with what is missing.</summary>
    public static AiAccount? Account(SettingValues values, ISecretStore secrets, out AiSetupProblem problem)
    {
        var provider = Provider(values);
        Uri? server = null;
        if (provider == AiProvider.Custom && (server = Server(values)) is null)
        {
            problem = AiSetupProblem.BadServer;
            return null;
        }
        var reference = values.GetSecretRef(AiDescriptor.KeyFor(provider));
        var key = reference is null || !secrets.IsAvailable ? null : secrets.Get(reference);
        if (key is null && AiProviders.NeedsKey(provider))
        {
            problem = reference is null ? AiSetupProblem.NoKey : AiSetupProblem.KeyFromOtherPc;
            return null;
        }
        problem = AiSetupProblem.None;
        return new AiAccount(provider, Model(values, provider), key, server);
    }

    /// <summary>True when a key is saved for this service and opens on this PC.</summary>
    public static bool HasKey(SettingValues values, ISecretStore secrets, AiProvider provider) =>
        values.GetSecretRef(AiDescriptor.KeyFor(provider)) is { } reference && secrets.IsAvailable && secrets.Get(reference) is not null;

    /// <summary>A key saved here that does not open on this PC.</summary>
    public static bool HasKeyFromOtherPc(SettingValues values, ISecretStore secrets, AiProvider provider) =>
        values.GetSecretRef(AiDescriptor.KeyFor(provider)) is not null && !HasKey(values, secrets, provider);

    /// <summary>The key with spaces trimmed. Null when it is empty or holds letters a key never has.</summary>
    public static string? Clean(string? pasted, out AiKeySave problem)
    {
        var key = pasted?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            problem = AiKeySave.Empty;
            return null;
        }
        if (key.Length > MaxKeyLength || key.Any(c => c is <= ' ' or > '~'))
        {
            problem = AiKeySave.NotAKey;
            return null;
        }
        problem = AiKeySave.Saved;
        return key;
    }

    /// <summary>
    /// Stores the key and writes only its reference. The old key goes once the file on disk names the new one,
    /// so a failed write never leaves the setting pointing at nothing.
    /// </summary>
    public static AiKeySave SaveKey(IAppSettings settings, ISecretStore secrets, AiProvider provider, string pasted)
    {
        if (Clean(pasted, out var problem) is not { } key)
            return problem;
        if (!secrets.IsAvailable)
            return AiKeySave.Unavailable;

        var setting = AiDescriptor.KeyFor(provider);
        var old = settings.Values.GetSecretRef(setting);
        string reference;
        try
        {
            reference = secrets.Put(key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or CryptographicException)
        {
            return AiKeySave.Unavailable;
        }

        switch (settings.Set(setting, reference))
        {
            case SettingWriteResult.Applied or SettingWriteResult.Unchanged:
                if (old is not null && old != reference && settings.Flush() == SettingWriteResult.Applied)
                    TryDelete(secrets, old);
                return AiKeySave.Saved;
            case SettingWriteResult.Invalid:
                TryDelete(secrets, reference);
                return AiKeySave.NotSaved;
            default:
                // In memory only. The file still names the old key, so both are kept.
                return AiKeySave.NotSaved;
        }
    }

    /// <summary>Forgets the key for this service. False when the settings file could not be written, and the key stays.</summary>
    public static bool RemoveKey(IAppSettings settings, ISecretStore secrets, AiProvider provider)
    {
        var setting = AiDescriptor.KeyFor(provider);
        if (settings.Values.GetSecretRef(setting) is not { } old)
            return settings.Set(setting, null) is not (SettingWriteResult.Failed or SettingWriteResult.ReadOnly);
        if (settings.Set(setting, null) is not SettingWriteResult.Applied || settings.Flush() != SettingWriteResult.Applied)
            return false;
        TryDelete(secrets, old);
        return true;
    }

    /// <summary>Forgets every Ask AI key, for Clear saved data. The number of keys removed.</summary>
    public static int RemoveAllKeys(IAppSettings settings, ISecretStore secrets)
    {
        var removed = 0;
        foreach (var id in AiProviders.Ids)
        {
            if (!AiProviders.TryParse(id, out var provider) || settings.Values.GetSecretRef(AiDescriptor.KeyFor(provider)) is null)
                continue;
            if (RemoveKey(settings, secrets, provider))
                removed++;
        }
        return removed;
    }

    private static void TryDelete(ISecretStore secrets, string reference)
    {
        try
        {
            secrets.Delete(reference);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The blob stays in secrets.json, unused, and goes with Delete all my Atap data.
        }
    }
}
