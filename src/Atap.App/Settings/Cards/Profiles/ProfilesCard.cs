using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Layout;
using Atap.Core.Localization;
using Atap.Core.Rules;
using Atap.Core.Settings;
using Atap.Features.Profiles;
using Atap.Notch;
using Atap.Settings.Cards.Profiles;

[assembly: AtapSettingsCard(typeof(ProfilesCard))]

namespace Atap.Settings.Cards.Profiles;

/// <summary>Make, edit and remove profiles, and pick the one used when no rule picks another.</summary>
internal sealed class ProfilesCard : ISettingsCard
{
    public string Id => "profiles";

    public string SectionId => SettingsSections.Profiles;

    public int Order => 10;

    public string TitleKey => "profilescard.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/Profiles/ProfilesViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context) =>
        new ProfilesCardViewModel(context.Services.Layout, context.Services.Settings, context.Services.Commands, context.Services.Text);
}

/// <summary>A profile in the list, or in the "usual profile" choice. Default has the id "default".</summary>
public sealed record ProfileChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

internal sealed class ProfilesCardViewModel : ObservableObject, IDisposable
{
    private readonly ILayoutService _layout;
    private readonly IAppSettings _settings;
    private readonly ICommandRegistry _commands;
    private readonly ILocalizer _text;
    private string _newName = string.Empty;
    private ProfileChoice? _usual;
    private bool _refreshing;

    public ProfilesCardViewModel(ILayoutService layout, IAppSettings settings, ICommandRegistry commands, ILocalizer text)
    {
        _layout = layout;
        _settings = settings;
        _commands = commands;
        _text = text;
        AddCommand = new RelayCommand(Add, () => NewName.Trim().Length > 0);
        RemoveCommand = new RelayCommand<string>(Remove);
        EditCommand = new AsyncRelayCommand<string>(EditAsync);
        _layout.Changed += OnChanged;
        _text.LanguageChanged += OnChanged;
        Refresh();
    }

    public ObservableCollection<ProfileChoice> Profiles { get; } = [];

    /// <summary>Default first, then every profile.</summary>
    public ObservableCollection<ProfileChoice> Choices { get; } = [];

    public bool HasProfiles => Profiles.Count > 0;

    public IRelayCommand AddCommand { get; }

    public IRelayCommand<string> RemoveCommand { get; }

    public IAsyncRelayCommand<string> EditCommand { get; }

    public string NewName
    {
        get => _newName;
        set
        {
            if (SetProperty(ref _newName, value ?? string.Empty))
                AddCommand.NotifyCanExecuteChanged();
        }
    }

    public ProfileChoice? Usual
    {
        get => _usual;
        set
        {
            if (!SetProperty(ref _usual, value) || _refreshing || value is null)
                return;
            _settings.Set(ProfilesDescriptor.ManualKey, value.Id == RuleResolver.DefaultProfileId ? string.Empty : value.Id);
        }
    }

    public void Dispose()
    {
        _layout.Changed -= OnChanged;
        _text.LanguageChanged -= OnChanged;
    }

    /// <summary>"Work" becomes "work", "Work 2" if taken. A new profile copies nothing, so it shows what Default shows until edited.</summary>
    internal static string NewId(string name, IEnumerable<string> taken)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
                builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }
        var stem = builder.ToString().Trim('-');
        if (stem.Length == 0 || stem == RuleResolver.DefaultProfileId)
            stem = "profile";
        var used = taken.ToHashSet(StringComparer.Ordinal);
        var id = stem;
        for (var n = 2; used.Contains(id); n++)
            id = $"{stem}-{n}";
        return id;
    }

    private void Add()
    {
        var name = NewName.Trim();
        if (name.Length == 0)
            return;
        _layout.Update(document =>
            document.Profiles.Add(new LayoutProfile { Id = NewId(name, document.Profiles.Select(p => p.Id)), Name = name }));
        NewName = string.Empty;
    }

    private void Remove(string? id)
    {
        if (id is null)
            return;
        _layout.Update(document => document.Profiles.RemoveAll(p => p.Id == id));
        if (_settings.Values.GetString(ProfilesDescriptor.ManualKey) == id)
            _settings.Set(ProfilesDescriptor.ManualKey, string.Empty);
        if (_layout.ActiveProfileId == id)
            _layout.SetActiveProfile(RuleResolver.DefaultProfileId);
    }

    // The editor changes the active profile, so this one is made active first. When the editor closes, the
    // rules pick the profile again.
    private async Task EditAsync(string? id)
    {
        if (id is null)
            return;
        _layout.SetActiveProfile(id);
        await _commands.TryRunAsync(NotchMenuViewModel.EditLayoutCommandId);
    }

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        _refreshing = true;
        try
        {
            var defaultChoice = new ProfileChoice(RuleResolver.DefaultProfileId, _text["profilescard.default"]);
            var profiles = _layout.Document.Profiles.Where(p => p.Id.Length > 0)
                .Select(p => new ProfileChoice(p.Id, p.Name.Length > 0 ? p.Name : p.Id)).ToList();
            Profiles.Clear();
            Choices.Clear();
            Choices.Add(defaultChoice);
            foreach (var profile in profiles)
            {
                Profiles.Add(profile);
                Choices.Add(profile);
            }
            var manual = _settings.Values.GetString(ProfilesDescriptor.ManualKey);
            Usual = Choices.FirstOrDefault(c => c.Id == manual) ?? defaultChoice;
            OnPropertyChanged(nameof(HasProfiles));
        }
        finally
        {
            _refreshing = false;
        }
    }
}
