using Rev.Core.Models;

namespace Rev.Core.Data;

// The quick actions in the card browser. Each one marks the card as changed, so sync will know:
// tags and the leech mark are content (EditedAt); suspending is part of the schedule (SchedChangedAt).
public sealed partial class RevDatabase
{
    public void AddTags(Guid cardId, IEnumerable<string> tags, DateTime? now = null) =>
        ChangeCard(cardId, card =>
        {
            card.TagsText = Tags.Format(Tags.Parse(card.TagsText).Concat(tags));
            card.MarkEdited(now);
        });

    public void RemoveTag(Guid cardId, string tag, DateTime? now = null) =>
        ChangeCard(cardId, card =>
        {
            card.TagsText = Tags.Format(Tags.Parse(card.TagsText).Where(t => !t.Equals(tag, StringComparison.OrdinalIgnoreCase)));
            card.MarkEdited(now);
        });

    public void SetSuspended(Guid cardId, bool suspended, DateTime? now = null) =>
        ChangeCard(cardId, card =>
        {
            card.IsSuspended = suspended;
            card.MarkScheduled(now);
        });

    /// <summary>"Not a leech any more": e.g. after you've rewritten the card.</summary>
    public void ClearLeech(Guid cardId, DateTime? now = null) =>
        ChangeCard(cardId, card =>
        {
            card.IsLeech = false;
            card.MarkEdited(now);
        });

    /// <summary>How many other cards were made from the same note (e.g. the other half of a reversed pair).</summary>
    public int CountSiblings(Card card) =>
        (int)Scalar<long>("SELECT COUNT(*) FROM cards WHERE note_id = $note AND id <> $id",
            ("$note", card.NoteId), ("$id", card.Id.ToString()));

    private void ChangeCard(Guid cardId, Action<Card> change)
    {
        if (GetCard(cardId) is not { } card) return;
        change(card);
        UpdateCard(card);
    }
}
