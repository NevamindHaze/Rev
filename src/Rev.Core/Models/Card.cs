using Rev.Core.Scheduling;

namespace Rev.Core.Models;

/// <summary>
/// One flashcard: a front (question) and a back (answer),
/// plus the information the scheduler needs to decide when to show it next.
/// </summary>
public sealed class Card
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeckId { get; set; }
    public string Front { get; set; } = "";
    public string Back { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public CardKind Kind { get; set; } = CardKind.Basic;
    /// <summary>Tags separated by spaces, like Anki: "parasitology paper_1".</summary>
    public string TagsText { get; set; } = "";
    /// <summary>Suspended cards are never shown in reviews until you unsuspend them.</summary>
    public bool IsSuspended { get; set; }
    /// <summary>A "leech" is a card you keep forgetting (8 lapses). It usually needs rewriting.</summary>
    public bool IsLeech { get; set; }

    // Cloze cards only: the full text with {{c1::...}} markers, which cloze number this card is,
    // and the optional extra text shown on the back.
    public string? ClozeSource { get; set; }
    public int? ClozeNumber { get; set; }
    public string? ClozeExtra { get; set; }

    // ---- Identity (shared with Rev on iPhone and Anki) ----

    /// <summary>
    /// Cards made from the same note (e.g. c1 and c2 of one cloze, or a card and its reverse) share this.
    /// For cards imported from Anki it's Anki's note guid; for notes made in Rev it's a new UUID.
    /// After you study one card of a note, the others wait until tomorrow ("burying").
    /// </summary>
    public string NoteId { get; set; } = Guid.NewGuid().ToString().ToUpperInvariant();
    /// <summary>
    /// Which card of its note this is (Anki's "ord"): 0 for the first card, 1 for a reversed card,
    /// cloze number − 1 for cloze cards.
    /// </summary>
    public int NoteOrd { get; set; }
    /// <summary>The card's identity in every app: "&lt;note id&gt;:&lt;ord&gt;".</summary>
    public string SyncKey => $"{NoteId}:{NoteOrd}";

    /// <summary>When the text, tags or deck last changed.</summary>
    public DateTime EditedAt { get; set; } = DateTime.Now;
    /// <summary>When the schedule (or suspended state) last changed.</summary>
    public DateTime SchedChangedAt { get; set; } = DateTime.Now;
    /// <summary>True for cards from Anki note types Rev can't rewrite; text edits then stay in Rev.</summary>
    public bool FieldsReadOnly { get; set; }

    // ---- Scheduling (updated every time you answer the card) ----

    public CardPhase Phase { get; set; } = CardPhase.New;
    public DateTime Due { get; set; } = DateTime.Now;
    public double IntervalDays { get; set; }
    public double Ease { get; set; } = 2.5;
    public int Reps { get; set; }
    public int Lapses { get; set; }
    public int Step { get; set; }
    public DateTime? LastReviewed { get; set; }
    // Used by FSRS. Not used by SM-2.
    public double? Stability { get; set; }
    public double? Difficulty { get; set; }

    /// <summary>Call after changing text, tags or deck, so sync knows this is the newest version.</summary>
    public void MarkEdited(DateTime? now = null) => EditedAt = now ?? DateTime.Now;

    /// <summary>Call after answering, suspending or unsuspending, so sync knows the schedule is newest.</summary>
    public void MarkScheduled(DateTime? now = null) => SchedChangedAt = now ?? DateTime.Now;

    /// <summary>
    /// All the scheduling fields bundled together, so the algorithm doesn't need to know about cards at all.
    /// </summary>
    public SchedulingState SchedulingState
    {
        get => new()
        {
            Phase = Phase, Due = Due, IntervalDays = IntervalDays, Ease = Ease, Reps = Reps,
            Lapses = Lapses, Step = Step, LastReviewed = LastReviewed,
            Stability = Stability, Difficulty = Difficulty,
            // The same card with the same number of answers always gets the same fuzz,
            // so the time shown on a button is exactly what you get.
            FuzzSeed = BitConverter.ToInt64(Id.ToByteArray(), 0) + Reps,
        };
        set
        {
            Phase = value.Phase;
            Due = value.Due;
            IntervalDays = value.IntervalDays;
            Ease = value.Ease;
            Reps = value.Reps;
            Lapses = value.Lapses;
            Step = value.Step;
            LastReviewed = value.LastReviewed;
            Stability = value.Stability;
            Difficulty = value.Difficulty;
        }
    }
}
