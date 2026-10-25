using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Core.Layout;
using Atap.Core.Localization;
using Atap.Core.Rules;
using Atap.Core.Settings;
using Atap.Settings.Cards.Behavior.AppRules;
using Atap.Settings.Cards.Profiles;

[assembly: AtapSettingsCard(typeof(AppRulesCard))]

namespace Atap.Settings.Cards.Behavior.AppRules;

/// <summary>"When this app is in front": switch profile, make the notch slim, quiet alerts, or hide it.</summary>
internal sealed class AppRulesCard : ISettingsCard
{
    public string Id => "appRules";

    public string SectionId => SettingsSections.Behavior;

    public int Order => 50;

    public string TitleKey => "apprulescard.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/Behavior/AppRules/AppRulesViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context) =>
        new AppRulesCardViewModel(context.Services.Settings, context.Services.Layout, context.Services.Text);
}

/// <summary>One app rule. Every change saves at once.</summary>
public sealed partial class AppRuleRow : ObservableObject
{
    private readonly Action _save;

    internal AppRuleRow(Rule rule, IReadOnlyList<ProfileChoice> choices, Action save)
    {
        Rule = rule;
        Choices = choices;
        _save = save;
    }

    internal Rule Rule { get; }

    public string Apps => string.Join(", ", (Rule.When.OfType<ForegroundAppCondition>().FirstOrDefault()?.Apps ?? []));

    public IReadOnlyList<ProfileChoice> Choices { get; }

    public ProfileChoice? Profile
    {
        get => Choices.FirstOrDefault(c => c.Id == (Rule.Then.Profile ?? string.Empty)) ?? Choices[0];
        set
        {
            var id = string.IsNullOrEmpty(value?.Id) ? null : value.Id;
            if (Rule.Then.Profile == id)
                return;
            Rule.Then.Profile = id;
            OnPropertyChanged();
            _save();
        }
    }

    public bool Slim
    {
        get => Rule.Then.Slim;
        set => Change(() => Rule.Then.Slim = value, Rule.Then.Slim != value);
    }

    public bool Quiet
    {
        get => Rule.Then.QuietAlerts;
        set => Change(() => Rule.Then.QuietAlerts = value, Rule.Then.QuietAlerts != value);
    }

    public bool Hide
    {
        get => Rule.Then.Hide;
        set => Change(() => Rule.Then.Hide = value, Rule.Then.Hide != value);
    }

    private void Change(Action set, bool changed)
    {
        if (!changed)
            return;
        set();
        OnPropertyChanged(string.Empty);
        _save();
    }
}

internal sealed class AppRulesCardViewModel : ObservableObject, IDisposable
{
    private readonly IAppSettings _settings;
    private readonly ILayoutService _layout;
    private readonly ILocalizer _text;
    private List<Rule> _rules = [];
    private string _newApp = string.Empty;
    private bool _saving;

    public AppRulesCardViewModel(IAppSettings settings, ILayoutService layout, ILocalizer text)
    {
        _settings = settings;
        _layout = layout;
        _text = text;
        AddCommand = new RelayCommand(Add, () => NewApp.Trim().Length > 0);
        RemoveCommand = new RelayCommand<AppRuleRow>(Remove);
        _settings.Changed += OnSettingsChanged;
        _layout.Changed += OnOtherChanged;
        _text.LanguageChanged += OnOtherChanged;
        Load();
    }

    public ObservableCollection<AppRuleRow> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    public IRelayCommand AddCommand { get; }

    public IRelayCommand<AppRuleRow> RemoveCommand { get; }

    public string NewApp
    {
        get => _newApp;
        set
        {
            if (SetProperty(ref _newApp, value ?? string.Empty))
                AddCommand.NotifyCanExecuteChanged();
        }
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _layout.Changed -= OnOtherChanged;
        _text.LanguageChanged -= OnOtherChanged;
    }

    /// <summary>A rule with one app condition and nothing else. Other rules stay as they are, in order.</summary>
    internal static bool IsAppRule(Rule rule) => rule.When.Count == 1 && rule.When[0] is ForegroundAppCondition;

    /// <summary>"Valorant" becomes "Valorant.exe", and a full path keeps only the file name. Matching ignores case.</summary>
    internal static string ExeName(string text)
    {
        var name = System.IO.Path.GetFileName(text.Trim().Trim('"'));
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
    }

    private void Add()
    {
        var app = ExeName(NewApp);
        if (app.Length <= 4)
            return;
        _rules.Add(new Rule
        {
            Id = "app-" + Guid.NewGuid().ToString("N")[..8],
            Name = app,
            When = [new ForegroundAppCondition { Apps = [app] }],
            Then = new RuleActions { Slim = true },
        });
        NewApp = string.Empty;
        Save();
        Load();
    }

    private void Remove(AppRuleRow? row)
    {
        if (row is null)
            return;
        _rules.Remove(row.Rule);
        Save();
        Load();
    }

    private void Save()
    {
        _saving = true;
        try
        {
            using var document = JsonDocument.Parse(RuleJson.Serialize(_rules));
            _settings.SetRules(document.RootElement.Clone());
        }
        finally
        {
            _saving = false;
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!_saving && e.Keys.Contains(SettingsChangedEventArgs.RulesKey))
            Load();
    }

    private void OnOtherChanged(object? sender, EventArgs e) => Load();

    private void Load()
    {
        _rules = RuleJson.Parse(_settings.Rules.ValueKind == JsonValueKind.Array ? _settings.Rules.GetRawText() : null) ?? [];
        List<ProfileChoice> choices = [new(string.Empty, _text["apprulescard.keepProfile"]), new(RuleResolver.DefaultProfileId, _text["profilescard.default"])];
        choices.AddRange(_layout.Document.Profiles.Where(p => p.Id.Length > 0).Select(p => new ProfileChoice(p.Id, p.Name.Length > 0 ? p.Name : p.Id)));
        Rows.Clear();
        foreach (var rule in _rules.Where(IsAppRule))
            Rows.Add(new AppRuleRow(rule, choices, Save));
        OnPropertyChanged(nameof(HasRows));
    }
}
