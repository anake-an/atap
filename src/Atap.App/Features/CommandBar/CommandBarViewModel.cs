using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core.Commands;
using Atap.Core.Commands.Query;
using Atap.Core.Localization;
using Atap.Features.Ai;
using Atap.Platform.Apps;

namespace Atap.Features.CommandBar;

/// <summary>One row of results: what it is, a word on what Enter does, and the action.</summary>
/// <param name="KeepsOpen">
/// The bar stays open while the row runs. Ask AI uses it to read the copied text first, then closes the bar itself.
/// </param>
internal sealed record CommandBarRow(QueryResultKind Kind, string Title, string Detail, string Glyph, Action Run, bool KeepsOpen = false);

/// <summary>
/// What the command bar shows for the text typed: a sum, a unit conversion, Atap commands, apps from Start and,
/// always last, a web search. Rows are ranked by Core's <see cref="QueryRanker"/>. Nothing runs until Enter or a
/// click, so typing never opens a browser. Made fresh on every open, and the app list goes with it.
/// While Ask AI is on, an Ask AI row sits above web search. Only Enter on it sends. The bar then closes and the
/// answer shows in the notch.
/// </summary>
internal sealed class CommandBarViewModel : ObservableObject, IDisposable
{
    public const string OpenCommandId = "commandbar.open";

    /// <summary>Opens Settings at the Ask AI card when Ask AI still needs a key.</summary>
    public const string SettingsCommandId = "settings.open";

    private const string AskAiGlyph = "";

    private readonly ICommandRegistry _commands;
    private readonly ILocalizer _text;
    private readonly IAppCatalog _catalog;
    private readonly Action<string> _openUrl;
    private readonly Func<string, bool> _copy;
    private readonly string _searchUrl;
    private IReadOnlyList<InstalledApp> _apps = [];
    private string _query = string.Empty;
    private int _selectedIndex = -1;

    /// <param name="searchUrl">The search page, with <c>{q}</c> where the query goes.</param>
    /// <param name="ai">The Ask AI part while Ask AI is on, else null.</param>
    /// <param name="askMode">Opened by the Ask AI shortcut: only the Ask AI row shows.</param>
    public CommandBarViewModel(
        ICommandRegistry commands, ILocalizer text, IAppCatalog catalog, Action<string> openUrl, Func<string, bool> copy, string searchUrl,
        AskAiViewModel? ai = null, bool askMode = false)
    {
        _commands = commands;
        _text = text;
        _catalog = catalog;
        _openUrl = openUrl;
        _copy = copy;
        _searchUrl = searchUrl;
        Ai = ai;
        IsAskMode = askMode && ai is not null;
    }

    /// <summary>The Ask AI part, or null while Ask AI is off.</summary>
    public AskAiViewModel? Ai { get; }

    public bool HasAi => Ai is not null;

    /// <summary>Opened ready to ask: only the Ask AI row shows.</summary>
    public bool IsAskMode { get; }

    /// <summary>The tick box for copied text shows while the Ask AI row is chosen.</summary>
    public bool ShowCopiedOption =>
        Ai is { Problem: AiSetupProblem.None }
        && (IsAskMode || (_selectedIndex >= 0 && _selectedIndex < Rows.Count && Rows[_selectedIndex].Kind == QueryResultKind.AskAi));

    /// <summary>Raised after a row ran, so the window closes.</summary>
    public event EventHandler? Done;

    /// <summary>Raised after a question went to Ask AI, so the window closes and the app before it gets the keyboard back.</summary>
    public event EventHandler? Asked;

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value ?? string.Empty))
                Refresh();
        }
    }

    public IReadOnlyList<CommandBarRow> Rows { get; private set; } = [];

    public bool HasRows => Rows.Count > 0;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (SetProperty(ref _selectedIndex, Rows.Count == 0 ? -1 : Math.Clamp(value, 0, Rows.Count - 1)))
                OnPropertyChanged(nameof(ShowCopiedOption));
        }
    }

    public string Placeholder => _text[IsAskMode ? "ai.placeholder" : "commandbar.placeholder"];

    /// <summary>Forgets the copied text. The window calls it when it closes. An answer in the notch carries on.</summary>
    public void Dispose() => Ai?.Dispose();

    /// <summary>Reads the apps from Start once for this open. Typed text before they arrive is ranked again after.</summary>
    public async Task LoadAppsAsync()
    {
        _apps = await _catalog.LoadAsync();
        Refresh();
    }

    /// <summary>Up and Down. It wraps round, so Up from the first row reaches web search.</summary>
    public void Move(int step)
    {
        if (Rows.Count == 0)
            return;
        SelectedIndex = ((_selectedIndex + step) % Rows.Count + Rows.Count) % Rows.Count;
    }

    /// <summary>Enter: runs the chosen row. False when there is nothing to run.</summary>
    public bool RunSelected()
    {
        if (_selectedIndex < 0 || _selectedIndex >= Rows.Count)
            return false;
        Run(Rows[_selectedIndex]);
        return true;
    }

    public void Run(CommandBarRow row)
    {
        // The window closes first, so an app or page opened next takes the front.
        if (!row.KeepsOpen)
            Done?.Invoke(this, EventArgs.Empty);
        row.Run();
    }

    /// <summary>Enter on the Ask AI row: sends the question, with the copied text if ticked, then closes the bar.</summary>
    private void AskAi(string question)
    {
        if (Ai is null || !Ai.Ask(question))
            return;
        Asked?.Invoke(this, EventArgs.Empty);
    }

    private void Refresh()
    {
        var query = _query.Trim();
        var actions = new Dictionary<(QueryResultKind, string), (string Detail, string Glyph, Action Run, bool KeepsOpen)>();
        var candidates = new List<QueryCandidate>();

        void Add(QueryCandidate candidate, string detail, string glyph, Action run, bool keepsOpen = false)
        {
            if (actions.TryAdd((candidate.Kind, candidate.Id), (detail, glyph, run, keepsOpen)))
                candidates.Add(candidate);
        }

        if (query.Length > 0 && IsAskMode)
            AddAskAi(Add, query);

        if (query.Length > 0 && !IsAskMode)
        {
            var sum = Calculator.Evaluate(query);
            if (sum.Success && !sum.IsTrivial)
            {
                var shown = QueryNumber.Format(sum.Value, CultureInfo.CurrentCulture);
                var plain = QueryNumber.Format(sum.Value, CultureInfo.InvariantCulture, grouping: false);
                Add(new QueryCandidate("sum", QueryResultKind.Calculation, $"= {shown}"), _text["commandbar.copy"], "", () => _copy(plain));
            }

            var unit = UnitConverter.Parse(query);
            if (unit is { Success: true, Conversion: { } conversion })
            {
                var plain = QueryNumber.Format(conversion.Result, CultureInfo.InvariantCulture, grouping: false);
                Add(new QueryCandidate("unit", QueryResultKind.Conversion, conversion.Format(CultureInfo.CurrentCulture)), _text["commandbar.copy"], "", () => _copy(plain));
            }

            foreach (var command in _commands.All)
            {
                if (!command.ShowInCommandBar || command.Id == OpenCommandId || !(command.IsAvailable?.Invoke() ?? true))
                    continue;
                var keywords = command.KeywordsKey is { } key ? _text[key].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
                var id = command.Id;
                Add(new QueryCandidate(id, QueryResultKind.Command, _text[command.TitleKey]) { Keywords = keywords },
                    _text["commandbar.kind.command"], "", () => _ = _commands.TryRunAsync(id));
            }

            foreach (var app in _apps)
                Add(new QueryCandidate(app.ParsingName, QueryResultKind.App, app.Name), _text["commandbar.kind.app"], "", () => _catalog.Launch(app));

            AddAskAi(Add, query);
            Add(new QueryCandidate("web", QueryResultKind.WebSearch, _text.Format("commandbar.searchWeb", ("query", query))),
                _text["commandbar.kind.web"], "", () => _openUrl(_searchUrl.Replace("{q}", Uri.EscapeDataString(query), StringComparison.Ordinal)));
        }

        Rows = QueryRanker.Rank(query, candidates)
            .Select(r =>
            {
                var (detail, glyph, run, keepsOpen) = actions[(r.Candidate.Kind, r.Candidate.Id)];
                return new CommandBarRow(r.Candidate.Kind, r.Candidate.Title, detail, glyph, run, keepsOpen);
            })
            .ToList();
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(HasRows));
        _selectedIndex = -1;
        SelectedIndex = 0;
        OnPropertyChanged(nameof(SelectedIndex));
        OnPropertyChanged(nameof(ShowCopiedOption));
    }

    /// <summary>
    /// The Ask AI row. It asks only on Enter or a click, and the answer shows in the notch. While a key is
    /// missing it opens Settings at the Ask AI card instead.
    /// </summary>
    private void AddAskAi(Action<QueryCandidate, string, string, Action, bool> add, string query)
    {
        if (Ai is null)
            return;
        if (Ai.Problem != AiSetupProblem.None)
        {
            add(new QueryCandidate("ai", QueryResultKind.AskAi, _text["ai.row.setup"]), _text["ai.row.setupDetail"], AskAiGlyph,
                () => _ = _commands.TryRunAsync(SettingsCommandId, Composition.SettingsSections.Behavior), false);
            return;
        }
        add(new QueryCandidate("ai", QueryResultKind.AskAi, _text.Format("ai.row", ("query", query))), _text["ai.row.enter"], AskAiGlyph,
            () => AskAi(query), true);
    }
}
