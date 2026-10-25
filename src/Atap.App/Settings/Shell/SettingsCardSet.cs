using System.Reflection;
using Atap.Composition;

namespace Atap.Settings.Shell;

/// <summary>
/// The settings cards, created from the registered card types when the window opens and dropped with it.
/// Cards sort by Order, then Id. A card that cannot be created, or whose id is taken, is left out and logged.
/// </summary>
internal sealed class SettingsCardSet
{
    private SettingsCardSet(IReadOnlyList<ISettingsCard> cards) => Cards = cards;

    public IReadOnlyList<ISettingsCard> Cards { get; }

    /// <param name="types">From <see cref="Discovery.SettingsCards"/>.</param>
    /// <param name="log">Warnings, with the exception if any.</param>
    public static SettingsCardSet Create(IEnumerable<Type> types, Action<string, Exception?> log)
    {
        var cards = new List<ISettingsCard>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            ISettingsCard card;
            try
            {
                card = (ISettingsCard)Activator.CreateInstance(type)!;
            }
            catch (Exception ex)
            {
                log($"Settings card {type.FullName} could not be created.", (ex as TargetInvocationException)?.InnerException ?? ex);
                continue;
            }
            if (string.IsNullOrWhiteSpace(card.Id) || !ids.Add(card.Id))
            {
                log($"Settings card {type.FullName} has an empty or duplicate id '{card.Id}'.", null);
                continue;
            }
            cards.Add(card);
        }
        return new SettingsCardSet(cards.OrderBy(c => c.Order).ThenBy(c => c.Id, StringComparer.Ordinal).ToList());
    }

    /// <summary>The cards of these sections, in order.</summary>
    public IReadOnlyList<ISettingsCard> For(IEnumerable<string> sectionIds)
    {
        var sections = sectionIds.ToHashSet(StringComparer.Ordinal);
        return Cards.Where(c => sections.Contains(c.SectionId)).ToList();
    }
}
