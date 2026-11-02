using Rev.Core.Models;

namespace Rev.Core.Data;

// Reading and writing decks.
public sealed partial class RevDatabase
{
    /// <summary>All decks, oldest first (the order they appear on the Home screen).</summary>
    public List<Deck> GetDecks() => Query(
        "SELECT id, name, created_at FROM decks ORDER BY created_at, name",
        r => new Deck { Id = Guid.Parse(r.GetString(0)), Name = r.GetString(1), CreatedAt = FromMs(r.GetInt64(2)) });

    public Deck AddDeck(string name, DateTime? createdAt = null)
    {
        var deck = new Deck { Name = name.Trim(), CreatedAt = createdAt ?? DateTime.Now };
        AddDeck(deck);
        return deck;
    }

    /// <summary>Adds a deck that already has its id (e.g. from a backup).</summary>
    public void AddDeck(Deck deck) =>
        Execute("INSERT INTO decks (id, name, created_at) VALUES ($id, $name, $created)",
            ("$id", deck.Id.ToString()), ("$name", deck.Name), ("$created", ToMs(deck.CreatedAt)));

    /// <summary>Removes every deck, card and review (used by restore, inside its transaction).</summary>
    public void DeleteEverything() =>
        Execute("DELETE FROM review_logs; DELETE FROM cards; DELETE FROM decks;");

    /// <summary>
    /// Renames a deck. Its cards count as edited, because a card's deck is part of what syncs with Anki.
    /// </summary>
    public void RenameDeck(Guid deckId, string newName, DateTime? now = null)
    {
        long editedAt = ToMs(now ?? DateTime.Now);
        InTransaction(() =>
        {
            Execute("UPDATE decks SET name = $name WHERE id = $id", ("$id", deckId.ToString()), ("$name", newName.Trim()));
            Execute("UPDATE cards SET edited_at = $at WHERE deck_id = $id", ("$id", deckId.ToString()), ("$at", editedAt));
        });
    }

    /// <summary>
    /// Deletes a deck and every card in it. Review history is kept, so past days in Stats stay the same.
    /// </summary>
    public void DeleteDeck(Guid deckId)
    {
        InTransaction(() =>
        {
            Execute("DELETE FROM cards WHERE deck_id = $id", ("$id", deckId.ToString()));
            Execute("DELETE FROM decks WHERE id = $id", ("$id", deckId.ToString()));
        });
    }

    public int CountCards(Guid deckId) =>
        (int)Scalar<long>("SELECT COUNT(*) FROM cards WHERE deck_id = $id", ("$id", deckId.ToString()));
}
