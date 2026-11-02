using Microsoft.Data.Sqlite;
using Rev.Core.Models;
using Rev.Core.Scheduling;

namespace Rev.Core.Data;

// Reading and writing cards.
public sealed partial class RevDatabase
{
    private const string CardColumns = """
        id, deck_id, front, back, created_at, kind, tags, suspended, leech,
        cloze_source, cloze_number, cloze_extra, note_id, note_ord, edited_at, sched_changed_at,
        fields_read_only, phase, due, interval_days, ease, reps, lapses, step, last_reviewed,
        stability, difficulty
        """;

    private const string CardValues = """
        $id, $deck, $front, $back, $created, $kind, $tags, $suspended, $leech,
        $clozeSource, $clozeNumber, $clozeExtra, $noteId, $noteOrd, $edited, $schedChanged,
        $readOnly, $phase, $due, $interval, $ease, $reps, $lapses, $step, $lastReviewed,
        $stability, $difficulty
        """;

    /// <summary>Every card, or only the cards of one deck.</summary>
    public List<Card> GetCards(Guid? deckId = null) => deckId is { } id
        ? Query($"SELECT {CardColumns} FROM cards WHERE deck_id = $deck ORDER BY created_at", ReadCard,
                ("$deck", id.ToString()))
        : Query($"SELECT {CardColumns} FROM cards ORDER BY created_at", ReadCard);

    public Card? GetCard(Guid cardId) =>
        Query($"SELECT {CardColumns} FROM cards WHERE id = $id", ReadCard, ("$id", cardId.ToString()))
            .FirstOrDefault();

    public void AddCard(Card card) =>
        Execute($"INSERT INTO cards ({CardColumns}) VALUES ({CardValues})", CardParameters(card));

    /// <summary>Saves every change to an existing card (text, tags, schedule, …).</summary>
    public void UpdateCard(Card card) =>
        Execute("""
            UPDATE cards SET deck_id = $deck, front = $front, back = $back, created_at = $created,
                kind = $kind, tags = $tags, suspended = $suspended, leech = $leech,
                cloze_source = $clozeSource, cloze_number = $clozeNumber, cloze_extra = $clozeExtra,
                note_id = $noteId, note_ord = $noteOrd, edited_at = $edited, sched_changed_at = $schedChanged,
                fields_read_only = $readOnly, phase = $phase, due = $due, interval_days = $interval,
                ease = $ease, reps = $reps, lapses = $lapses, step = $step, last_reviewed = $lastReviewed,
                stability = $stability, difficulty = $difficulty
            WHERE id = $id
            """, CardParameters(card));

    public void DeleteCard(Guid cardId) =>
        Execute("DELETE FROM cards WHERE id = $id", ("$id", cardId.ToString()));

    private static (string, object?)[] CardParameters(Card c) =>
    [
        ("$id", c.Id.ToString()), ("$deck", c.DeckId.ToString()), ("$front", c.Front), ("$back", c.Back),
        ("$created", ToMs(c.CreatedAt)), ("$kind", c.Kind.ToText()), ("$tags", c.TagsText),
        ("$suspended", c.IsSuspended ? 1 : 0), ("$leech", c.IsLeech ? 1 : 0),
        ("$clozeSource", c.ClozeSource), ("$clozeNumber", c.ClozeNumber), ("$clozeExtra", c.ClozeExtra),
        ("$noteId", c.NoteId), ("$noteOrd", c.NoteOrd),
        ("$edited", ToMs(c.EditedAt)), ("$schedChanged", ToMs(c.SchedChangedAt)),
        ("$readOnly", c.FieldsReadOnly ? 1 : 0), ("$phase", c.Phase.ToText()), ("$due", ToMs(c.Due)),
        ("$interval", c.IntervalDays), ("$ease", c.Ease), ("$reps", c.Reps), ("$lapses", c.Lapses),
        ("$step", c.Step), ("$lastReviewed", ToMs(c.LastReviewed)),
        ("$stability", c.Stability), ("$difficulty", c.Difficulty),
    ];

    /// <summary>Turns one row of the cards table into a Card. Column numbers follow CardColumns.</summary>
    private static Card ReadCard(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(0)),
        DeckId = Guid.Parse(r.GetString(1)),
        Front = r.GetString(2),
        Back = r.GetString(3),
        CreatedAt = FromMs(r.GetInt64(4)),
        Kind = CardKindText.ParseKind(r.GetString(5)),
        TagsText = r.GetString(6),
        IsSuspended = r.GetInt64(7) != 0,
        IsLeech = r.GetInt64(8) != 0,
        ClozeSource = r.IsDBNull(9) ? null : r.GetString(9),
        ClozeNumber = r.IsDBNull(10) ? null : r.GetInt32(10),
        ClozeExtra = r.IsDBNull(11) ? null : r.GetString(11),
        NoteId = r.GetString(12),
        NoteOrd = r.GetInt32(13),
        EditedAt = FromMs(r.GetInt64(14)),
        SchedChangedAt = FromMs(r.GetInt64(15)),
        FieldsReadOnly = r.GetInt64(16) != 0,
        Phase = CardPhaseText.ParsePhase(r.GetString(17)),
        Due = FromMs(r.GetInt64(18)),
        IntervalDays = r.GetDouble(19),
        Ease = r.GetDouble(20),
        Reps = r.GetInt32(21),
        Lapses = r.GetInt32(22),
        Step = r.GetInt32(23),
        LastReviewed = FromNullableMs(r, 24),
        Stability = r.IsDBNull(25) ? null : r.GetDouble(25),
        Difficulty = r.IsDBNull(26) ? null : r.GetDouble(26),
    };
}
