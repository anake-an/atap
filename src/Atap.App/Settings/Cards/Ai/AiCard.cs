using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Core.Ai;
using Atap.Core.Localization;
using Atap.Core.Settings;
using Atap.Core.Storage;
using Atap.Features.Ai;
using Atap.Features.NetworkPrivacy;
using Atap.Settings.Cards.Ai;
using Atap.Settings.Shell;

[assembly: AtapSettingsCard(typeof(AiCard))]

namespace Atap.Settings.Cards.Ai;

/// <summary>Ask AI on the Home page: turn on, pick a service, add a key, pick a speed. Your own server and model names under Advanced.</summary>
internal sealed class AiCard : ISettingsCard
{
    public string Id => "ai";

    public string SectionId => SettingsSections.Behavior;

    public int Order => 40;

    public string TitleKey => "ai.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/Ai/AiViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context)
    {
        var services = context.Services;
        // Its own transport, so a key can be checked before Ask AI is on. It goes when the window closes.
        var transport = new AiTransport(AppInfo.Version, services.Network)
        {
            OfflineMode = services.Settings.Values.GetBool(NetworkPrivacyDescriptor.OfflineKey),
        };
        return new AiCardViewModel(services.Settings, services.Secrets, (account, token) => transport.CheckAsync(account, token), services.Text,
            url => ShellOpen.Web(url), transport);
    }
}

/// <summary>A service in the list.</summary>
internal sealed record AiServiceChoice(AiProvider Provider, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Fast or Smarter.</summary>
internal sealed record AiSpeedChoice(AiSpeed Speed, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// The Ask AI card. A key is trimmed, warned about when it looks like another service's, saved with Windows data
/// protection, then checked with a request that costs nothing. Every value is a Machine setting.
/// </summary>
internal sealed class AiCardViewModel : ObservableObject, IDisposable
{
    private static readonly AiProvider[] ServiceOrder = [AiProvider.Anthropic, AiProvider.OpenAi, AiProvider.Gemini, AiProvider.OpenRouter];

    private readonly IAppSettings _settings;
    private readonly ISecretStore _secrets;
    private readonly Func<AiAccount, CancellationToken, Task<AiError>> _check;
    private readonly ILocalizer _text;
    private readonly Action<Uri> _openPage;
    private readonly IDisposable? _owned;
    private AiProvider _lastService = AiProvider.Anthropic;
    private CancellationTokenSource? _checking;
    private Func<string>? _message;
    private Func<string>? _warning;
    private string _server;
    private string _model;
    private string? _serverError;
    private string? _modelError;
    private bool _disposed;

    /// <param name="settings">Where the ai.* settings live.</param>
    /// <param name="secrets">Where keys are locked.</param>
    /// <param name="check">Checks a key. The real one lists models, which costs nothing.</param>
    /// <param name="text">UI text.</param>
    /// <param name="openPage">Opens a service's key page in the browser.</param>
    /// <param name="owned">Disposed with the card, such as its transport.</param>
    public AiCardViewModel(
        IAppSettings settings,
        ISecretStore secrets,
        Func<AiAccount, CancellationToken, Task<AiError>> check,
        ILocalizer text,
        Action<Uri> openPage,
        IDisposable? owned = null)
    {
        _settings = settings;
        _secrets = secrets;
        _check = check;
        _text = text;
        _openPage = openPage;
        _owned = owned;
        if (Provider != AiProvider.Custom)
            _lastService = Provider;
        _server = settings.Values.GetString(AiDescriptor.ServerKey);
        _model = settings.Values.GetString(AiDescriptor.ModelKey);
        Services = BuildServices();
        Speeds = BuildSpeeds();
        GetKeyCommand = new RelayCommand(() =>
        {
            if (AiProviders.KeyPage(Provider) is { } page)
                _openPage(page);
        }, () => HasKeyPage);
        SaveKeyCommand = new AsyncRelayCommand<object?>(async parameter =>
        {
            if (parameter is not PasswordBox box)
                return;
            var pasted = box.Password;
            box.Clear();
            await SaveKeyAsync(pasted);
        });
        RemoveKeyCommand = new RelayCommand(RemoveKey, () => HasKey || HasKeyFromOtherPc);
        SaveServerCommand = new RelayCommand(SaveServer);
        _settings.Changed += OnSettingsChanged;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IRelayCommand GetKeyCommand { get; }

    /// <summary>Takes the password box, reads it once and clears it.</summary>
    public IAsyncRelayCommand<object?> SaveKeyCommand { get; }

    public IRelayCommand RemoveKeyCommand { get; }

    public IRelayCommand SaveServerCommand { get; }

    public bool IsOn
    {
        get => _settings.Values.GetBool(AiDescriptor.EnabledKey);
        set
        {
            if (value == IsOn)
                return;
            _settings.Set(AiDescriptor.EnabledKey, value);
            OnPropertyChanged();
        }
    }

    public AiProvider Provider => AiSetup.Provider(_settings.Values);

    public IReadOnlyList<AiServiceChoice> Services { get; private set; }

    /// <summary>The chosen service. While your own server is on, the one to go back to.</summary>
    public AiServiceChoice? SelectedService
    {
        get => Services.FirstOrDefault(s => s.Provider == (UsesOwnServer ? _lastService : Provider));
        set
        {
            if (value is null || (!UsesOwnServer && value.Provider == Provider))
                return;
            _lastService = value.Provider;
            SetProvider(value.Provider);
        }
    }

    public bool UsesOwnServer
    {
        get => Provider == AiProvider.Custom;
        set
        {
            if (value == UsesOwnServer)
                return;
            SetProvider(value ? AiProvider.Custom : _lastService);
        }
    }

    public bool CanPickService => !UsesOwnServer;

    public string ServiceName => _text[AiText.ServiceKey(Provider)];

    public bool HasKeyPage => AiProviders.KeyPage(Provider) is not null;

    public string BillingText => _text.Format("ai.billing", ("service", ServiceName));

    public string KeyHint => UsesOwnServer ? _text["ai.keyOptional"] : _text.Format("ai.keyHint", ("service", ServiceName));

    public bool HasKey => AiSetup.HasKey(_settings.Values, _secrets, Provider);

    public bool HasKeyFromOtherPc => AiSetup.HasKeyFromOtherPc(_settings.Values, _secrets, Provider);

    public string KeyState => _text[
        !_secrets.IsAvailable ? "ai.keyState.unavailable"
        : HasKey ? "ai.keyState.saved"
        : HasKeyFromOtherPc ? "ai.keyState.otherPc"
        : "ai.keyState.none"];

    /// <summary>What the last save, check or remove did.</summary>
    public string? Message => _message?.Invoke();

    public bool HasMessage => _message is not null;

    /// <summary>The key looks like another service's.</summary>
    public string? Warning => _warning?.Invoke();

    public bool HasWarning => _warning is not null;

    public bool IsChecking => _checking is not null;

    public IReadOnlyList<AiSpeedChoice> Speeds { get; private set; }

    public AiSpeedChoice SelectedSpeed
    {
        get => Speeds.First(s => s.Speed == AiSetup.Speed(_settings.Values));
        set
        {
            if (value is null || value.Speed == AiSetup.Speed(_settings.Values))
                return;
            _settings.Set(AiDescriptor.SpeedKey, value.Speed == AiSpeed.Smarter ? "smarter" : "fast");
        }
    }

    /// <summary>Your own server's address as typed. Saved with <see cref="SaveServerCommand"/>.</summary>
    public string Server
    {
        get => _server;
        set
        {
            if (SetProperty(ref _server, value ?? string.Empty))
                OnPropertyChanged(nameof(ServerWarning));
        }
    }

    public string? ServerError => _serverError is null ? null : _text[_serverError];

    public bool HasServerError => _serverError is not null;

    /// <summary>A plain warning while the address uses plain http.</summary>
    public string? ServerWarning =>
        Uri.TryCreate(_server.Trim(), UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttp ? _text["ai.serverPlain"] : null;

    public bool HasServerWarning => ServerWarning is not null;

    /// <summary>The model typed under Advanced. Empty uses Fast or Smarter. Saved when the box loses focus.</summary>
    public string Model
    {
        get => _model;
        set
        {
            var typed = (value ?? string.Empty).Trim();
            if (typed.Length > 0 && !AiModels.IsValidId(typed, Provider))
            {
                _modelError = "ai.modelBad";
                OnPropertyChanged(nameof(ModelError));
                OnPropertyChanged(nameof(HasModelError));
                return;
            }
            _modelError = null;
            _model = typed;
            _settings.Set(AiDescriptor.ModelKey, typed.Length == 0 ? null : typed);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ModelError));
            OnPropertyChanged(nameof(HasModelError));
        }
    }

    public string? ModelError => _modelError is null ? null : _text[_modelError];

    public bool HasModelError => _modelError is not null;

    public string ModelHint => _text.Format("ai.modelHint", ("example", AiModels.Default(Provider, AiSpeed.Smarter)));

    public string PrivacyText => _text.Format("ai.privacy", ("service", ServiceName));

    public string CheckFreeText => _text["ai.check.free"];

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        _text.LanguageChanged -= OnLanguageChanged;
        _checking?.Cancel();
        _owned?.Dispose();
    }

    /// <summary>Trims and saves the key, warns when it looks like another service's, then checks it.</summary>
    public async Task SaveKeyAsync(string pasted)
    {
        var provider = Provider;
        SetWarning(null);
        var result = AiSetup.SaveKey(_settings, _secrets, provider, pasted);
        if (result != AiKeySave.Saved)
        {
            var key = result switch
            {
                AiKeySave.Empty => "ai.check.empty",
                AiKeySave.NotAKey => "ai.check.notAKey",
                AiKeySave.Unavailable => "ai.keyState.unavailable",
                _ => "ai.check.notSaved",
            };
            SetMessage(() => _text.Format(key, ("service", _text[AiText.ServiceKey(provider)])));
            RefreshKey();
            return;
        }

        if (provider != AiProvider.Custom && AiProviders.GuessFromKey(AiSetup.Clean(pasted, out _)) is { } other && other != provider)
            SetWarning(() => _text.Format("ai.check.otherService", ("other", _text[AiText.ServiceKey(other)])));
        RefreshKey();
        await CheckAsync();
    }

    /// <summary>Checks the saved key for the chosen service. Saving a key does this too.</summary>
    public async Task CheckAsync()
    {
        _checking?.Cancel();
        if (AiSetup.Account(_settings.Values, _secrets, out _) is not { } account)
            return;
        using var checking = new CancellationTokenSource();
        _checking = checking;
        OnPropertyChanged(nameof(IsChecking));
        var provider = account.Provider;
        SetMessage(() => _text.Format("ai.check.checking", ("service", _text[AiText.ServiceKey(provider)])));
        AiError error;
        try
        {
            error = await _check(account, checking.Token);
        }
        catch (OperationCanceledException)
        {
            error = AiError.Stopped;
        }
        if (!ReferenceEquals(_checking, checking))
            return;
        _checking = null;
        OnPropertyChanged(nameof(IsChecking));
        if (error == AiError.Stopped)
        {
            SetMessage(null);
            return;
        }
        SetMessage(error == AiError.None ? () => _text["ai.check.ok"] : () => AiText.Error(_text, error, provider));
    }

    private void RemoveKey()
    {
        _checking?.Cancel();
        SetWarning(null);
        var removed = AiSetup.RemoveKey(_settings, _secrets, Provider);
        SetMessage(removed ? () => _text["ai.check.removed"] : () => _text["ai.check.notSaved"]);
        RefreshKey();
    }

    private void SaveServer()
    {
        var typed = _server.Trim();
        if (!Uri.TryCreate(typed, UriKind.Absolute, out var url) || !AiTransport.IsValidServer(url))
        {
            _serverError = "ai.serverBad";
        }
        else
        {
            _serverError = null;
            _settings.Set(AiDescriptor.ServerKey, url.AbsoluteUri);
            SetMessage(() => _text["ai.serverSaved"]);
        }
        OnPropertyChanged(nameof(ServerError));
        OnPropertyChanged(nameof(HasServerError));
    }

    /// <summary>A new service starts with Fast or Smarter, since a model name typed for another service would not work.</summary>
    private void SetProvider(AiProvider provider)
    {
        _checking?.Cancel();
        _settings.Set(AiDescriptor.ProviderKey, AiProviders.IdOf(provider));
        _settings.Set(AiDescriptor.ModelKey, null);
        _model = string.Empty;
        _modelError = null;
        SetMessage(null);
        SetWarning(null);
        RefreshAll();
    }

    private void SetMessage(Func<string>? message)
    {
        _message = message;
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasMessage));
    }

    private void SetWarning(Func<string>? warning)
    {
        _warning = warning;
        OnPropertyChanged(nameof(Warning));
        OnPropertyChanged(nameof(HasWarning));
    }

    private void RefreshKey()
    {
        OnPropertyChanged(nameof(HasKey));
        OnPropertyChanged(nameof(HasKeyFromOtherPc));
        OnPropertyChanged(nameof(KeyState));
        RemoveKeyCommand.NotifyCanExecuteChanged();
    }

    private void RefreshAll()
    {
        // Every property, so text and choices follow the settings and the language.
        OnPropertyChanged(string.Empty);
        GetKeyCommand.NotifyCanExecuteChanged();
        RemoveKeyCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<AiServiceChoice> BuildServices() =>
        ServiceOrder.Select(p => new AiServiceChoice(p, _text[AiText.ServiceKey(p)])).ToList();

    private IReadOnlyList<AiSpeedChoice> BuildSpeeds() =>
        [new(AiSpeed.Fast, _text["ai.speed.fast"]), new(AiSpeed.Smarter, _text["ai.speed.smarter"])];

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!e.Keys.Any(k => k.StartsWith("ai.", StringComparison.Ordinal)))
            return;
        if (Provider != AiProvider.Custom)
            _lastService = Provider;
        if (e.Keys.Contains(AiDescriptor.ServerKey))
            _server = _settings.Values.GetString(AiDescriptor.ServerKey);
        if (e.Keys.Contains(AiDescriptor.ModelKey))
            _model = _settings.Values.GetString(AiDescriptor.ModelKey);
        RefreshAll();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Services = BuildServices();
        Speeds = BuildSpeeds();
        RefreshAll();
    }
}
