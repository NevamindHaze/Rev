using Rev.Core.Models;
using Rev.Core.Text;

namespace Rev.Core.Data;

// Adding and editing whole notes (a note can be several cards: a reversed pair, or c1, c2… of a cloze).
public sealed partial class RevDatabase
{
    /// <summary>Saves the card(s) for one new note. Returns how many cards were made.</summary>
    public int AddNote(Guid deckId, CardKind kind, string front, string back, IEnumerable<string> tags, DateTime? now = null)
    {
        var cards = CardFactory.Create(kind, front.Trim(), back.Trim(), tags, deckId, now ?? DateTime.Now);
        InTransaction(() => cards.ForEach(AddCard));
        return cards.Count;
    }

    /// <summary>Paste many: adds one basic card per (front, back) pair, in the order given.</summary>
    public int AddPasted(Guid deckId, IReadOnlyList<(string Front, string Back)> pairs, DateTime? now = null)
    {
        var start = now ?? DateTime.Now;
        // One millisecond apart, so the cards stay in the order you pasted them.
        InTransaction(() =>
        {
            for (int i = 0; i < pairs.Count; i++)
                AddCard(CardFactory.Basic(pairs[i].Front, pairs[i].Back, deckId, start.AddMilliseconds(i)));
        });
        return pairs.Count;
    }

    /// <summary>
    /// Saves your changes to a card. Study progress is kept.
    /// - Cloze: every card of the note is rewritten; new numbers get new cards, removed numbers lose theirs.
    /// - Reversed: the other card of the pair gets the same text the other way round.
    /// </summary>
    public void SaveEdit(Card card, string front, string back, IEnumerable<string> tags, DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        front = front.Trim();
        back = back.Trim();
        string tagsText = Tags.Format(tags);
        var siblings = GetCards(card.DeckId).Where(c => c.NoteId == card.NoteId && c.Id != card.Id).ToList();

        InTransaction(() =>
        {
            if (card.Kind == CardKind.Cloze && card.ClozeSource != null)
            {
                var numbers = Cloze.Numbers(front);
                var noteCards = siblings.Where(c => c.Kind == CardKind.Cloze).Append(card).ToList();
                foreach (var c in noteCards)
                {
                    if (c.ClozeNumber is int n && numbers.Contains(n))
                    {
                        CardFactory.FillCloze(c, front, back, n);
                        c.TagsText = tagsText;
                        c.MarkEdited(at);
                        UpdateCard(c);
                    }
                    else DeleteCard(c.Id);
                }
                var existing = noteCards.Select(c => c.ClozeNumber).ToHashSet();
                foreach (int n in numbers.Where(n => !existing.Contains(n)))
                {
                    var added = new Card
                    {
                        DeckId = card.DeckId, Kind = CardKind.Cloze, NoteId = card.NoteId, TagsText = tagsText,
                        CreatedAt = at, Due = at, EditedAt = at, SchedChangedAt = at,
                    };
                    CardFactory.FillCloze(added, front, back, n);
                    AddCard(added);
                }
                return;
            }

            (card.Front, card.Back, card.TagsText) = (front, back, tagsText);
            card.MarkEdited(at);
            UpdateCard(card);

            if (card.Kind == CardKind.Reversed)
            {
                foreach (var twin in siblings.Where(c => c.Kind == CardKind.Reversed))
                {
                    (twin.Front, twin.Back, twin.TagsText) = (back, front, tagsText);
                    twin.MarkEdited(at);
                    UpdateCard(twin);
                }
            }
        });
    }

    // ---- Small remembered choices (e.g. the last card kind you used) ----

    public string? GetPreference(string key) =>
        Query("SELECT value FROM settings WHERE key = $key", r => r.GetString(0), ("$key", "pref." + key)).FirstOrDefault();

    public void SetPreference(string key, string value) => SetValue("pref." + key, value);
}
