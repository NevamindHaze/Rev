using Rev.Core.Models;
using Rev.Core.Scheduling;
using static Rev.Core.Tests.TestData;

namespace Rev.Core.Tests;

public class LeechTests
{
    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    [InlineData(12, true)]
    [InlineData(16, true)]
    public void LeechAt8LapsesThenEvery4(int lapsesAfter, bool expected)
    {
        var before = ReviewState() with { Lapses = lapsesAfter - 1 };
        var after = before with { Lapses = lapsesAfter, Phase = CardPhase.Relearning };
        Assert.Equal(expected, Leech.JustBecameLeech(before, Rating.Again, after));
    }

    [Fact]
    public void OnlyForgettingAGraduatedCardCounts()
    {
        var learning = NewState() with { Phase = CardPhase.Relearning, Lapses = 8 };
        Assert.False(Leech.JustBecameLeech(learning, Rating.Again, learning));
        var review = ReviewState() with { Lapses = 8 };
        Assert.False(Leech.JustBecameLeech(review, Rating.Good, review));
    }

    [Fact]
    public void TheEighthRealLapseWithFsrsMakesALeech()
    {
        var fsrs = new FsrsScheduler();
        var s = ReviewState() with { Lapses = 7 };
        var after = fsrs.Next(s, Rating.Again, Now);
        Assert.True(Leech.JustBecameLeech(s, Rating.Again, after));
    }
}

public class WaitTextTests
{
    [Theory]
    [InlineData(1, "1 d")]
    [InlineData(30, "30 d")]
    [InlineData(45, "1.5 mo")]
    [InlineData(400, "1.1 y")]
    public void DaysAreShownShortly(double days, string expected) =>
        Assert.Equal(expected, WaitText.FormatDays(days));

    [Fact]
    public void LearningStepsShowMinutesOrHours()
    {
        Assert.Equal("1 min", (NewState() with { Phase = CardPhase.Learning, Due = Now.AddSeconds(20) }).WaitTextFrom(Now));
        Assert.Equal("6 min", (NewState() with { Phase = CardPhase.Learning, Due = Now.AddMinutes(5.5) }).WaitTextFrom(Now));
        Assert.Equal("2 h", (NewState() with { Phase = CardPhase.Learning, Due = Now.AddMinutes(150) }).WaitTextFrom(Now));
    }

    [Fact]
    public void NewCardButtonsShowRevsUsualTimes()
    {
        var previews = new FsrsScheduler().Previews(NewState(), Now);
        Assert.Equal("1 min", previews[Rating.Again]);
        Assert.Equal("6 min", previews[Rating.Hard]);
        Assert.Equal("10 min", previews[Rating.Good]);
        Assert.EndsWith(" d", previews[Rating.Easy]);
    }
}

public class CardTests
{
    [Fact]
    public void EveryCardHasANoteIdentity()
    {
        var card = new Card();
        Assert.False(string.IsNullOrEmpty(card.NoteId));
        Assert.Equal($"{card.NoteId}:0", card.SyncKey);
    }

    [Fact]
    public void SchedulingStateRoundTripsAndFuzzSeedIsStable()
    {
        var card = new Card();
        var state = ReviewState() with { Lapses = 3, Ease = 2.1 };
        card.SchedulingState = state;
        Assert.Equal(state with { FuzzSeed = card.SchedulingState.FuzzSeed }, card.SchedulingState);
        Assert.Equal(card.SchedulingState.FuzzSeed, card.SchedulingState.FuzzSeed);
    }

    [Theory]
    [InlineData(CardPhase.New, "new")]
    [InlineData(CardPhase.Relearning, "relearning")]
    public void PhasesUseTheSameWordsAsTheIPhone(CardPhase phase, string text)
    {
        Assert.Equal(text, phase.ToText());
        Assert.Equal(phase, CardPhaseText.ParsePhase(text));
    }

    [Fact]
    public void KindsUseTheSameWordsAsTheIPhone()
    {
        Assert.Equal("typeIn", CardKind.TypeIn.ToText());
        Assert.Equal(CardKind.Reversed, CardKindText.ParseKind("reversed"));
        Assert.Equal(CardKind.Basic, CardKindText.ParseKind("something else"));
    }
}
