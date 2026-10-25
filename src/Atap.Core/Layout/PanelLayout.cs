using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Layout;

/// <summary>The panel: one or more pages, switched with the page dots or the horizontal wheel.</summary>
public sealed class PanelLayout
{
    public const string MainPageId = "main";
    public const string MainPageName = "Main";

    public List<PanelPage> Pages { get; set; } = [NewMainPage()];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public static PanelPage NewMainPage() => new() { Id = MainPageId, Name = MainPageName };

    /// <summary>Drops broken pages, gives every page a unique id and keeps at least one page.</summary>
    internal void Normalize()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var pages = new List<PanelPage>();
        foreach (var page in Pages ?? [])
        {
            if (page is null)
                continue;
            page.Placements = StateLayout.NormalizePlacements(page.Placements);
            page.Name ??= string.Empty;
            if (string.IsNullOrWhiteSpace(page.Id) || !ids.Add(page.Id))
            {
                var number = pages.Count + 1;
                while (!ids.Add($"page-{number}"))
                    number++;
                page.Id = $"page-{number}";
            }
            pages.Add(page);
        }
        if (pages.Count == 0)
            pages.Add(NewMainPage());
        Pages = pages;
    }
}

/// <summary>One panel page.</summary>
public sealed class PanelPage
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<WidgetPlacement> Placements { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
