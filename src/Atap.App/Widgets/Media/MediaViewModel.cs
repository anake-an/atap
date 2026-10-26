using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core.Localization;
using Atap.Core.Media;
using Atap.Notch.Automation;

namespace Atap.Widgets.Media;

public sealed class MediaViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;

    // Null while nothing is playing; empty when the app gives no title.
    private string? _trackTitle;
    private string _artist = string.Empty;
    private ImageSource? _albumArt;
    private Brush? _albumBrush;
    private bool _isPlaying;
    private bool _hasSession;
    private bool _canSkipPrevious;
    private bool _canSkipNext;
    private double _progress;
    private bool _hasProgress;
    private bool _prefersLyricLine;
    private string _lyricLine = string.Empty;
    private bool _isCompactMiddleShown;

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
            if (!SetProperty(ref _artist, value))
                return;
            OnPropertyChanged(nameof(AccessibleSummary));
            OnPropertyChanged(nameof(CompactDetail));
        }
    }

    /// <summary>What a screen reader says, for example "Now playing. Lampu Kota by Hujan Petang".</summary>
    public string AccessibleSummary => _trackTitle is null
        ? _text["media.nothingPlaying"]
        : string.IsNullOrWhiteSpace(Artist)
            ? _text.Format("media.accessible", ("title", Title))
            : _text.Format("media.accessibleWithArtist", ("title", Title), ("artist", Artist));

    /// <summary>
    /// What the small notch shows in the middle: the song title, or the lyric line playing now when that is chosen
    /// and there is one. Without a line it falls back to the title.
    /// </summary>
    public string CompactText => ShowsLyricLine ? _lyricLine : Title;

    /// <summary>The muted part after the title, " · Artist". Empty with no artist, or while a lyric line shows.</summary>
    public string CompactDetail => ShowsLyricLine || string.IsNullOrWhiteSpace(Artist) ? string.Empty : $" · {Artist}";

    /// <summary>The media setting: the small notch shows the lyric line in place of the title when there is one.</summary>
    public bool PrefersLyricLine
    {
        get => _prefersLyricLine;
        set
        {
            if (_prefersLyricLine == value)
                return;
            _prefersLyricLine = value;
            OnCompactChanged();
        }
    }

    /// <summary>The lyric line playing now, shared by the lyrics widget. Empty when there is none.</summary>
    public string LyricLine
    {
        get => _lyricLine;
        set
        {
            value = string.IsNullOrWhiteSpace(value) ? string.Empty : value;
            if (_lyricLine == value)
                return;
            _lyricLine = value;
            OnCompactChanged();
        }
    }

    /// <summary>
    /// True while the middle of the small notch is on screen, with the title and the progress line. Set by the view
    /// through <see cref="MediaShownReport"/>. False on a side edge, where that part is left out.
    /// </summary>
    public bool IsCompactMiddleShown
    {
        get => _isCompactMiddleShown;
        set => SetProperty(ref _isCompactMiddleShown, value);
    }

    /// <summary>True while the small notch shows a lyric line and not the title.</summary>
    public bool ShowsLyricLine => _prefersLyricLine && _trackTitle is not null && _lyricLine.Length > 0;

    /// <summary>Null when the app gives no thumbnail; the view shows a placeholder.</summary>
    public ImageSource? AlbumArt
    {
        get => _albumArt;
        set
        {
            if (!SetProperty(ref _albumArt, value))
                return;
            if (SetProperty(ref _albumBrush, BrushOf(value), nameof(AlbumBrush)))
                OnPropertyChanged(nameof(HasAlbumColour));
        }
    }

    /// <summary>
    /// The cover's main colour, lifted to read on black, for the playing bars and the small notch's line. Null
    /// without a cover, so the view keeps the music accent. In high contrast the view uses system colours instead.
    /// </summary>
    public Brush? AlbumBrush => _albumBrush;

    public bool HasAlbumColour => _albumBrush is not null;

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

    /// <summary>How far through the song, from 0 to 100. Only meaningful while <see cref="HasProgress"/>.</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>False when the player reports no song length, so the bar is left out.</summary>
    public bool HasProgress
    {
        get => _hasProgress;
        private set => SetProperty(ref _hasProgress, value);
    }

    /// <summary>Sets the bar from a share of the song, 0 to 1, or hides it with null.</summary>
    public void SetProgress(double? fraction)
    {
        HasProgress = fraction is not null;
        // Whole tenths of a percent, so a tick that moves the bar less than a pixel changes nothing.
        Progress = fraction is { } value ? Math.Round(value * 1000) / 10 : 0;
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
        SetProgress(null);
    }

    public void SetTrack(string? title, string? artist)
    {
        SetTitle(string.IsNullOrWhiteSpace(title) ? string.Empty : title);
        Artist = artist ?? string.Empty;
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    /// <summary>A frozen brush in the cover's main colour, or null when there is no cover to read.</summary>
    internal static Brush? BrushOf(ImageSource? art)
    {
        if (art is not BitmapSource bitmap || bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
            return null;
        try
        {
            // The covers Atap decodes are already 32 bit, so this copies nothing more than the small picture.
            BitmapSource pixels = bitmap.Format == PixelFormats.Pbgra32 || bitmap.Format == PixelFormats.Bgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var stride = pixels.PixelWidth * 4;
            var buffer = new byte[stride * pixels.PixelHeight];
            pixels.CopyPixels(buffer, stride, 0);
            if (AlbumColour.From(buffer) is not { } colour)
                return null;
            var brush = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B));
            brush.Freeze();
            return brush;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private void SetTitle(string? title)
    {
        if (string.Equals(_trackTitle, title, StringComparison.Ordinal))
            return;
        _trackTitle = title;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(AccessibleSummary));
        OnCompactChanged();
    }

    private void OnCompactChanged()
    {
        OnPropertyChanged(nameof(ShowsLyricLine));
        OnPropertyChanged(nameof(CompactText));
        OnPropertyChanged(nameof(CompactDetail));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(AccessibleSummary));
        OnPropertyChanged(nameof(CompactText));
    }
}
