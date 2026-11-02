using Rev.Core.Text;

namespace Rev.Core.Models;

/// <summary>Turns what you type in the editor into one or more cards, depending on the card kind.</summary>
public static class CardFactory
{
    /// <summary>
    /// The cards for one new note. All of them share a new note id (a UUID), so burying and sync
    /// know they belong together. Basic and Type-in make 1 card, Reversed 2, Cloze one per number.
    /// </summary>
    public static List<Card> Create(CardKind kind, string front, string back, IEnumerable<string> tags,
                                    Guid deckId, DateTime now)
    {
        string noteId = Guid.NewGuid().ToString().ToUpperInvariant();
        string tagsText = Tags.Format(tags);
        Card Make(int ord, int index) => new()
        {
            DeckId = deckId, Kind = kind, NoteId = noteId, NoteOrd = ord, TagsText = tagsText,
            // Tiny time steps keep the cards of one note in order.
            CreatedAt = now.AddMilliseconds(index), Due = now.AddMilliseconds(index),
            EditedAt = now, SchedChangedAt = now,
        };

        switch (kind)
        {
            case CardKind.Reversed:
                var forward = Make(0, 0);
                (forward.Front, forward.Back) = (front, back);
                var reverse = Make(1, 1);
                (reverse.Front, reverse.Back) = (back, front);
                return [forward, reverse];

            case CardKind.Cloze:
                return Cloze.Numbers(front).Select((number, index) =>
                {
                    var card = Make(number - 1, index);
                    FillCloze(card, front, back, number);
                    return card;
                }).ToList();

            default:   // Basic and Type the answer
                var single = Make(0, 0);
                (single.Front, single.Back) = (front, back);
                return [single];
        }
    }

    /// <summary>A plain basic card, e.g. from Paste many.</summary>
    public static Card Basic(string front, string back, Guid deckId, DateTime createdAt) =>
        Create(CardKind.Basic, front, back, [], deckId, createdAt)[0];

    /// <summary>
    /// Fills in a cloze card's two sides from the full text. The card's ord is its cloze number − 1,
    /// the same as Anki's.
    /// </summary>
    public static void FillCloze(Card card, string source, string extra, int number)
    {
        card.ClozeSource = source;
        card.ClozeExtra = extra;
        card.ClozeNumber = number;
        card.NoteOrd = number - 1;
        card.Front = Cloze.Render(source, number, Cloze.Side.Front);
        string answer = Cloze.Render(source, number, Cloze.Side.Back);
        card.Back = extra.Length == 0 ? answer : answer + "\n\n" + extra;
    }
}
