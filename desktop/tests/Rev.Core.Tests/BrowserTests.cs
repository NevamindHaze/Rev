using Rev.Core.Browser;
using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using static Rev.Core.Tests.TestData;

namespace Rev.Core.Tests;

public class CardQueryTests
{
    private static Card Make(string front, CardPhase phase = CardPhase.New, string tags = "", int ageDays = 0,
                             DateTime? due = null, int lapses = 0, double ease = 2.5, double? difficulty = null,
                             bool leech = false, bool suspended = false, string back = "") => new()
    {
        Front = front, Back = back, Phase = phase, TagsText = tags, CreatedAt = Now.AddDays(-ageDays),
        Due = due ?? Now, Lapses = lapses, Ease = ease, Difficulty = difficulty, IsLeech = leech, IsSuspended = suspended,
    };

    private static List<string> Fronts(IEnumerable<Card> cards) => cards.Select(c => c.Front).ToList();

    [Fact]
    public void SearchLooksInQuestionAnswerAndTagsWithAnyCapitals()
    {
        var cards = new[]
        {
            Make("Meloxicam class?", back: "NSAID"),
            Make("Toxocara host?", tags: "Parasitology"),
            Make("Atipamezole?", back: "reverses MEDETOMIDINE"),
        };
        Assert.Equal(["Meloxicam class?"], Fronts(CardQuery.Apply(cards, "nsaid", CardStateFilter.All, null, CardSort.Oldest, Now)));
        Assert.Equal(["Toxocara host?"], Fronts(CardQuery.Apply(cards, "PARASIT", CardStateFilter.All, null, CardSort.Oldest, Now)));
        Assert.Equal(["Atipamezole?"], Fronts(CardQuery.Apply(cards, " medetomidine ", CardStateFilter.All, null, CardSort.Oldest, Now)));
        Assert.Equal(3, CardQuery.Apply(cards, "", CardStateFilter.All, null, CardSort.Oldest, Now).Count);
    }

    [Fact]
    public void StateFiltersPickTheRightCards()
    {
        var cards = new[]
        {
            Make("new"),
            Make("learning", CardPhase.Learning, due: Now.AddMinutes(5)),
            Make("relearning", CardPhase.Relearning, due: Now.AddMinutes(5)),
            Make("due today", CardPhase.Review, due: TodayStart),
            Make("due later", CardPhase.Review, due: TodayStart.AddDays(4)),
            Make("leech", CardPhase.Review, due: TodayStart.AddDays(4), leech: true),
            Make("suspended", CardPhase.Review, due: TodayStart, suspended: true),
        };
        List<string> Run(CardStateFilter f) => Fronts(CardQuery.Apply(cards, "", f, null, CardSort.Oldest, Now));

        Assert.Equal(7, Run(CardStateFilter.All).Count);
        Assert.Equal(["new"], Run(CardStateFilter.New));
        Assert.Equal(["learning", "relearning"], Run(CardStateFilter.Learning));
        Assert.Equal(["due today", "due later", "leech", "suspended"], Run(CardStateFilter.Review));
        Assert.Equal(["learning", "relearning", "due today"], Run(CardStateFilter.DueToday));   // not the suspended one
        Assert.Equal(["leech"], Run(CardStateFilter.Leeches));
        Assert.Equal(["suspended"], Run(CardStateFilter.Suspended));
    }

    [Fact]
    public void TagFilterMatchesWholeTagsWithAnyCapitals()
    {
        var cards = new[] { Make("a", tags: "Pharm paper_1"), Make("b", tags: "pharmacology"), Make("c") };
        Assert.Equal(["a"], Fronts(CardQuery.Apply(cards, "", CardStateFilter.All, "pharm", CardSort.Oldest, Now)));
        Assert.Equal(["paper_1", "Pharm", "pharmacology"], CardQuery.AllTags(cards.Append(Make("d", tags: "PHARM"))));
    }

    [Fact]
    public void SortOrders()
    {
        var cards = new[]
        {
            Make("**banana**", ageDays: 1, due: Now.AddDays(5), lapses: 2, ease: 2.5),
            Make("apple", ageDays: 3, due: Now.AddDays(1), lapses: 0, ease: 1.3),
            Make("Cherry", ageDays: 2, due: Now.AddDays(9), lapses: 7, difficulty: 6),
        };
        List<string> Run(CardSort s) => Fronts(CardQuery.Apply(cards, "", CardStateFilter.All, null, s, Now));

        Assert.Equal(["**banana**", "Cherry", "apple"], Run(CardSort.Newest));
        Assert.Equal(["apple", "Cherry", "**banana**"], Run(CardSort.Oldest));
        Assert.Equal(["apple", "**banana**", "Cherry"], Run(CardSort.DueSoonest));
        Assert.Equal(["apple", "**banana**", "Cherry"], Run(CardSort.AToZ));          // formatting marks don't count
        Assert.Equal(["Cherry", "**banana**", "apple"], Run(CardSort.MostForgotten));
        Assert.Equal(["apple", "Cherry", "**banana**"], Run(CardSort.Hardest));       // ease 1.3 ≈ 9.8 > 6 > ease 2.5 = 5
    }

    [Fact]
    public void StateBadgesSayWhenTheCardIsDue()
    {
        Assert.Equal("New", CardQuery.StateText(Make("x"), Now));
        Assert.Equal("Learning", CardQuery.StateText(Make("x", CardPhase.Relearning), Now));
        Assert.Equal("Due today", CardQuery.StateText(Make("x", CardPhase.Review, due: TodayStart), Now));
        Assert.Equal("Due tomorrow", CardQuery.StateText(Make("x", CardPhase.Review, due: TodayStart.AddDays(1)), Now));
        Assert.Equal("Due in 5 days", CardQuery.StateText(Make("x", CardPhase.Review, due: TodayStart.AddDays(5)), Now));
        Assert.Equal("Due yesterday", CardQuery.StateText(Make("x", CardPhase.Review, due: TodayStart.AddDays(-1)), Now));
        Assert.Equal("Due 3 days ago", CardQuery.StateText(Make("x", CardPhase.Review, due: TodayStart.AddDays(-3)), Now));
    }
}

public sealed class CardActionTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rev-tests-" + Guid.NewGuid().ToString("N"));
    private readonly RevDatabase db;
    private readonly Card card;

    public CardActionTests()
    {
        db = new RevDatabase(Path.Combine(folder, "rev.db"));
        var deck = db.AddDeck("D");
        db.AddNote(deck.Id, CardKind.Reversed, "Q", "A", ["pharm"], Now.AddDays(-3));
        card = db.GetCards().First();
    }

    public void Dispose()
    {
        db.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void TagsCanBeAddedAndRemoved()
    {
        db.AddTags(card.Id, ["high_yield", "PHARM"], Now);
        var tagged = db.GetCard(card.Id)!;
        Assert.Equal("pharm high_yield", tagged.TagsText);
        Assert.Equal(Now, tagged.EditedAt);

        db.RemoveTag(card.Id, "Pharm", Now);
        Assert.Equal("high_yield", db.GetCard(card.Id)!.TagsText);
    }

    [Fact]
    public void SuspendingChangesTheScheduleTime()
    {
        db.SetSuspended(card.Id, true, Now);
        var suspended = db.GetCard(card.Id)!;
        Assert.True(suspended.IsSuspended);
        Assert.Equal(Now, suspended.SchedChangedAt);

        db.SetSuspended(card.Id, false, Now.AddMinutes(1));
        Assert.False(db.GetCard(card.Id)!.IsSuspended);
    }

    [Fact]
    public void TheLeechMarkCanBeCleared()
    {
        card.IsLeech = true;
        db.UpdateCard(card);
        db.ClearLeech(card.Id, Now);
        Assert.False(db.GetCard(card.Id)!.IsLeech);
    }

    [Fact]
    public void SiblingsAreCounted() => Assert.Equal(1, db.CountSiblings(card));
}
