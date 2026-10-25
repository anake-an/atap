using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Core.Layout;
using Atap.Core.Localization;
using Atap.Core.Net;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Settings.Cards.Widgets;
using Atap.Settings.Form;

[assembly: AtapSettingsCard(typeof(WidgetsCard))]

namespace Atap.Settings.Cards.Widgets;

/// <summary>Everything on the notch, how each one is doing, Restart after a failure, and each one's settings.</summary>
internal sealed class WidgetsCard : ISettingsCard
{
    public string Id => "widgets";

    public string SectionId => SettingsSections.Widgets;

    public int Order => 10;

    public string TitleKey => "widgetlist.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/Widgets/WidgetsViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context)
    {
        var services = context.Services;
        var picker = new DialogPathPicker(() => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive));
        return new WidgetsCardViewModel(services.Widgets, services.Layout, services.Text, services.Secrets, picker, services.Http.For);
    }
}

/// <summary>
/// One row per enabled instance, in layout order. Rows are kept across changes, so a settings form that is
/// open stays open while its widget restarts.
/// </summary>
internal sealed class WidgetsCardViewModel : ObservableObject, IDisposable
{
    private readonly IWidgetHostView _host;
    private readonly ILayoutService _layout;
    private readonly ILocalizer _text;
    private readonly ISecretStore _secrets;
    private readonly IPathPicker? _picker;
    private readonly Func<WidgetCapabilities, IHttpDataService>? _http;

    public WidgetsCardViewModel(
        IWidgetHostView host, ILayoutService layout, ILocalizer text, ISecretStore secrets, IPathPicker? picker, Func<WidgetCapabilities, IHttpDataService>? http = null)
    {
        _host = host;
        _layout = layout;
        _text = text;
        _secrets = secrets;
        _picker = picker;
        _http = http;
        Sync();
        _host.WidgetsChanged += OnWidgetsChanged;
        _host.StatusChanged += OnStatusChanged;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<WidgetRowViewModel> Rows { get; } = [];

    public bool IsEmpty => Rows.Count == 0;

    public string EmptyText => _text["widgetlist.empty"];

    public void Dispose()
    {
        _host.WidgetsChanged -= OnWidgetsChanged;
        _host.StatusChanged -= OnStatusChanged;
        _text.LanguageChanged -= OnLanguageChanged;
        foreach (var row in Rows)
            row.Dispose();
        Rows.Clear();
    }

    /// <summary>Adds rows for new instances, drops rows for removed ones, and keeps the rest in layout order.</summary>
    private void Sync()
    {
        var rows = new List<WidgetRowViewModel>();
        foreach (var id in _host.InstanceIds)
        {
            var row = Rows.FirstOrDefault(r => r.InstanceId == id);
            if (row is not null)
                row.RefreshStatus();
            else if (_host.Catalog.TryResolve(id, out var descriptor))
                row = new WidgetRowViewModel(id, descriptor, _host, _layout, _text, _secrets, _picker, _http);
            if (row is not null)
                rows.Add(row);
        }
        foreach (var gone in Rows.Except(rows).ToList())
            gone.Dispose();
        if (!Rows.SequenceEqual(rows))
        {
            Rows.Clear();
            foreach (var row in rows)
                Rows.Add(row);
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnWidgetsChanged(object? sender, EventArgs e) => Sync();

    private void OnStatusChanged(object? sender, string instanceId) =>
        Rows.FirstOrDefault(r => r.InstanceId == instanceId)?.RefreshStatus();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(EmptyText));
        foreach (var row in Rows)
            row.OnLanguageChanged();
    }
}

/// <summary>One instance: its name, its status, Restart when it failed, and its settings form, built when first opened.</summary>
internal sealed class WidgetRowViewModel : ObservableObject, IDisposable
{
    private readonly IWidgetDescriptor _descriptor;
    private readonly IWidgetHostView _host;
    private readonly ILayoutService _layout;
    private readonly ILocalizer _text;
    private readonly ISecretStore _secrets;
    private readonly IPathPicker? _picker;
    private readonly Func<WidgetCapabilities, IHttpDataService>? _http;
    private SettingsFormViewModel? _form;
    private bool _isExpanded;

    public WidgetRowViewModel(
        string instanceId,
        IWidgetDescriptor descriptor,
        IWidgetHostView host,
        ILayoutService layout,
        ILocalizer text,
        ISecretStore secrets,
        IPathPicker? picker,
        Func<WidgetCapabilities, IHttpDataService>? http = null)
    {
        InstanceId = instanceId;
        _descriptor = descriptor;
        _host = host;
        _layout = layout;
        _text = text;
        _secrets = secrets;
        _picker = picker;
        _http = http;
        RestartCommand = new AsyncRelayCommand(() => _host.RestartAsync(InstanceId), () => CanRestart);
    }

    public string InstanceId { get; }

    /// <summary>"Clock", or "JSON URL (nas)" for a second instance.</summary>
    public string Name
    {
        get
        {
            var name = _text[_descriptor.NameKey];
            return WidgetInstanceId.TryParse(InstanceId, out var id) && id.Name is { } instance
                ? _text.Format("widgetlist.named", ("name", name), ("instance", instance))
                : name;
        }
    }

    public WidgetStatus Status => _host.StatusOf(InstanceId);

    public string StatusText => _text[Status switch
    {
        WidgetStatus.Running => "widgetlist.status.running",
        WidgetStatus.NeedsSetup => "widgetlist.status.needsSetup",
        WidgetStatus.NeedsApproval => "widgetlist.status.needsApproval",
        WidgetStatus.Offline => "widgetlist.status.offline",
        WidgetStatus.Failed => "widgetlist.status.failed",
        _ => "widgetlist.status.stopped",
    }];

    /// <summary>The instance's own words about its status, if it gave any.</summary>
    public string? StatusDetail => _host.StatusMessageKeyOf(InstanceId) is { } key ? _text[key] : null;

    public bool HasStatusDetail => _host.StatusMessageKeyOf(InstanceId) is not null;

    public bool CanRestart => Status == WidgetStatus.Failed;

    public IAsyncRelayCommand RestartCommand { get; }

    public string RestartName => _text.Format("widgetlist.restartName", ("name", Name));

    public bool HasSettings => _descriptor.Settings.Count > 0;

    public string SettingsHeader => _text.Format("widgetlist.settings", ("name", Name));

    /// <summary>Opening the settings builds the form. It is kept until the window closes.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value) || !value || _form is not null)
                return;
            var target = new WidgetSettingsTarget(_layout, InstanceId, _descriptor.Settings);
            var http = (_descriptor.Capabilities.Permissions & WidgetPermissions.Network) != 0 ? _http?.Invoke(_descriptor.Capabilities) : null;
            _form = new SettingsFormViewModel(_descriptor.Settings, new SettingsFormContext(target, _text, _secrets, _picker, http));
            OnPropertyChanged(nameof(Form));
        }
    }

    public SettingsFormViewModel? Form => _form;

    internal void RefreshStatus()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(HasStatusDetail));
        OnPropertyChanged(nameof(CanRestart));
        RestartCommand.NotifyCanExecuteChanged();
    }

    internal void OnLanguageChanged()
    {
        OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        _form?.Dispose();
        _form = null;
    }
}
