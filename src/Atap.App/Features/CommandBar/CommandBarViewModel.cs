using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core.Commands;
using Atap.Core.Commands.Query;
using Atap.Core.Localization;
using Atap.Platform.Apps;

namespace Atap.Features.CommandBar;

/// <summary>One row of results: what it is, a word on what Enter does, and the action.</summary>
internal sealed record CommandBarRow(QueryResultKind Kind, string Title, string Detail, string Glyph, Action Run);

/// <summary>
/// What the command bar shows for the text typed: a sum, a unit conversion, Atap commands, apps from Start and,
/// always last, a web search. Rows are ranked by Core's <see cref="QueryRanker"/>. Nothing runs until Enter or a
/// click, so typing never opens a browser. Made fresh on every open, and the app list goes with it.
/// </summary>
internal sealed class CommandBarViewModel : ObservableObject
{
    public const string OpenCommandId = "commandbar.open";

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
    public CommandBarViewModel(ICommandRegistry commands, ILocalizer text, IAppCatalog catalog, Action<string> openUrl, Func<string, bool> copy, string searchUrl)
    {
        _commands = commands;
        _text = text;
        _catalog = catalog;
        _openUrl = openUrl;
        _copy = copy;
        _searchUrl = searchUrl;
    }

    /// <summary>Raised after a row ran, so the window closes.</summary>
    public event EventHandler? Done;

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
        set => SetProperty(ref _selectedIndex, Rows.Count == 0 ? -1 : Math.Clamp(value, 0, Rows.Count - 1));
    }

    public string Placeholder => _text["commandbar.placeholder"];

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
        Done?.Invoke(this, EventArgs.Empty);
        row.Run();
    }

    private void Refresh()
    {
        var query = _query.Trim();
        var actions = new Dictionary<(QueryResultKind, string), (string Detail, string Glyph, Action Run)>();
        var candidates = new List<QueryCandidate>();

        void Add(QueryCandidate candidate, string detail, string glyph, Action run)
        {
            if (actions.TryAdd((candidate.Kind, candidate.Id), (detail, glyph, run)))
                candidates.Add(candidate);
        }

        if (query.Length > 0)
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

            Add(new QueryCandidate("web", QueryResultKind.WebSearch, _text.Format("commandbar.searchWeb", ("query", query))),
                _text["commandbar.kind.web"], "", () => _openUrl(_searchUrl.Replace("{q}", Uri.EscapeDataString(query), StringComparison.Ordinal)));
        }

        Rows = QueryRanker.Rank(query, candidates)
            .Select(r =>
            {
                var (detail, glyph, run) = actions[(r.Candidate.Kind, r.Candidate.Id)];
                return new CommandBarRow(r.Candidate.Kind, r.Candidate.Title, detail, glyph, run);
            })
            .ToList();
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(HasRows));
        _selectedIndex = -1;
        SelectedIndex = 0;
        OnPropertyChanged(nameof(SelectedIndex));
    }
}
