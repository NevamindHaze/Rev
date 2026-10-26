using System.Security.Cryptography;
using System.Text.Json;
using Rev.Core.Anki;
using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Scheduling;

namespace Rev.Core.Sync;

/// <summary>
/// The sync rules on Rev for Windows' side, the same as Rev on iPhone's (Rev/Sync/SyncMerger.swift):
/// - A card is identified by "&lt;note id&gt;:&lt;ord&gt;" in every app.
/// - For each card, text/tags/deck and schedule are merged separately: the newest change wins.
/// - Deletions travel as "tombstones"; a deletion loses if the card was changed after it.
/// - Reviews from every app are kept (duplicates skipped), so stats and streaks include everything.
/// </summary>
public static class SyncMerger
{
    private const double ReviewDays = 60;
    private const double TombstoneDays = 60;

    // ---- Anki → Rev ----

    /// <summary>Applies what changed in Anki (anki.json) to Rev's database.</summary>
    public static SyncReport Apply(RevDatabase db, SyncSnapshot anki, SyncState state, DateTime now)
    {
        int added = 0, updated = 0, rescheduled = 0, deleted = 0, reviews = 0;
        db.InTransaction(() =>
        {
            var cards = db.GetCards();
            var byKey = cards.ToDictionary(c => c.SyncKey);
            var decksByName = db.GetDecks().GroupBy(d => d.Name).ToDictionary(g => g.Key, g => g.First());
            Deck DeckNamed(string ankiName)
            {
                string name = ankiName.Replace("::", " › ");
                if (decksByName.TryGetValue(name, out var deck)) return deck;
                deck = db.AddDeck(name, now);
                decksByName[name] = deck;
                return deck;
            }

            // 1. Cards deleted in Anki (unless changed in Rev afterwards).
            foreach (var tombstone in anki.Deleted)
            {
                if (!byKey.TryGetValue(tombstone.Key, out var card) || LastChange(card) >= tombstone.At) continue;
                db.DeleteCard(card.Id);
                byKey.Remove(tombstone.Key);
                deleted += 1;
            }

            // 2. Changed and new cards.
            var exported = state.ExportedKeys.ToHashSet();
            int order = 0;
            foreach (var remote in anki.Cards)
            {
                if (byKey.TryGetValue(remote.Key, out var card))
                {
                    bool changed = false;
                    if (remote.EditedAt > Seconds(card.EditedAt) + 1)
                    {
                        ApplyContent(remote, card, DeckNamed(remote.Deck));
                        card.EditedAt = FromSeconds(remote.EditedAt);
                        updated += 1;
                        changed = true;
                    }
                    if (remote.SchedAt > Seconds(card.SchedChangedAt) + 1)
                    {
                        ApplySchedule(remote, card);
                        card.SchedChangedAt = FromSeconds(remote.SchedAt);
                        rescheduled += 1;
                        changed = true;
                    }
                    if (changed) db.UpdateCard(card);
                }
                else if (!exported.Contains(remote.Key))
                {
                    // (If Rev sent this key before, it was deleted in Rev; Anki will delete it too.)
                    var created = now.AddMilliseconds(order++);
                    var fresh = new Card
                    {
                        NoteId = remote.NoteId, NoteOrd = remote.Ord, CreatedAt = created, Due = created,
                    };
                    ApplyContent(remote, fresh, DeckNamed(remote.Deck));
                    ApplySchedule(remote, fresh);
                    fresh.EditedAt = FromSeconds(remote.EditedAt);
                    fresh.SchedChangedAt = FromSeconds(remote.SchedAt);
                    db.AddCard(fresh);
                    byKey[remote.Key] = fresh;
                    added += 1;
                }
            }

            // 3. Reviews done in Anki (and in Rev on iPhone, through Anki).
            var cutoff = now.AddDays(-ReviewDays);
            var seen = db.GetReviewLogs(cutoff).Select(l => (l.CardId, RevDatabase.ToMs(l.Date))).ToHashSet();
            foreach (var review in anki.Reviews)
            {
                if (!byKey.TryGetValue(review.Key, out var card) || !seen.Add((card.Id, review.At))) continue;
                db.AddReviewLog(new ReviewLog
                {
                    CardId = card.Id, Date = RevDatabase.FromMs(review.At), WasNew = false,
                    Rating = Enum.IsDefined((Rating)review.Rating) ? (Rating)review.Rating : Rating.Good,
                    IntervalDays = review.Interval,
                });
                reviews += 1;
            }
        });
        return new SyncReport(added, updated, rescheduled, deleted, reviews);
    }

    private static void ApplyContent(SyncCard remote, Card card, Deck deck)
    {
        card.Front = AnkiText.PlainText(remote.Front);
        card.Back = AnkiText.PlainText(remote.Back);
        card.TagsText = Tags.Format(remote.Tags);
        card.IsLeech = remote.Leech;
        switch (remote.Kind)
        {
            case "cloze":
                card.Kind = CardKind.Cloze;
                card.ClozeSource = AnkiText.PlainText(remote.ClozeSource ?? "");
                card.ClozeExtra = AnkiText.PlainText(remote.ClozeExtra ?? "");
                card.ClozeNumber = remote.Ord + 1;
                card.FieldsReadOnly = false;
                break;
            case "basic" or "reversed" or "typeIn":
                card.Kind = CardKindText.ParseKind(remote.Kind);
                card.FieldsReadOnly = false;
                break;
            default:
                // A note type Rev can't rewrite: Rev shows Anki's text, and edits made in Rev stay in Rev.
                card.Kind = CardKind.Basic;
                card.FieldsReadOnly = true;
                break;
        }
        card.DeckId = deck.Id;
    }

    private static void ApplySchedule(SyncCard remote, Card card)
    {
        var s = remote.Sched;
        card.Phase = CardPhaseText.ParsePhase(s.Phase);
        if (card.Phase != CardPhase.New) card.Due = FromSeconds(s.Due);
        card.IntervalDays = s.Interval;
        card.Ease = s.Ease;
        card.Reps = s.Reps;
        card.Lapses = s.Lapses;
        card.Step = 0;
        if (s.Stability is double stability && s.Difficulty is double difficulty)
        {
            card.Stability = stability;
            card.Difficulty = difficulty;
        }
        card.IsSuspended = s.Suspended;
        if (remote.LastReview is double last) card.LastReviewed = FromSeconds(last);
    }

    // ---- Rev → windows.json ----

    /// <summary>Everything Rev for Windows has, for the add-on to read. Also updates the deletion memory in <paramref name="state"/>.</summary>
    public static SyncSnapshot MakeSnapshot(RevDatabase db, SyncState state, DateTime now)
    {
        double nowSeconds = Seconds(now);
        var deckNames = db.GetDecks().ToDictionary(d => d.Id, d => d.Name.Replace(" › ", "::"));
        var cards = db.GetCards().Where(c => deckNames.ContainsKey(c.DeckId)).ToList();
        var keyOfCard = cards.ToDictionary(c => c.Id, c => c.SyncKey);

        var output = cards.Select(card => new SyncCard
        {
            Key = card.SyncKey, NoteId = card.NoteId, Ord = card.NoteOrd, Deck = deckNames[card.DeckId],
            Kind = card.FieldsReadOnly ? "other" : card.Kind.ToText(),
            Front = card.Front, Back = card.Back, Fields = Fields(card),
            Tags = Tags.Parse(card.TagsText), Leech = card.IsLeech,
            EditedAt = Seconds(card.EditedAt),
            Sched = new SyncSchedule
            {
                Phase = card.Phase.ToText(), Due = Seconds(card.Due), Interval = card.IntervalDays, Ease = card.Ease,
                Reps = card.Reps, Lapses = card.Lapses, Stability = card.Stability, Difficulty = card.Difficulty,
                Suspended = card.IsSuspended,
            },
            SchedAt = Seconds(card.SchedChangedAt),
            LastReview = card.LastReviewed is { } last ? Seconds(last) : null,
        }).OrderBy(c => c.Key, StringComparer.Ordinal).ToList();

        // Cards that were sent before but are gone now were deleted in Rev.
        var current = keyOfCard.Values.ToHashSet();
        foreach (var key in state.ExportedKeys)
            if (!current.Contains(key) && !state.Tombstones.ContainsKey(key)) state.Tombstones[key] = nowSeconds;
        state.Tombstones = state.Tombstones
            .Where(t => !current.Contains(t.Key) && t.Value > nowSeconds - TombstoneDays * 86_400)
            .ToDictionary(t => t.Key, t => t.Value);
        state.ExportedKeys = current.Order(StringComparer.Ordinal).ToList();

        var reviews = db.GetReviewLogs(now.AddDays(-ReviewDays))
            .Where(l => keyOfCard.ContainsKey(l.CardId))
            .Select(l => new SyncReview { Key = keyOfCard[l.CardId], At = RevDatabase.ToMs(l.Date), Rating = (int)l.Rating, Interval = l.IntervalDays })
            .ToList();

        return new SyncSnapshot
        {
            Format = 1, Writer = "windows", WrittenAt = 0, Cards = output, Reviews = reviews,
            Deleted = state.Tombstones.OrderBy(t => t.Key, StringComparer.Ordinal)
                .Select(t => new SyncTombstone { Key = t.Key, At = t.Value }).ToList(),
        };
    }

    /// <summary>A fingerprint of the snapshot's content, to skip writing the file when nothing changed.</summary>
    public static string Digest(SyncSnapshot snapshot)
    {
        double written = snapshot.WrittenAt;
        snapshot.WrittenAt = 0;
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        snapshot.WrittenAt = written;
        return Convert.ToHexString(SHA256.HashData(json));
    }

    /// <summary>The Anki fields for a card Rev can edit (the same layout as Rev on iPhone).</summary>
    private static Dictionary<string, string>? Fields(Card card)
    {
        if (card.FieldsReadOnly) return null;
        string Html(string text) => AnkiText.HtmlFromPlain(text);
        return card.Kind switch
        {
            CardKind.Cloze => new() { ["Text"] = Html(card.ClozeSource ?? card.Front), ["Back Extra"] = Html(card.ClozeExtra ?? "") },
            CardKind.Reversed when card.NoteOrd == 1 => new() { ["Front"] = Html(card.Back), ["Back"] = Html(card.Front) },
            _ => new() { ["Front"] = Html(card.Front), ["Back"] = Html(card.Back) },
        };
    }

    // ---- Helpers ----

    /// <summary>When the card last changed in Rev, text or schedule.</summary>
    private static double LastChange(Card card) => Math.Max(Seconds(card.EditedAt), Seconds(card.SchedChangedAt));

    public static double Seconds(DateTime time) => RevDatabase.ToMs(time) / 1000.0;

    public static DateTime FromSeconds(double seconds) => RevDatabase.FromMs((long)Math.Round(seconds * 1000));
}
