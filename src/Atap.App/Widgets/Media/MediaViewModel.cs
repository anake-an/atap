using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core.Localization;
using Atap.Notch.Automation;

namespace Atap.Widgets.Media;

public sealed class MediaViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;

    // Null while nothing is playing; empty when the app gives no title.
    private string? _trackTitle;
    private string _artist = string.Empty;
    private ImageSource? _albumArt;
    private bool _isPlaying;
    private bool _hasSession;
    private bool _canSkipPrevious;
    private bool _canSkipNext;

    public MediaViewModel(ILocalizer text, Func<Task> skipPrevious, Func<Task> togglePlayPause, Func<Task> skipNext)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
        PreviousCommand = new AsyncRelayCommand(skipPrevious, () => CanSkipPrevious);
        PlayPauseCommand = new AsyncRelayCommand(togglePlayPause, () => HasSession);
        NextCommand = new AsyncRelayCommand(skipNext, () => CanSkipNext);
    }

    public IAsyncRelayCommand PreviousCommand { get; }

    public IAsyncRelayCommand PlayPauseCommand { get; }

    public IAsyncRelayCommand NextCommand { get; }

    public string Title => _trackTitle switch
    {
        null => _text["media.nothingPlaying"],
        "" => _text["media.unknownTitle"],
        var title => title,
    };

    public string Artist
    {
        get => _artist;
        private set
        {
            if (SetProperty(ref _artist, value))
                OnPropertyChanged(nameof(AccessibleSummary));
        }
    }

    /// <summary>What a screen reader says, for example "Now playing. Lampu Kota by Hujan Petang".</summary>
    public string AccessibleSummary => _trackTitle is null
        ? _text["media.nothingPlaying"]
        : string.IsNullOrWhiteSpace(Artist)
            ? _text.Format("media.accessible", ("title", Title))
            : _text.Format("media.accessibleWithArtist", ("title", Title), ("artist", Artist));

    /// <summary>Null when the app gives no thumbnail; the view shows a placeholder.</summary>
    public ImageSource? AlbumArt
    {
        get => _albumArt;
        set => SetProperty(ref _albumArt, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    public bool HasSession
    {
        get => _hasSession;
        set
        {
            if (SetProperty(ref _hasSession, value))
                PlayPauseCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanSkipPrevious
    {
        get => _canSkipPrevious;
        set
        {
            if (SetProperty(ref _canSkipPrevious, value))
                PreviousCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanSkipNext
    {
        get => _canSkipNext;
        set
        {
            if (SetProperty(ref _canSkipNext, value))
                NextCommand.NotifyCanExecuteChanged();
        }
    }

    public void Clear()
    {
        SetTitle(null);
        Artist = string.Empty;
        AlbumArt = null;
        IsPlaying = false;
        HasSession = false;
        CanSkipPrevious = false;
        CanSkipNext = false;
    }

    public void SetTrack(string? title, string? artist)
    {
        SetTitle(string.IsNullOrWhiteSpace(title) ? string.Empty : title);
        Artist = artist ?? string.Empty;
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void SetTitle(string? title)
    {
        if (string.Equals(_trackTitle, title, StringComparison.Ordinal))
            return;
        _trackTitle = title;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(AccessibleSummary));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(AccessibleSummary));
    }
}
