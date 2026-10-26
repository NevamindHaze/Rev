using Rev.Core.Models;
using Rev.Core.Text;

namespace Rev.Core.Tests;

public class ClozeTests
{
    [Fact]
    public void NumbersAreFoundOnceEachInOrder() =>
        Assert.Equal([1, 3], Cloze.Numbers("{{c3::b}} {{c1::a}} {{C1::c}}"));

    [Fact]
    public void NextNumberFollowsTheHighest()
    {
        Assert.Equal(1, Cloze.NextNumber("no clozes"));
        Assert.Equal(4, Cloze.NextNumber("{{c1::a}} {{c3::b}}"));
    }

    [Fact]
    public void OnlyThisCardsNumberIsHidden()
    {
        const string text = "{{c1::Toxocara}} is spread by {{c2::eggs::stage}}";
        Assert.Equal("[...] is spread by eggs", Cloze.Render(text, 1, Cloze.Side.Front));
        Assert.Equal("[Toxocara] is spread by eggs", Cloze.Render(text, 1, Cloze.Side.Back));
        Assert.Equal("Toxocara is spread by [stage]", Cloze.Render(text, 2, Cloze.Side.Front));
        Assert.Equal("Toxocara is spread by [eggs]", Cloze.Render(text, 2, Cloze.Side.Back));
    }

    [Fact]
    public void HideWrapsTheFirstWordNotAlreadyHidden()
    {
        string text = "Dogs and dogs";
        text = Cloze.Hide(text, "dogs", 1)!;
        Assert.Equal("{{c1::Dogs}} and dogs", text);
        text = Cloze.Hide(text, " dogs ", 2)!;
        Assert.Equal("{{c1::Dogs}} and {{c2::dogs}}", text);
        Assert.Null(Cloze.Hide(text, "cats", 3));
        Assert.Null(Cloze.Hide(text, "  ", 3));
    }
}

public class TextToolsTests
{
    [Fact]
    public void BoldWrapsTheSelectionAndKeepsItSelected()
    {
        var (text, start, length) = TextTools.ToggleFormat("the heart rate", "**", 4, 5);
        Assert.Equal("the **heart** rate", text);
        Assert.Equal("heart", text.Substring(start, length));
    }

    [Fact]
    public void PressingBoldAgainRemovesIt()
    {
        var (text, start, length) = TextTools.ToggleFormat("the **heart** rate", "**", 6, 5);
        Assert.Equal("the heart rate", text);
        Assert.Equal((4, 5), (start, length));
    }

    [Fact]
    public void ItalicUsesOneStar() =>
        Assert.Equal("*Fasciola* hepatica", TextTools.ToggleFormat("Fasciola hepatica", "*", 0, 8).Text);

    [Theory]
    [InlineData("Atipamezole", "atipamezole", true)]
    [InlineData("  atipamezole. ", "Atipamezole", true)]
    [InlineData("60 – 160   bpm", "60 – 160 bpm.", true)]
    [InlineData("**Atipamezole**", "Atipamezole", true)]
    [InlineData("Atipamezol", "Atipamezole", false)]
    [InlineData("", "Atipamezole", false)]
    public void TypedAnswersIgnoreCaseSpacesAndAFinalFullStop(string typed, string answer, bool expected) =>
        Assert.Equal(expected, TextTools.AnswerMatches(typed, answer));

    [Fact]
    public void PastedLinesBecomeCards()
    {
        var pairs = TextTools.ParsePasted(
            "Normal canine heart rate | 60–160 bpm\r\n" +
            "no separator here\n" +
            "\n" +
            " | missing front\n" +
            "Cat heart rate\t140–220 bpm\n" +
            "Pipe in the back | a | b\n");
        Assert.Equal(
            [("Normal canine heart rate", "60–160 bpm"), ("Cat heart rate", "140–220 bpm"), ("Pipe in the back", "a | b")],
            pairs);
    }
}

public class CardTextTests
{
    [Fact]
    public void LinesKnowListsImagesAndBlanks()
    {
        var lines = CardText.Lines("Signs:\n• fever\n2. cough\n\n[img:lung.png]");
        Assert.Equal(new CardLine[]
        {
            new CardLine.Text("Signs:"),
            new CardLine.ListItem("•", "fever"),
            new CardLine.ListItem("2.", "cough"),
            new CardLine.Blank(),
            new CardLine.Image("lung.png"),
        }, lines);
        Assert.Equal(["lung.png"], CardText.ImageNames("a\n[img:lung.png]"));
    }

    [Fact]
    public void BoldAndItalicBecomeSpans()
    {
        var spans = CardText.Spans("Give **meloxicam *once* daily** with *food*");
        Assert.Equal(new[]
        {
            new TextSpan("Give ", false, false),
            new TextSpan("meloxicam ", true, false),
            new TextSpan("once", true, true),
            new TextSpan(" daily", true, false),
            new TextSpan(" with ", false, false),
            new TextSpan("food", false, true),
        }, spans);
    }

    [Fact]
    public void LoneStarsStayAsText() =>
        Assert.Equal([new TextSpan("2 * 3 = 6", false, false)], CardText.Spans("2 * 3 = 6"));

    [Fact]
    public void PreviewIsOneLine() =>
        Assert.Equal("Signs:  • fever  🖼", CardText.Preview("Signs:\n• fever\n[img:lung.png]"));
}

public class TagsTests
{
    [Fact]
    public void TagsAreSplitOnSpacesAndCommasWithoutDuplicates() =>
        Assert.Equal(["Paper_1", "parasites"], Tags.Parse("Paper_1, parasites  paper_1"));

    [Fact]
    public void SuggestionsAreTheMostUsedTagsNotAlreadyChosen()
    {
        var cards = new[]
        {
            new Card { TagsText = "pharm paper_1" },
            new Card { TagsText = "pharm paper_2" },
            new Card { TagsText = "Pharm surgery" },
        };
        Assert.Equal(["pharm", "paper_1", "paper_2", "surgery"], Tags.Suggestions(cards, ""));
        Assert.Equal(["paper_1", "paper_2"], Tags.Suggestions(cards, "PHARM surgery"));
        Assert.Single(Tags.Suggestions(cards, "", count: 1));
    }
}
