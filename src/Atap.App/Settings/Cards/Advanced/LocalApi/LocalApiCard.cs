using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Core.Localization;
using Atap.Features.LocalApi;
using Atap.Settings.Cards.Advanced.LocalApi;

[assembly: AtapSettingsCard(typeof(LocalApiCard))]

namespace Atap.Settings.Cards.Advanced.LocalApi;

/// <summary>Whether the local API listens, and an example to try in a terminal. On the Advanced page, under its settings.</summary>
internal sealed class LocalApiCard : ISettingsCard
{
    public string Id => "localApi";

    public string SectionId => SettingsSections.Advanced;

    public int Order => 20;

    public string TitleKey => "localapi.card.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/Advanced/LocalApi/LocalApiViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context) => new LocalApiCardViewModel(context.Services.Text, CopyToClipboard);

    private static bool CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

internal sealed class LocalApiCardViewModel : ObservableObject, IDisposable
{
    /// <summary>One command a reader can paste as it is.</summary>
    public const string Example = "atap push --id build --title \"Building\" --progress 40";

    private readonly ILocalizer _text;
    private readonly Func<string, bool> _copy;
    private string? _copyStatus;

    public LocalApiCardViewModel(ILocalizer text, Func<string, bool> copy)
    {
        _text = text;
        _copy = copy;
        CopyCommand = new RelayCommand(() => CopyStatus = _copy(Example) ? _text["localapi.card.copied"] : null);
        LocalApiStatus.Changed += OnStatusChanged;
        _text.LanguageChanged += OnStatusChanged;
    }

    public IRelayCommand CopyCommand { get; }

    public string ExampleText => Example;

    public bool IsRunning => LocalApiStatus.State == LocalApiState.Running;

    public string StatusText => _text[LocalApiStatus.State switch
    {
        LocalApiState.Running => "localapi.card.running",
        LocalApiState.CouldNotStart => "localapi.card.couldNotStart",
        _ => "localapi.card.off",
    }];

    public string? CopyStatus
    {
        get => _copyStatus;
        private set => SetProperty(ref _copyStatus, value);
    }

    public void Dispose()
    {
        LocalApiStatus.Changed -= OnStatusChanged;
        _text.LanguageChanged -= OnStatusChanged;
    }

    private void OnStatusChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsRunning));
    }
}
