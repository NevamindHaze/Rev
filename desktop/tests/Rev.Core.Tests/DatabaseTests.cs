using Microsoft.Data.Sqlite;
using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using static Rev.Core.Tests.TestData;

namespace Rev.Core.Tests;

/// <summary>Each test gets its own empty database in a temporary folder, deleted afterwards.</summary>
public sealed class DatabaseTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rev-tests-" + Guid.NewGuid().ToString("N"));
    private readonly RevDatabase db;

    public DatabaseTests() => db = new RevDatabase(Path.Combine(folder, "rev.db"));

    public void Dispose()
    {
        db.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    /// <summary>The database keeps times to the millisecond, so compare like with like.</summary>
    private static DateTime Ms(DateTime t) => new(t.Ticks - t.Ticks % TimeSpan.TicksPerMillisecond, t.Kind);

    private Card AddCardTo(Deck deck, Action<Card>? change = null)
    {
        var card = new Card { DeckId = deck.Id, Front = "Q", Back = "A" };
        change?.Invoke(card);
        db.AddCard(card);
        return card;
    }

    [Fact]
    public void DecksAreListedOldestFirst()
    {
        db.AddDeck("Surgery", Now);
        db.AddDeck("  Anatomy  ", Now.AddDays(-1));
        Assert.Equal(["Anatomy", "Surgery"], db.GetDecks().Select(d => d.Name));
    }

    [Fact]
    public void RenamingADeckMarksItsCardsEdited()
    {
        var deck = db.AddDeck("Pharm");
        var card = AddCardTo(deck, c => c.EditedAt = Now.AddDays(-5));

        db.RenameDeck(deck.Id, "Pharmacology", Now);

        Assert.Equal("Pharmacology", db.GetDecks().Single().Name);
        Assert.Equal(Ms(Now), db.GetCard(card.Id)!.EditedAt);
    }

    [Fact]
    public void DeletingADeckDeletesItsCardsButKeepsReviewHistory()
    {
        var keep = db.AddDeck("Keep");
        var gone = db.AddDeck("Gone");
        AddCardTo(keep);
        var card = AddCardTo(gone);
        AddCardTo(gone);
        db.AddReviewLog(new ReviewLog { CardId = card.Id, Date = Now });

        Assert.Equal(2, db.CountCards(gone.Id));
        db.DeleteDeck(gone.Id);

        Assert.Equal(["Keep"], db.GetDecks().Select(d => d.Name));
        Assert.Single(db.GetCards());
        Assert.Equal(1, db.ReviewsToday(Now));
    }

    [Fact]
    public void EveryCardFieldIsSavedAndReadBack()
    {
        var deck = db.AddDeck("Parasitology");
        var card = new Card
        {
            DeckId = deck.Id, Front = "Life cycle of **Toxocara canis**?", Back = "• eggs\n• larvae",
            CreatedAt = Now.AddDays(-3), Kind = CardKind.Cloze, TagsText = "paper_1 nematodes",
            IsSuspended = true, IsLeech = true, ClozeSource = "{{c2::Toxocara}}", ClozeNumber = 2,
            ClozeExtra = "extra", NoteId = "anki-guid-123", NoteOrd = 1, EditedAt = Now.AddHours(-2),
            SchedChangedAt = Now.AddHours(-1), FieldsReadOnly = true, Phase = CardPhase.Relearning,
            Due = Now.AddMinutes(10), IntervalDays = 12, Ease = 2.15, Reps = 9, Lapses = 3, Step = 1,
            LastReviewed = Now, Stability = 4.25, Difficulty = 7.5,
        };
        db.AddCard(card);

        var back = db.GetCard(card.Id)!;
        Assert.Equal(card.Id, back.Id);
        Assert.Equal(card.DeckId, back.DeckId);
        Assert.Equal((card.Front, card.Back, card.Kind, card.TagsText), (back.Front, back.Back, back.Kind, back.TagsText));
        Assert.Equal((true, true, true), (back.IsSuspended, back.IsLeech, back.FieldsReadOnly));
        Assert.Equal((card.ClozeSource, card.ClozeNumber, card.ClozeExtra), (back.ClozeSource, back.ClozeNumber, back.ClozeExtra));
        Assert.Equal(card.SyncKey, back.SyncKey);
        Assert.Equal((Ms(card.CreatedAt), Ms(card.EditedAt), Ms(card.SchedChangedAt)),
                     (back.CreatedAt, back.EditedAt, back.SchedChangedAt));
        Assert.Equal(card.SchedulingState with { Due = Ms(card.Due), LastReviewed = Ms(card.LastReviewed!.Value) },
                     back.SchedulingState);
    }

    [Fact]
    public void UpdatingACardSavesTheChange()
    {
        var card = AddCardTo(db.AddDeck("D"));
        card.Back = "New answer";
        card.SchedulingState = ReviewState();
        db.UpdateCard(card);

        var back = db.GetCard(card.Id)!;
        Assert.Equal("New answer", back.Back);
        Assert.Equal(CardPhase.Review, back.Phase);
        Assert.Equal(10, back.IntervalDays);
    }

    [Fact]
    public void TwoCardsCannotShareAnIdentity()
    {
        var deck = db.AddDeck("D");
        AddCardTo(deck, c => (c.NoteId, c.NoteOrd) = ("note", 0));
        AddCardTo(deck, c => (c.NoteId, c.NoteOrd) = ("note", 1));
        Assert.Throws<SqliteException>(() => AddCardTo(deck, c => (c.NoteId, c.NoteOrd) = ("note", 0)));
    }

    [Fact]
    public void ReviewLogsKeepMillisecondsAndCountFrom4am()
    {
        var card = AddCardTo(db.AddDeck("D"));
        var precise = TodayStart.AddHours(5).AddMilliseconds(123);
        db.AddReviewLog(new ReviewLog { CardId = card.Id, Date = precise, WasNew = true, Rating = Rating.Easy });
        db.AddReviewLog(new ReviewLog { CardId = card.Id, Date = TodayStart.AddMinutes(-1) });   // 3:59 am: yesterday

        Assert.Equal(1, db.ReviewsToday(Now));
        Assert.Equal(1, db.NewCardsStudiedToday(Now));
        Assert.Equal([card.Id], db.CardIdsReviewedToday(Now));
        var log = db.GetReviewLogs(TodayStart).Single();
        Assert.Equal(precise, log.Date);
        Assert.Equal(Rating.Easy, log.Rating);
        Assert.Equal(2, db.GetReviewLogs().Count);
    }

    [Fact]
    public void UndoCanRemoveAReviewLog()
    {
        var log = new ReviewLog { CardId = Guid.NewGuid(), Date = Now };
        db.AddReviewLog(log);
        db.DeleteReviewLog(log.Id);
        Assert.Equal(0, db.ReviewsToday(Now));
    }

    [Fact]
    public void SettingsStartWithRevsDefaultsAndAreSaved()
    {
        Assert.Equal(new AppSettings(), db.GetSettings());
        Assert.Equal(20, db.GetSettings().NewCardsPerDay);

        var mine = new AppSettings { UserName = "Sam", NewCardsPerDay = 35, Scheduler = SchedulerKind.Sm2, DesiredRetention = 0.93 };
        db.SaveSettings(mine);
        Assert.Equal(mine, db.GetSettings());
        Assert.IsType<Sm2Scheduler>(db.GetSettings().CreateScheduler());
    }

    [Fact]
    public void EverythingIsStillThereAfterReopening()
    {
        var deck = db.AddDeck("Physiology");
        AddCardTo(deck);
        db.SaveSettings(new AppSettings { UserName = "Sam" });
        db.Dispose();

        using var reopened = new RevDatabase(Path.Combine(folder, "rev.db"));
        Assert.Equal("Physiology", reopened.GetDecks().Single().Name);
        Assert.Single(reopened.GetCards(deck.Id));
        Assert.Equal("Sam", reopened.GetSettings().UserName);
    }
}
