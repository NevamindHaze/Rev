using System.Text.Json;
using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Scheduling;

namespace Rev.Core.Backup;

/// <summary>Makes JSON backups and restores them.</summary>
public static class BackupService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        // Leave out empty optional fields, like Rev for iPhone does.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // ---- Making a backup ----

    /// <summary>Everything in Rev: decks with their cards, every review, and your settings.</summary>
    public static BackupFile Make(RevDatabase db, DateTime? now = null)
    {
        var settings = db.GetSettings();
        var cardsByDeck = db.GetCards().ToLookup(c => c.DeckId);
        return new BackupFile
        {
            FormatVersion = 1,
            ExportedAt = Seconds(now ?? DateTime.Now),
            NewCardsPerDay = settings.NewCardsPerDay,
            UserName = settings.UserName,
            Scheduler = settings.Scheduler == SchedulerKind.Sm2 ? "sm2" : "fsrs",
            DesiredRetention = settings.DesiredRetention,
            Decks = db.GetDecks().Select(d => new DeckDto
            {
                Id = d.Id, Name = d.Name, CreatedAt = Seconds(d.CreatedAt),
                Cards = cardsByDeck[d.Id].Select(ToDto).ToList(),
            }).ToList(),
            ReviewLogs = db.GetReviewLogs().Select(l => new ReviewLogDto
            {
                Id = l.Id, CardId = l.CardId, Date = Seconds(l.Date), Rating = (int)l.Rating,
                WasNew = l.WasNew, IntervalDays = l.IntervalDays,
            }).ToList(),
        };
    }

    public static void Save(BackupFile backup, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(backup, Json));

    /// <summary>Reads a backup file. Throws a friendly error if it isn't one.</summary>
    public static BackupFile Load(string path)
    {
        BackupFile? backup;
        try
        {
            backup = JsonSerializer.Deserialize<BackupFile>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("This doesn't look like a Rev backup file.");
        }
        if (backup is null || backup.FormatVersion < 1)
            throw new InvalidDataException("This doesn't look like a Rev backup file.");
        if (backup.FormatVersion > 1)
            throw new InvalidDataException("This backup was made by a newer version of Rev. Please update Rev first.");
        return backup;
    }

    // ---- Restoring ----

    /// <summary>A file name like "Rev backup 2026-10-26 2015.json".</summary>
    public static string SuggestedFileName(DateTime now) => $"Rev backup {now:yyyy-MM-dd HHmm}.json";

    /// <summary>
    /// Replaces everything in Rev with the backup. First, a copy of what's there now is saved in
    /// <paramref name="safetyFolder"/>, so even a mistaken restore can be undone. Returns that copy's path.
    /// If anything goes wrong, nothing is changed (it all happens in one transaction).
    /// </summary>
    public static string Restore(RevDatabase db, BackupFile backup, string safetyFolder, DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        Directory.CreateDirectory(safetyFolder);
        string safetyCopy = Path.Combine(safetyFolder, $"Before restore {at:yyyy-MM-dd HHmmss}.json");
        Save(Make(db, at), safetyCopy);

        var (decks, cards) = Convert(backup, at);
        var settings = db.GetSettings();
        db.InTransaction(() =>
        {
            db.DeleteEverything();
            foreach (var deck in decks) db.AddDeck(deck);
            foreach (var card in cards) db.AddCard(card);
            foreach (var log in backup.ReviewLogs)
                db.AddReviewLog(new ReviewLog
                {
                    Id = log.Id ?? Guid.NewGuid(), CardId = log.CardId, Date = FromSeconds(log.Date),
                    Rating = (Rating)Math.Clamp(log.Rating, 1, 4), WasNew = log.WasNew, IntervalDays = log.IntervalDays,
                });
            db.SaveSettings(settings with
            {
                UserName = backup.UserName ?? settings.UserName,
                NewCardsPerDay = backup.NewCardsPerDay ?? settings.NewCardsPerDay,
                Scheduler = backup.Scheduler switch { "sm2" => SchedulerKind.Sm2, "fsrs" => SchedulerKind.Fsrs, _ => settings.Scheduler },
                DesiredRetention = backup.DesiredRetention is double r
                    ? Math.Clamp(r, SchedulerFactory.MinimumRetention, SchedulerFactory.MaximumRetention)
                    : settings.DesiredRetention,
            });
        });
        return safetyCopy;
    }

    /// <summary>
    /// Turns the backup into decks and cards. Older iPhone backups may lack a card's note id or ord;
    /// those get one, and no two cards end up with the same "&lt;note id&gt;:&lt;ord&gt;" identity.
    /// </summary>
    private static (List<Deck> Decks, List<Card> Cards) Convert(BackupFile backup, DateTime now)
    {
        var decks = new List<Deck>();
        var cards = new List<Card>();
        var usedKeys = new HashSet<string>();
        foreach (var d in backup.Decks)
        {
            var deck = new Deck { Id = d.Id, Name = d.Name, CreatedAt = FromSeconds(d.CreatedAt) };
            decks.Add(deck);
            foreach (var c in d.Cards)
            {
                var card = FromDto(c, deck.Id, now);
                int ord = c.NoteOrd ?? (c.ClozeNumber is int n ? n - 1 : 0);
                while (!usedKeys.Add($"{card.NoteId}:{ord}")) ord += 1;
                card.NoteOrd = ord;
                cards.Add(card);
            }
        }
        return (decks, cards);
    }

    private static CardDto ToDto(Card c) => new()
    {
        Id = c.Id, Front = c.Front, Back = c.Back, CreatedAt = Seconds(c.CreatedAt), Phase = c.Phase.ToText(),
        Due = Seconds(c.Due), IntervalDays = c.IntervalDays, Ease = c.Ease, Reps = c.Reps, Lapses = c.Lapses,
        Step = c.Step, LastReviewed = c.LastReviewed is { } last ? Seconds(last) : null,
        Stability = c.Stability, Difficulty = c.Difficulty, NoteId = c.NoteId, IsSuspended = c.IsSuspended,
        IsLeech = c.IsLeech, Tags = c.TagsText, Kind = c.Kind.ToText(), ClozeSource = c.ClozeSource,
        ClozeNumber = c.ClozeNumber, ClozeExtra = c.ClozeExtra, NoteOrd = c.NoteOrd,
        EditedAt = Seconds(c.EditedAt), SchedChangedAt = Seconds(c.SchedChangedAt), FieldsReadOnly = c.FieldsReadOnly,
    };

    private static Card FromDto(CardDto c, Guid deckId, DateTime now)
    {
        var created = FromSeconds(c.CreatedAt);
        return new Card
        {
            Id = c.Id, DeckId = deckId, Front = c.Front, Back = c.Back, CreatedAt = created,
            Phase = CardPhaseText.ParsePhase(c.Phase), Due = FromSeconds(c.Due), IntervalDays = c.IntervalDays,
            Ease = c.Ease, Reps = c.Reps, Lapses = c.Lapses, Step = c.Step,
            LastReviewed = c.LastReviewed is double last ? FromSeconds(last) : null,
            Stability = c.Stability, Difficulty = c.Difficulty,
            NoteId = string.IsNullOrEmpty(c.NoteId) ? Guid.NewGuid().ToString().ToUpperInvariant() : c.NoteId,
            IsSuspended = c.IsSuspended ?? false, IsLeech = c.IsLeech ?? false, TagsText = c.Tags ?? "",
            Kind = CardKindText.ParseKind(c.Kind), ClozeSource = c.ClozeSource, ClozeNumber = c.ClozeNumber,
            ClozeExtra = c.ClozeExtra,
            // Cards from old backups without these times count as changed when they were made.
            EditedAt = c.EditedAt is double e ? FromSeconds(e) : created,
            SchedChangedAt = c.SchedChangedAt is double s ? FromSeconds(s) : created,
            FieldsReadOnly = c.FieldsReadOnly ?? false,
        };
    }

    // ---- Times: seconds since 1970, kept to the millisecond ----

    public static double Seconds(DateTime time) => RevDatabase.ToMs(time) / 1000.0;

    public static DateTime FromSeconds(double seconds) => RevDatabase.FromMs((long)Math.Round(seconds * 1000));
}
