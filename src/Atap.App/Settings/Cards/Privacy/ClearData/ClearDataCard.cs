using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Core.Ai;
using Atap.Core.Localization;
using Atap.Core.Net;
using Atap.Core.Settings;
using Atap.Core.Storage;
using Atap.Features.Ai;
using Atap.Settings.Cards.Privacy.ClearData;

[assembly: AtapSettingsCard(typeof(ClearDataCard))]

namespace Atap.Settings.Cards.Privacy.ClearData;

/// <summary>Shows how much the download cache takes on this PC, and clears it with one button.</summary>
internal sealed class ClearDataCard : ISettingsCard
{
    public string Id => "clearData";

    public string SectionId => SettingsSections.Privacy;

    public int Order => 90;

    public string TitleKey => "cleardata.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/Privacy/ClearData/ClearDataViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context)
    {
        var services = context.Services;
        return new ClearDataViewModel(new SavedData(services.Http, services.Paths.Cache), services.Text, keys: new AiKeyCleaner(services.Settings, services.Secrets));
    }
}

/// <summary>The Ask AI keys, which Clear saved data removes too.</summary>
internal interface ISavedKeys
{
    bool Any();

    /// <summary>The number removed.</summary>
    int RemoveAll();
}

internal sealed class AiKeyCleaner(IAppSettings settings, ISecretStore secrets) : ISavedKeys
{
    public bool Any() => AiProviders.Ids.Any(id => settings.Values.GetSecretRef(AiDescriptor.KeyPrefix + id) is not null);

    public int RemoveAll() => AiSetup.RemoveAllKeys(settings, secrets);
}

/// <summary>The cache as the card sees it.</summary>
internal interface ISavedData
{
    /// <summary>Bytes on disk.</summary>
    long Size();

    ClearResult Clear();
}

/// <summary>
/// Everything in the cache folder: the HTTP cache (memory and disk, through the service so it forgets its
/// copies too) and anything else kept there. All of it can be fetched or made again.
/// </summary>
internal sealed class SavedData(HttpDataService http, string cacheFolder) : ISavedData
{
    public long Size() => CacheFiles.Size(cacheFolder);

    public ClearResult Clear() => http.ClearCache() + CacheFiles.Clear(cacheFolder);
}

internal sealed class ClearDataViewModel : ObservableObject, IDisposable
{
    private readonly ISavedData _data;
    private readonly ILocalizer _text;
    private readonly Func<Func<ClearResult>, Task<ClearResult>> _run;
    private readonly ISavedKeys? _keys;
    private long _size;
    private ClearResult? _result;
    private int _keysRemoved;

    /// <param name="data">The cache.</param>
    /// <param name="text">UI text.</param>
    /// <param name="run">Runs the clear off the UI thread. Task.Run when null. Tests run it inline.</param>
    /// <param name="keys">The Ask AI keys, removed on the UI thread with the cache. None when null.</param>
    public ClearDataViewModel(ISavedData data, ILocalizer text, Func<Func<ClearResult>, Task<ClearResult>>? run = null, ISavedKeys? keys = null)
    {
        _data = data;
        _text = text;
        _run = run ?? Task.Run;
        _keys = keys;
        _size = data.Size();
        ClearCommand = new AsyncRelayCommand(ClearAsync);
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IAsyncRelayCommand ClearCommand { get; }

    /// <summary>How much is saved now.</summary>
    public string SizeText => _size > 0 ? _text.Format("cleardata.size", ("size", FormatSize(_size, _text))) : _text["cleardata.empty"];

    /// <summary>What the last clear did. Empty until the button is used.</summary>
    public string ResultText => _result switch
    {
        null => string.Empty,
        { FilesLeft: > 0 } r => _text.Format("cleardata.someLeft", ("size", FormatSize(r.FreedBytes, _text))),
        { FreedBytes: > 0 } r => _text.Format("cleardata.freed", ("size", FormatSize(r.FreedBytes, _text))),
        _ => _text["cleardata.nothing"],
    };

    public bool HasResult => _result is not null;

    /// <summary>Said under the hint while Ask AI keys are saved, since the button removes them too.</summary>
    public bool HasKeys => _keys?.Any() ?? false;

    public string KeysText => _text["cleardata.keys"];

    /// <summary>Said after a clear that removed Ask AI keys.</summary>
    public string KeysRemovedText => _keysRemoved > 0 ? _text["cleardata.keysRemoved"] : string.Empty;

    public bool HasKeysRemoved => _keysRemoved > 0;

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    /// <summary>"350 KB" below a megabyte, rounded up so a small file never shows as 0, else "1.2 MB".</summary>
    internal static string FormatSize(long bytes, ILocalizer text)
    {
        const double Mega = 1024 * 1024;
        if (bytes < Mega)
        {
            var kb = (long)Math.Ceiling(Math.Max(bytes, 0) / 1024.0);
            return text.Format("cleardata.kb", ("value", kb.ToString("N0", text.Culture)));
        }
        return text.Format("cleardata.mb", ("value", (bytes / Mega).ToString("0.#", text.Culture)));
    }

    private async Task ClearAsync()
    {
        // Settings are written on the UI thread, before the files are cleared off it.
        _keysRemoved = _keys?.RemoveAll() ?? 0;
        var result = await _run(_data.Clear);
        _size = _data.Size();
        _result = result;
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(HasKeys));
        OnPropertyChanged(nameof(KeysRemovedText));
        OnPropertyChanged(nameof(HasKeysRemoved));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(KeysText));
        OnPropertyChanged(nameof(KeysRemovedText));
    }
}
