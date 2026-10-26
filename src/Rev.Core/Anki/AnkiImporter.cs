using System.Text.RegularExpressions;
using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Text;

namespace Rev.Core.Anki;

/// <summary>What an import did, for the message afterwards.</summary>
public sealed record ImportResult(int DecksCreated, int Added, int Updated, int Unchanged, int Images)
{
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Added > 0) parts.Add($"{Added.Counted("new card")}");
            if (Updated > 0) parts.Add($"{Updated.Counted("card")} updated");
            if (Unchanged > 0) parts.Add($"{Unchanged.Counted("card")} already up to date");
            if (Images > 0) parts.Add(Images.Counted("image"));
            string decks = DecksCreated > 0 ? $"{DecksCreated.Counted("new deck")}: " : "";
            return decks + (parts.Count == 0 ? "nothing to add." : string.Join(", ", parts) + ".");
        }
    }
}

/// <summary>
/// Puts what came out of an Anki file into Rev.
/// Decks are matched by name. Importing the same deck again updates its cards instead of adding copies:
/// a card is the same card if it has the same Anki identity (note guid + card number), or, for cards
/// imported by older versions, the same question in the same deck. Your study progress in Rev is kept.
/// </summary>
public static partial class AnkiImporter
{
    public static ImportResult Import(RevDatabase db, ApkgContents contents, DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        int decksCreated = 0, added = 0, updated = 0, unchanged = 0;

        db.InTransaction(() =>
        {
            var decksByName = db.GetDecks().GroupBy(d => d.Name).ToDictionary(g => g.Key, g => g.First());
            var allCards = db.GetCards();
            var byIdentity = allCards.ToDictionary(c => c.SyncKey);
            var matched = new HashSet<Guid>();

            foreach (var imported in contents.Decks)
            {
                if (!decksByName.TryGetValue(imported.Name, out var deck))
                {
                    deck = db.AddDeck(imported.Name, at);
                    decksByName[deck.Name] = deck;
                    decksCreated += 1;
                }

                // Cards already in this deck, looked up by their question.
                var byQuestion = allCards.Where(c => c.DeckId == deck.Id)
                    .GroupBy(c => MatchKey(c.Front)).ToDictionary(g => g.Key, g => new Queue<Card>(g));

                foreach (var card in imported.Cards)
                {
                    var existing = byIdentity.GetValueOrDefault(card.SyncKey);
                    if (existing is null && byQuestion.TryGetValue(MatchKey(card.Front), out var queue))
                        while (queue.Count > 0 && existing is null)
                        {
                            var candidate = queue.Dequeue();
                            if (!matched.Contains(candidate.Id)) existing = candidate;
                        }

                    if (existing is not null && matched.Add(existing.Id))
                    {
                        if (UpdateText(existing, card, at)) { db.UpdateCard(existing); updated += 1; }
                        else unchanged += 1;
                        continue;
                    }
                    if (existing is not null) continue;   // two cards in the file match one card: keep the first

                    card.DeckId = deck.Id;
                    db.AddCard(card);
                    byIdentity[card.SyncKey] = card;
                    matched.Add(card.Id);
                    added += 1;
                }
            }
        });
        return new ImportResult(decksCreated, added, updated, unchanged, contents.ImagesSaved);
    }

    /// <summary>
    /// Brings Anki's text into a card already in Rev (its study progress stays as it is in Rev).
    /// Anki's tags are taken only if you haven't tagged the card in Rev. Returns true if anything changed.
    /// </summary>
    private static bool UpdateText(Card existing, Card fromAnki, DateTime now)
    {
        bool changed = false;
        if (existing.Front != fromAnki.Front || existing.Back != fromAnki.Back)
        {
            (existing.Front, existing.Back) = (fromAnki.Front, fromAnki.Back);
            (existing.Kind, existing.ClozeSource, existing.ClozeNumber, existing.ClozeExtra, existing.FieldsReadOnly) =
                (fromAnki.Kind, fromAnki.ClozeSource, fromAnki.ClozeNumber, fromAnki.ClozeExtra, fromAnki.FieldsReadOnly);
            changed = true;
        }
        if (existing.TagsText.Length == 0 && fromAnki.TagsText.Length > 0)
        {
            existing.TagsText = fromAnki.TagsText;
            changed = true;
        }
        if (changed) existing.MarkEdited(now);
        return changed;
    }

    [GeneratedRegex(@"\[img:[^\]]+\]|\[image\]")]
    private static partial Regex ImagePattern();

    /// <summary>
    /// The question used to recognise the same card again. Image names, formatting marks and line breaks
    /// are ignored (older versions of Rev stored them differently). The same rule as Rev for iPhone.
    /// </summary>
    public static string MatchKey(string front) =>
        string.Join(" ", ImagePattern().Replace(front, "🖼").Replace("*", "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
