using Atap.Composition;
using Atap.Core.Ai;
using Atap.Core.Commands;
using Atap.Core.Localization;
using Atap.Core.Settings;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Diagnostics;
using Atap.Features.Ai;
using Atap.Features.NetworkPrivacy;

[assembly: AtapFeature(typeof(AiDescriptor))]

namespace Atap.Features.Ai;

/// <summary>
/// Ask AI: a question typed in the command bar goes to the service the user picked, with their own key, and
/// the answer streams back into the bar. Off by default and in safe mode. Every setting stays on this PC.
/// </summary>
internal sealed class AiDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "ai";
    public const string EnabledKey = "ai.enabled";
    public const string ProviderKey = "ai.provider";
    public const string SpeedKey = "ai.speed";
    public const string ModelKey = "ai.model";
    public const string ServerKey = "ai.server";
    public const string KeyPrefix = "ai.key.";

    /// <summary>Opens the command bar ready to ask. The hotkey <c>hotkeys.ai.ask</c> runs it.</summary>
    public const string AskCommand = "ai.ask";

    /// <summary>What <see cref="AskCommand"/> passes to the command bar's open command.</summary>
    public const string AskArgument = "ai";

    public static string KeyFor(AiProvider provider) => KeyPrefix + AiProviders.IdOf(provider);

    public string Id => FeatureId;

    /// <summary>All Machine, so nothing here syncs, and none is in the generated form: the Ask AI card shows them.</summary>
    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(EnabledKey, "ai.toggle", SettingType.Bool, false) { Scope = SettingScope.Machine },
        new(ProviderKey, "ai.provider", SettingType.Choice, "anthropic", AiProviders.Ids) { Scope = SettingScope.Machine },
        new(SpeedKey, "ai.speed", SettingType.Choice, "fast", ["fast", "smarter"]) { Scope = SettingScope.Machine },
        new(ModelKey, "ai.model", SettingType.String, string.Empty) { Scope = SettingScope.Machine, MaxLength = AiModels.MaxIdLength },
        new(ServerKey, "ai.server", SettingType.Url, AiProviders.DefaultServer.AbsoluteUri) { Scope = SettingScope.Machine, MaxLength = 512 },
        .. AiProviders.Ids.Select(id => new SettingDefinition(KeyPrefix + id, "ai.key", SettingType.Secret, null)
        {
            Scope = SettingScope.Machine,
            MaxLength = AiSetup.MaxKeyLength,
        }),
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [EnabledKey];

    public bool IsEnabled(SettingValues settings, AppMode mode) => mode != AppMode.Safe && settings.GetBool(EnabledKey);

    public IAppFeature Create(FeatureContext context) => new AiFeature(
        context.Commands,
        context.Settings,
        context.Secrets,
        context.Text,
        new AiTransport(AppInfo.Version, context.Network),
        Log.Warn);
}

/// <summary>What the command bar uses to ask. Null in <see cref="AskAi.Current"/> while Ask AI is off.</summary>
internal interface IAskAi
{
    /// <summary>What is missing before a question can go, read fresh from the settings.</summary>
    AiSetupProblem Problem { get; }

    /// <summary>The chosen service, read fresh from the settings.</summary>
    AiProvider Provider { get; }

    /// <summary>The service's name, such as "Claude".</summary>
    string ServiceName { get; }

    /// <summary>Asks. Pieces of the answer go to <paramref name="onDelta"/> on a worker thread. Returns Done or Failed.</summary>
    Task<AiEvent> AskAsync(IReadOnlyList<AiTurn> turns, Action<AiEvent> onDelta, CancellationToken cancellationToken);

    /// <summary>The service, key, model or server changed. A question in flight stops. Raised on the UI thread.</summary>
    event EventHandler? SetupChanged;
}

/// <summary>The running Ask AI, for the command bar. Set and cleared on the UI thread by <see cref="AiFeature"/>.</summary>
internal static class AskAi
{
    public static IAskAi? Current { get; set; }
}

/// <summary>
/// Owns the transport while Ask AI is on. It follows the Privacy offline switch, registers <c>ai.ask</c>, and
/// logs failed requests by provider, status and kind of error, never the question, the answer or the key.
/// Holds no timer.
/// </summary>
internal sealed class AiFeature(
    ICommandRegistry commands,
    IAppSettings settings,
    ISecretStore secrets,
    ILocalizer text,
    AiTransport transport,
    Action<string, Exception?> log) : IAppFeature, IAskAi
{
    private IDisposable? _registration;

    public event EventHandler? SetupChanged;

    public AiSetupProblem Problem
    {
        get
        {
            AiSetup.Account(settings.Values, secrets, out var problem);
            return problem;
        }
    }

    public AiProvider Provider => AiSetup.Provider(settings.Values);

    public string ServiceName => text[AiText.ServiceKey(Provider)];

    public Task StartAsync()
    {
        transport.OfflineMode = settings.Values.GetBool(NetworkPrivacyDescriptor.OfflineKey);
        settings.Changed += OnSettingsChanged;
        transport.Attempted += OnAttempted;
        _registration = commands.Register(new AtapCommand(AiDescriptor.AskCommand, "ai.ask", _ => commands.TryRunAsync(CommandBar.CommandBarViewModel.OpenCommandId, AiDescriptor.AskArgument))
        {
            ShowInCommandBar = false,
        });
        AskAi.Current = this;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (ReferenceEquals(AskAi.Current, this))
            AskAi.Current = null;
        settings.Changed -= OnSettingsChanged;
        transport.Attempted -= OnAttempted;
        _registration?.Dispose();
        _registration = null;
        // Stops a question in flight.
        transport.Dispose();
        return ValueTask.CompletedTask;
    }

    public async Task<AiEvent> AskAsync(IReadOnlyList<AiTurn> turns, Action<AiEvent> onDelta, CancellationToken cancellationToken)
    {
        if (AiSetup.Account(settings.Values, secrets, out _) is not { } account)
            return AiEvent.Failed(AiError.KeyNotAccepted);
        return await transport.AskAsync(account, turns, onDelta, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The log line for a failed request: provider, status and kind of error only.</summary>
    internal static string? Describe(AiAttempt attempt)
    {
        if (attempt.Error is AiError.None or AiError.Stopped or AiError.Offline)
            return null;
        var status = attempt.StatusCode is { } code ? $", HTTP {code}" : string.Empty;
        return $"Ask AI to {AiProviders.IdOf(attempt.Provider)} did not work: {attempt.Error}{status}.";
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(NetworkPrivacyDescriptor.OfflineKey))
            transport.OfflineMode = settings.Values.GetBool(NetworkPrivacyDescriptor.OfflineKey);
        if (e.Keys.Any(k => k.StartsWith("ai.", StringComparison.Ordinal) && k != AiDescriptor.EnabledKey))
            SetupChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnAttempted(object? sender, AiAttempt attempt)
    {
        if (Describe(attempt) is { } line)
            log(line, null);
    }
}

/// <summary>The plain words for services and errors, shared by the card and the command bar.</summary>
internal static class AiText
{
    public static string ServiceKey(AiProvider provider) => "ai.provider." + AiProviders.IdOf(provider);

    /// <summary>What went wrong, in plain words. Your own server has its own words for "cannot reach".</summary>
    public static string ErrorKey(AiError error, AiProvider provider) => error switch
    {
        AiError.KeyNotAccepted => "ai.error.key",
        AiError.OutOfCredit => "ai.error.credit",
        AiError.LimitReached => "ai.error.limit",
        AiError.ModelNotAvailable => "ai.error.model",
        AiError.ServiceBusy => "ai.error.busy",
        AiError.NoAnswerInTime => "ai.error.time",
        AiError.Offline => "ai.error.offline",
        AiError.CannotReachServer => provider == AiProvider.Custom ? "ai.error.server" : "ai.error.reach",
        AiError.NotAllowed => "ai.error.notAllowed",
        AiError.Stopped => "ai.error.stopped",
        _ => "ai.error.unexpected",
    };

    public static string Error(ILocalizer text, AiError error, AiProvider provider) =>
        text.Format(ErrorKey(error, provider), ("service", text[ServiceKey(provider)]));
}
