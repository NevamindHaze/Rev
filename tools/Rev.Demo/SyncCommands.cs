using System.Text.Json;
using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using Rev.Core.Sync;

/// <summary>Small commands that let a test script use Rev for Windows' real sync on a test data folder.</summary>
internal static class SyncCommands
{
    public static int Run(string[] args)
    {
        string dataFolder = args[0];
        using var db = new RevDatabase(Path.Combine(dataFolder, "rev.db"));
        var now = DateTime.Now;
        Card ByFront(string front) => db.GetCards().Single(c => c.Front == front);

        switch (args[1])
        {
            case "--sync":
                var service = new SyncService(db, args[2], Path.Combine(dataFolder, "sync-state.json"), Path.Combine(dataFolder, "Media"));
                Console.WriteLine(JsonSerializer.Serialize(service.Run(now)));
                return 0;

            case "--edit":
                var card = ByFront(args[2]);
                db.SaveEdit(card, card.Front, args[3], Tags.Parse(card.TagsText), now);
                return 0;

            case "--study":
                var studied = ByFront(args[2]);
                var before = studied.SchedulingState;
                var after = new FsrsScheduler().Next(before with { Phase = CardPhase.Review, IntervalDays = Math.Max(1, before.IntervalDays),
                                                                   Stability = before.Stability ?? 3, Difficulty = before.Difficulty ?? 5 },
                                                     Rating.Good, now);
                studied.SchedulingState = after;
                studied.MarkScheduled(now);
                db.UpdateCard(studied);
                db.AddReviewLog(new ReviewLog { CardId = studied.Id, Date = now, Rating = Rating.Good, IntervalDays = after.IntervalDays });
                return 0;

            case "--add":
                var deck = db.GetDecks().FirstOrDefault(d => d.Name == args[2]) ?? db.AddDeck(args[2], now);
                db.AddNote(deck.Id, CardKind.Basic, args[3], args[4], ["from_windows"], now);
                return 0;

            case "--delete":
                db.DeleteCard(ByFront(args[2]).Id);
                return 0;

            case "--delete-deck":
                db.DeleteDeck(db.GetDecks().Single(d => d.Name == args[2]).Id);
                return 0;

            case "--decks":
                Console.WriteLine(JsonSerializer.Serialize(db.GetDecks().Select(d => d.Name).Order()));
                return 0;

            case "--dump":
                var decks = db.GetDecks().ToDictionary(d => d.Id, d => d.Name);
                Console.WriteLine(JsonSerializer.Serialize(db.GetCards().Select(c => new
                {
                    key = c.SyncKey, deck = decks[c.DeckId], front = c.Front, back = c.Back, tags = c.TagsText,
                    phase = c.Phase.ToText(), interval = c.IntervalDays, suspended = c.IsSuspended,
                    reviews = db.GetReviewLogs().Count(l => l.CardId == c.Id),
                })));
                return 0;
        }
        return 1;
    }
}
