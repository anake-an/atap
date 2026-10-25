using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core;
using Atap.Core.Custom;
using Atap.Core.Localization;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Features.NetworkPrivacy;
using Atap.Notch.Automation;
using Atap.Platform.Mqtt;
using Atap.Ui.Controls;
using Atap.Widgets.HomeAssistant;

[assembly: AtapWidget(typeof(HomeAssistantDescriptor))]

namespace Atap.Widgets.HomeAssistant;

/// <summary>One value from Home Assistant, or anything else that publishes over MQTT. Constants only.</summary>
public sealed class HomeAssistantDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "home-assistant";
    public const string BrokerKey = "broker";
    public const string TopicKey = "topic";
    public const string LabelKey = "label";
    public const string ShowKey = "show";
    public const string UnitKey = "unit";
    public const string FullAtKey = "fullAt";
    public const string AlertKey = "alert";
    public const string AlertAtKey = "alertAt";
    public const string PortKey = "port";
    public const string SecureKey = "secure";
    public const string UserKey = "user";
    public const string PasswordKey = "password";
    public const string FieldKey = "field";

    public const string ShowNumber = "number";
    public const string ShowText = "text";
    public const string ShowBar = "bar";
    public const string ShowStatus = "status";

    public string TypeId => TypeIdValue;

    public string NameKey => "home-assistant.name";

    public string DescriptionKey => "home-assistant.description";

    public WidgetGroup Group => WidgetGroup.Custom;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Compact, NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(BrokerKey, "home-assistant.broker", SettingType.String, "") { DescriptionKey = "home-assistant.brokerHint", Required = true, MaxLength = 253 },
        new(TopicKey, "home-assistant.topic", SettingType.String, "") { DescriptionKey = "home-assistant.topicHint", Required = true, MaxLength = 512 },
        new(LabelKey, "home-assistant.label", SettingType.String, "") { DescriptionKey = "home-assistant.labelHint", MaxLength = 40 },
        new(ShowKey, "home-assistant.show", SettingType.Choice, ShowNumber, [ShowNumber, ShowText, ShowBar, ShowStatus]),
        new(UnitKey, "home-assistant.unit", SettingType.String, "") { DescriptionKey = "home-assistant.unitHint", MaxLength = 12 },
        new(FullAtKey, "home-assistant.fullAt", SettingType.Double, 100d) { DescriptionKey = "home-assistant.fullAtHint", VisibleWhen = (ShowKey, ShowBar) },
        new(AlertKey, "home-assistant.alert", SettingType.Bool, false) { DescriptionKey = "home-assistant.alertHint" },
        new(AlertAtKey, "home-assistant.alertAt", SettingType.Double, 0d) { DescriptionKey = "home-assistant.alertAtHint", VisibleWhen = (AlertKey, true) },
        new(PortKey, "home-assistant.port", SettingType.Int, 0) { DescriptionKey = "home-assistant.portHint", Min = 0, Max = 65535, Step = 1, Advanced = true },
        new(SecureKey, "home-assistant.secure", SettingType.Bool, false) { DescriptionKey = "home-assistant.secureHint", Advanced = true },
        new(UserKey, "home-assistant.user", SettingType.String, "") { MaxLength = 128, Advanced = true },
        new(PasswordKey, "home-assistant.password", SettingType.Secret, null) { DescriptionKey = "home-assistant.passwordHint", Advanced = true },
        new(FieldKey, "home-assistant.field", SettingType.String, "$") { DescriptionKey = "home-assistant.fieldHint", MaxLength = 200, Advanced = true },
    ];

    /// <summary>The broker is any host the user types in, reached over MQTT rather than the HTTP client.</summary>
    public WidgetCapabilities Capabilities { get; } = new(WidgetPermissions.Network | WidgetPermissions.UserUrls, [], "home-assistant.privacy");

    public bool AllowsMultipleInstances => true;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/HomeAssistant/HomeAssistantViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new HomeAssistantWidget(context, () =>
    {
        var lease = context.Shared.Acquire<MqttBrokers>();
        return (lease.Value, lease);
    });
}

/// <summary>
/// Listens to one MQTT topic through the shared <see cref="MqttBrokers"/>, so instances on the same broker share one
/// connection. The value goes through a <see cref="ValueTemplate"/>, and a value past the alert level makes the widget
/// live. It lets go of the topic when it stops, while Atap is offline, and while battery saver is on and the widget
/// is not on screen. It listens again when that ends. The password is a secret and is never shown or logged.
/// </summary>
public sealed class HomeAssistantWidget : WidgetBase
{
    private readonly Func<(MqttBrokers Brokers, IDisposable Lease)> _open;
    private readonly HomeAssistantViewModel _viewModel;
    private MqttBrokers? _brokers;
    private IDisposable? _lease;
    private MqttBrokerKey? _broker;
    private string? _password;
    private MqttMapping? _mapping;
    private ValueTemplate? _template;
    private MqttSubscription? _subscription;
    private ValueView? _view;
    private bool _stale;

    internal HomeAssistantWidget(WidgetContext context, Func<(MqttBrokers Brokers, IDisposable Lease)> open)
        : base(context)
    {
        _open = open;
        _viewModel = new HomeAssistantViewModel(context.Text);
    }

    public override object ViewModel => _viewModel;

    /// <summary>True while the topic is subscribed.</summary>
    internal bool IsListening => _subscription is not null;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _viewModel.SetLabel(settings.GetString(HomeAssistantDescriptor.LabelKey).Trim());

        var host = settings.GetString(HomeAssistantDescriptor.BrokerKey).Trim();
        var topic = settings.GetString(HomeAssistantDescriptor.TopicKey).Trim();
        if (host.Length == 0 || host.Any(char.IsWhiteSpace) || MqttTopic.ValidateFilter(topic) != MqttTopicProblem.None)
        {
            _viewModel.ShowState(HomeAssistantState.NeedsSetup);
            return Task.CompletedTask;
        }
        if (!ValueTemplate.TryCreate(Spec(settings), out var template, out _))
        {
            _viewModel.ShowState(HomeAssistantState.BadField);
            return Task.CompletedTask;
        }

        _template = template;
        _mapping = new MqttMapping(topic);
        _password = settings.GetSecretRef(HomeAssistantDescriptor.PasswordKey) is { } reference ? Context.Secrets.Get(reference) : null;
        _broker = MqttBrokerKey.Create(
            host,
            settings.GetInt(HomeAssistantDescriptor.PortKey),
            settings.GetBool(HomeAssistantDescriptor.SecureKey),
            settings.GetString(HomeAssistantDescriptor.UserKey).Trim());

        (_brokers, _lease) = _open();
        Context.Power.Changed += OnConditionsChanged;
        Context.Visibility.Changed += OnConditionsChanged;
        Context.App.Changed += OnAppSettingsChanged;
        Update();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        if (_lease is not null)
        {
            Context.Power.Changed -= OnConditionsChanged;
            Context.Visibility.Changed -= OnConditionsChanged;
            Context.App.Changed -= OnAppSettingsChanged;
        }
        _subscription?.Dispose();
        _subscription = null;
        _lease?.Dispose();
        _lease = null;
        _brokers = null;
        _broker = null;
        _password = null;
        _view = null;
        _stale = false;
        SetIsLive(false);
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    /// <summary>The template from the settings. The field path is checked by <see cref="ValueTemplate.TryCreate"/>.</summary>
    internal static ValueTemplateSpec Spec(SettingValues settings)
    {
        var kind = settings.GetString(HomeAssistantDescriptor.ShowKey) switch
        {
            HomeAssistantDescriptor.ShowText => ValueTemplateKind.Text,
            HomeAssistantDescriptor.ShowBar => ValueTemplateKind.Progress,
            HomeAssistantDescriptor.ShowStatus => ValueTemplateKind.StatusDot,
            _ => ValueTemplateKind.Number,
        };
        var field = settings.GetString(HomeAssistantDescriptor.FieldKey).Trim();
        var unit = settings.GetString(HomeAssistantDescriptor.UnitKey).Trim();
        return new ValueTemplateSpec(kind, field.Length > 0 ? field : "$")
        {
            Unit = unit.Length > 0 ? unit : null,
            Max = kind == ValueTemplateKind.Progress ? settings.GetDouble(HomeAssistantDescriptor.FullAtKey) : null,
            Thresholds = settings.GetBool(HomeAssistantDescriptor.AlertKey)
                ? new ThresholdSpec(Warning: settings.GetDouble(HomeAssistantDescriptor.AlertAtKey))
                : null,
            NoValueWords = StatusWords.DefaultNoValue,
        };
    }

    private bool IsOnScreen => Context.Visibility.Value is WidgetVisibility.Compact or WidgetVisibility.Expanded or WidgetVisibility.Panel;

    private bool IsOffline => Context.App.Values.GetBool(NetworkPrivacyDescriptor.OfflineKey);

    private void OnConditionsChanged(object? sender, EventArgs e) => Update();

    private void OnAppSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(NetworkPrivacyDescriptor.OfflineKey))
            Update();
    }

    /// <summary>Listens or lets go, from offline mode, battery saver and whether the widget is on screen.</summary>
    private void Update()
    {
        if (_brokers is null || _broker is null || _mapping is null)
            return;

        HomeAssistantState? paused = IsOffline ? HomeAssistantState.PausedOffline
            : Context.Power.SaverActive && !IsOnScreen ? HomeAssistantState.PausedSaver
            : null;

        if (paused is null && _subscription is null)
        {
            _subscription = _brokers.Subscribe(_broker, _password, _mapping.Topic, OnMessage, OnStatus);
        }
        else if (paused is { } reason && _subscription is not null)
        {
            _subscription.Dispose();
            _subscription = null;
            _stale = true;
            _viewModel.ShowState(reason, Shown());
        }
        else if (paused is { } first && _view is null)
        {
            _viewModel.ShowState(first);
        }
    }

    private void OnStatus(MqttStatus status)
    {
        var state = status switch
        {
            { State: MqttState.Connected } => HomeAssistantState.Waiting,
            { State: MqttState.Connecting } => HomeAssistantState.Connecting,
            { Problem: MqttProblem.SignInRefused } => HomeAssistantState.SignInRefused,
            { Problem: MqttProblem.NeedsTls } => HomeAssistantState.NeedsTls,
            _ => HomeAssistantState.Retrying,
        };
        // A value stays on screen through a reconnect, marked out of date until the next message.
        if (status.State == MqttState.Retrying)
            _stale = true;
        _viewModel.ShowState(state, Shown());
    }

    private void OnMessage(string topic, byte[] payload)
    {
        if (_template is null || _mapping is null)
            return;
        var read = _mapping.Read(payload);
        if (read.Status == PayloadStatus.TooLarge)
            return;
        _view = _template.Render(read, _view, CultureInfo.CurrentCulture);
        _stale = false;
        _viewModel.ShowState(HomeAssistantState.Ready, Shown());
        SetIsLive(_view.IsAlerting);
    }

    private ValueView? Shown() => _view is null ? null : _stale ? _view with { Stale = true } : _view;
}

public enum HomeAssistantState
{
    NeedsSetup,
    BadField,
    Connecting,

    /// <summary>Connected, with no message yet.</summary>
    Waiting,
    Retrying,
    SignInRefused,
    NeedsTls,
    PausedSaver,
    PausedOffline,

    /// <summary>A message arrived.</summary>
    Ready,
}

public sealed class HomeAssistantViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private HomeAssistantState _state = HomeAssistantState.Connecting;
    private string _label = string.Empty;

    public HomeAssistantViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public HomeAssistantState State => _state;

    /// <summary>The name the user gave it, or "Home Assistant".</summary>
    public string Title => _label.Length > 0 ? _label : _text["home-assistant.name"];

    /// <summary>The last value, marked stale while it is out of date. Null until the first message.</summary>
    public ValueView? Value { get; private set; }

    public bool HasValue => Value is not null;

    public bool ShowMessage => Value is null;

    public string Message => _state switch
    {
        HomeAssistantState.NeedsSetup => _text["home-assistant.needsSetup"],
        HomeAssistantState.BadField => _text["home-assistant.badField"],
        HomeAssistantState.Waiting => _text["home-assistant.waiting"],
        HomeAssistantState.Retrying => _text["home-assistant.retrying"],
        HomeAssistantState.SignInRefused => _text["home-assistant.signInRefused"],
        HomeAssistantState.NeedsTls => _text["home-assistant.needsTls"],
        HomeAssistantState.PausedSaver => _text["home-assistant.pausedSaver"],
        HomeAssistantState.PausedOffline => _text["home-assistant.pausedOffline"],
        _ => _text["home-assistant.connecting"],
    };

    /// <summary>"21.5 °C" for the compact notch, or empty without a value.</summary>
    public string CompactText => Value is { HasValue: true } view ? view.DisplayText : string.Empty;

    public ValueLevel Level => Value?.Level ?? ValueLevel.Unknown;

    public string AccessibleSummary => _text.Format(
        "home-assistant.accessible",
        ("title", Title),
        ("value", Value is { } view ? ValueSpeech.Describe(view, _text) : Message));

    internal void SetLabel(string label)
    {
        _label = label;
        OnPropertyChanged(string.Empty);
    }

    internal void ShowState(HomeAssistantState state, ValueView? value = null)
    {
        _state = state;
        Value = value;
        OnPropertyChanged(string.Empty);
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
