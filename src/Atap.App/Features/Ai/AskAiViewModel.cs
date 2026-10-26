using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core.Ai;
using Atap.Core.Localization;

namespace Atap.Features.Ai;

/// <summary>
/// The Ask AI part of the command bar: the "Include what I copied" box and the send. Only <see cref="Ask"/> sends,
/// and the command bar calls it on Enter. The answer then shows in the notch. Copied text is read only while the box
/// is ticked, and the tick clears after each question. Made fresh on every open of the bar.
/// </summary>
internal sealed class AskAiViewModel : ObservableObject, IDisposable
{
    /// <summary>How much of the copied text the preview shows.</summary>
    private const int PreviewLength = 280;

    private readonly IAskAi _ai;
    private readonly ILocalizer _text;
    private readonly Func<string?> _readClipboard;
    private bool _includeCopied;
    private string _copied = string.Empty;
    private bool _copiedCut;
    private bool _disposed;

    /// <param name="ai">The running Ask AI.</param>
    /// <param name="text">UI text.</param>
    /// <param name="readClipboard">Reads the copied text, only when the box is ticked.</param>
    public AskAiViewModel(IAskAi ai, ILocalizer text, Func<string?> readClipboard)
    {
        _ai = ai;
        _text = text;
        _readClipboard = readClipboard;
        _ai.SetupChanged += OnSetupChanged;
    }

    /// <summary>What is missing before a question can go. The command bar then offers Settings instead.</summary>
    public AiSetupProblem Problem => _ai.Problem;

    public string ServiceName => _ai.ServiceName;

    /// <summary>Send what is on the clipboard with the next question. Reading it happens only when ticked.</summary>
    public bool IncludeCopied
    {
        get => _includeCopied;
        set
        {
            if (!SetProperty(ref _includeCopied, value))
                return;
            var raw = value ? _readClipboard() : null;
            _copied = AiPrompt.Copied(raw);
            _copiedCut = raw is not null && raw.Trim().Length > AiPrompt.MaxCopiedLength;
            OnPropertyChanged(nameof(CopiedPreview));
            OnPropertyChanged(nameof(HasCopiedPreview));
            OnPropertyChanged(nameof(CopiedNote));
            OnPropertyChanged(nameof(HasCopiedNote));
        }
    }

    /// <summary>The start of what would go with the question, while the box is ticked.</summary>
    public string CopiedPreview => !_includeCopied || _copied.Length == 0 ? string.Empty
        : AiChat.Clean(_copied.Length > PreviewLength ? _copied[..PreviewLength] + "..." : _copied);

    public bool HasCopiedPreview => CopiedPreview.Length > 0;

    /// <summary>Nothing is copied, or only the first 8,000 characters go.</summary>
    public string CopiedNote => !_includeCopied ? string.Empty
        : _copied.Length == 0 ? _text["ai.copiedNone"]
        : _copiedCut ? _text["ai.copiedCut"]
        : string.Empty;

    public bool HasCopiedNote => CopiedNote.Length > 0;

    /// <summary>Sends <paramref name="question"/>, with the copied text if the box is ticked. False when nothing was sent.</summary>
    public bool Ask(string question)
    {
        var asked = question.Trim();
        if (_disposed || asked.Length == 0 || _ai.Problem != AiSetupProblem.None)
            return false;
        return _ai.Ask(asked, TakeCopied());
    }

    /// <summary>Forgets the copied text. The command bar calls it when it closes. An answer in the notch carries on.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _ai.SetupChanged -= OnSetupChanged;
        _copied = string.Empty;
    }

    /// <summary>The copied text while the box is ticked, else null. The tick clears, so it goes once.</summary>
    private string? TakeCopied()
    {
        var copied = _includeCopied && _copied.Length > 0 ? _copied : null;
        IncludeCopied = false;
        return copied;
    }

    /// <summary>The service, key or model changed: copied text is not kept for it.</summary>
    private void OnSetupChanged(object? sender, EventArgs e) => IncludeCopied = false;
}
