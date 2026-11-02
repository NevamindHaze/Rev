using Rev.Core.Models;
using Rev.Core.Scheduling;

namespace Rev.Core.Data;

// Reading and writing review history.
public sealed partial class RevDatabase
{
    public void AddReviewLog(ReviewLog log) =>
        Execute("""
            INSERT INTO review_logs (id, card_id, reviewed_at, rating, was_new, interval_days)
            VALUES ($id, $card, $at, $rating, $new, $interval)
            """,
            ("$id", log.Id.ToString()), ("$card", log.CardId.ToString()), ("$at", ToMs(log.Date)),
            ("$rating", (int)log.Rating), ("$new", log.WasNew ? 1 : 0), ("$interval", log.IntervalDays));

    /// <summary>Removes one answer (used by Undo).</summary>
    public void DeleteReviewLog(Guid logId) =>
        Execute("DELETE FROM review_logs WHERE id = $id", ("$id", logId.ToString()));

    /// <summary>Every answer given since <paramref name="since"/>, oldest first.</summary>
    public List<ReviewLog> GetReviewLogs(DateTime? since = null) => Query(
        "SELECT id, card_id, reviewed_at, rating, was_new, interval_days FROM review_logs " +
        "WHERE reviewed_at >= $since ORDER BY reviewed_at",
        r => new ReviewLog
        {
            Id = Guid.Parse(r.GetString(0)),
            CardId = Guid.Parse(r.GetString(1)),
            Date = FromMs(r.GetInt64(2)),
            Rating = (Rating)r.GetInt32(3),
            WasNew = r.GetInt64(4) != 0,
            IntervalDays = r.GetDouble(5),
        },
        ("$since", since is { } s ? ToMs(s) : long.MinValue));

    /// <summary>How many answers you gave today (since 4 am).</summary>
    public int ReviewsToday(DateTime now) =>
        (int)Scalar<long>("SELECT COUNT(*) FROM review_logs WHERE reviewed_at >= $start",
            ("$start", ToMs(now.StartOfStudyDay())));

    /// <summary>New cards studied today; used for the daily new-card limit.</summary>
    public int NewCardsStudiedToday(DateTime now) =>
        (int)Scalar<long>("SELECT COUNT(*) FROM review_logs WHERE reviewed_at >= $start AND was_new = 1",
            ("$start", ToMs(now.StartOfStudyDay())));

    /// <summary>The ids of every card you answered today (for sibling burying).</summary>
    public HashSet<Guid> CardIdsReviewedToday(DateTime now) => Query(
        "SELECT DISTINCT card_id FROM review_logs WHERE reviewed_at >= $start",
        r => Guid.Parse(r.GetString(0)),
        ("$start", ToMs(now.StartOfStudyDay()))).ToHashSet();
}
