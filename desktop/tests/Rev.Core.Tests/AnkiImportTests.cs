using System.IO.Compression;
using Rev.Core.Anki;
using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using Rev.Core.Text;
using static Rev.Core.Tests.TestData;

namespace Rev.Core.Tests;

/// <summary>
/// Reads real Anki files (made by Anki's own library, see tools/make-anki-fixtures.py) in both the
/// older and the newest format. They hold a small vet collection: every card kind, formatting, a list,
/// an image, tags, a leech, a suspended card and a few reviews with FSRS on.
/// </summary>
public sealed class AnkiImportTests : IDisposable
{
    public static TheoryData<string> Files => new() { "vet-new.apkg", "vet-legacy.apkg", "vet-new.colpkg", "vet-legacy.colpkg" };

    private readonly string folder = Path.Combine(Path.GetTempPath(), "rev-tests-" + Guid.NewGuid().ToString("N"));
    private readonly RevDatabase db;
    private string Media => Path.Combine(folder, "Media");

    public AnkiImportTests() => db = new RevDatabase(Path.Combine(folder, "rev.db"));

    public void Dispose()
    {
        db.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private ApkgContents Read(string file) => ApkgReader.Read(Fixture(file), Media, Now);

    private static Card One(ApkgContents contents, string front) =>
        contents.Decks.SelectMany(d => d.Cards).Single(c => c.Front == front);

    [Theory, MemberData(nameof(Files))]
    public void DecksKeepTheirNamesWithSubDecks(string file)
    {
        var contents = Read(file);
        Assert.Equal(["Vet › Parasitology", "Vet › Pharmacology"], contents.Decks.Select(d => d.Name));
        Assert.Equal([3, 7], contents.Decks.Select(d => d.Cards.Count));
    }

    [Theory, MemberData(nameof(Files))]
    public void TextKeepsBoldItalicListsAndImages(string file)
    {
        var contents = Read(file);
        var meloxicam = One(contents, "Class of **meloxicam**?");
        Assert.Equal("NSAID (*COX-2 preferential*)", meloxicam.Back);
        Assert.Equal("paper_1 pharm", meloxicam.TagsText);   // Anki keeps a note's tags in A–Z order
        Assert.Equal(CardKind.Basic, meloxicam.Kind);
        Assert.False(meloxicam.FieldsReadOnly);

        Assert.Equal("• vomiting\n• GI ulcers\n• kidney injury", One(contents, "Signs of NSAID toxicity?").Back);
        // The <br> after the picture leaves a small gap, as in Rev for iPhone.
        Assert.Equal("[img:heart.png]\n\nA heart & lungs", One(contents, "What does this show?").Back);

        Assert.Equal(1, contents.ImagesSaved);
        byte[] png = File.ReadAllBytes(Path.Combine(Media, "heart.png"));
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
    }

    [Theory, MemberData(nameof(Files))]
    public void CardKindsAreRecognised(string file)
    {
        var contents = Read(file);
        var forward = One(contents, "Atipamezole");
        var reverse = One(contents, "Reverses medetomidine");
        Assert.Equal((CardKind.Reversed, 0, "Reverses medetomidine"), (forward.Kind, forward.NoteOrd, forward.Back));
        Assert.Equal((CardKind.Reversed, 1, "Atipamezole"), (reverse.Kind, reverse.NoteOrd, reverse.Back));
        Assert.Equal(forward.NoteId, reverse.NoteId);

        var typeIn = One(contents, "Antidote for paracetamol in cats?");
        Assert.Equal((CardKind.TypeIn, "N-acetylcysteine"), (typeIn.Kind, typeIn.Back));

        var cloze = contents.Decks.SelectMany(d => d.Cards).Where(c => c.Kind == CardKind.Cloze).OrderBy(c => c.NoteOrd).ToList();
        Assert.Equal(2, cloze.Count);
        Assert.Equal(["[...] lives in the liver", "Fasciola hepatica lives in the [organ]"], cloze.Select(c => c.Front));
        Assert.Equal("[Fasciola hepatica] lives in the liver\nIntermediate host: mud snail", cloze[0].Back);
        Assert.Equal([1, 2], cloze.Select(c => c.ClozeNumber!.Value));
        Assert.Equal("{{c1::Fasciola hepatica}} lives in the {{c2::liver::organ}}", cloze[0].ClozeSource);
        Assert.Equal("Intermediate host: mud snail", cloze[0].ClozeExtra);
    }

    [Theory, MemberData(nameof(Files))]
    public void LeechAndSuspendedCardsComeAcross(string file)
    {
        var contents = Read(file);
        var leech = One(contents, "Why avoid ivermectin in Collies?");
        Assert.True(leech.IsLeech);
        Assert.Equal("pharm", leech.TagsText);   // Anki's "leech" tag becomes Rev's leech mark
        Assert.True(One(contents, "Vector of Babesia canis?").IsSuspended);
        Assert.False(One(contents, "Atipamezole").IsSuspended);
    }

    [Theory, MemberData(nameof(Files))]
    public void StudyProgressAndFsrsMemoryComeAcross(string file)
    {
        var cards = Read(file).Decks.SelectMany(d => d.Cards).ToList();
        var reviewed = cards.Where(c => c.Phase == CardPhase.Review).ToList();
        Assert.Equal(3, reviewed.Count);
        Assert.Single(cards, c => c.Phase == CardPhase.Learning);
        Assert.Equal(6, cards.Count(c => c.Phase == CardPhase.New));
        Assert.All(reviewed, c =>
        {
            Assert.NotNull(c.Stability);
            Assert.NotNull(c.Difficulty);
            Assert.NotNull(c.LastReviewed);
            Assert.True(c.IntervalDays >= 1);
            Assert.True(c.Due > DateTime.Now);
            Assert.True(c.Reps >= 1);
        });
    }

    [Theory, MemberData(nameof(Files))]
    public void EveryCardKeepsItsAnkiIdentity(string file)
    {
        var cards = Read(file).Decks.SelectMany(d => d.Cards).ToList();
        Assert.Equal(cards.Count, cards.Select(c => c.SyncKey).Distinct().Count());
        Assert.All(cards, c => Assert.False(Guid.TryParse(c.NoteId, out _)));   // Anki's guid, not a new UUID
    }

    /// <summary>
    /// Anki's own change times for every card, read straight from the database inside the legacy test file
    /// (not through Rev's reader): (note's time, card's time) by "guid:ord". All four test files hold the
    /// same collection, so these are right for each of them.
    /// </summary>
    private Dictionary<string, (DateTime Note, DateTime Card)> AnkiChangeTimes()
    {
        Directory.CreateDirectory(folder);
        string database = Path.Combine(folder, "anki21.sqlite");
        using (var zip = ZipFile.OpenRead(Fixture("vet-legacy.apkg")))
            zip.GetEntry("collection.anki21")!.ExtractToFile(database, overwrite: true);
        var times = new Dictionary<string, (DateTime, DateTime)>();
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT n.guid, c.ord, n.mod, c.mod FROM cards c JOIN notes n ON c.nid = n.id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            times[$"{reader.GetString(0)}:{reader.GetInt32(1)}"] =
                (DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(2)).LocalDateTime,
                 DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(3)).LocalDateTime);
        return times;
    }

    [Theory, MemberData(nameof(Files))]
    public void ImportedCardsKeepAnkisOwnChangeTimes(string file)
    {
        var times = AnkiChangeTimes();
        var cards = Read(file).Decks.SelectMany(d => d.Cards).ToList();
        Assert.Equal(10, cards.Count);
        Assert.All(cards, c =>
        {
            // The text's time is the note's last change in Anki, the schedule's time the card's: never the import time.
            Assert.Equal(times[c.SyncKey].Note, c.EditedAt);
            Assert.Equal(times[c.SyncKey].Card, c.SchedChangedAt);
            Assert.NotEqual(c.CreatedAt, c.EditedAt);
        });
    }

    [Fact]
    public void AReimportPutsBackAnkisTimeButKeepsRevsNewerStudy()
    {
        AnkiImporter.Import(db, Read("vet-new.apkg"), Now);
        var times = AnkiChangeTimes();
        var edited = db.GetCards().Single(c => c.Front == "Signs of NSAID toxicity?");
        db.SaveEdit(edited, edited.Front, "changed in Rev", []);
        Assert.True(db.GetCard(edited.Id)!.EditedAt > times[edited.SyncKey].Note);
        var studied = db.GetCards().Single(c => c.Front == "Atipamezole");
        studied.SchedulingState = ReviewState();
        studied.MarkScheduled(DateTime.Now);
        db.UpdateCard(studied);

        AnkiImporter.Import(db, Read("vet-new.apkg"), Now);

        // Anki's text came back, so the card carries Anki's time again (not "now")...
        var restored = db.GetCard(edited.Id)!;
        Assert.Equal("• vomiting\n• GI ulcers\n• kidney injury", restored.Back);
        Assert.Equal(times[edited.SyncKey].Note, restored.EditedAt);
        // ...while the card studied in Rev keeps Rev's (newer) schedule and its time.
        var stillStudied = db.GetCard(studied.Id)!;
        Assert.Equal(CardPhase.Review, stillStudied.Phase);
        Assert.True(stillStudied.SchedChangedAt > times[studied.SyncKey].Card);
    }

    [Fact]
    public void AMissingAnkiTimeMeansLongAgo() =>
        Assert.Equal(DateTimeOffset.UnixEpoch.LocalDateTime, ApkgReader.AnkiTime(0));

    [Fact]
    public void ImportingTheSameDeckAgainUpdatesInsteadOfDuplicating()
    {
        var first = AnkiImporter.Import(db, Read("vet-new.apkg"), Now);
        Assert.Equal((2, 10, 0, 0, 1), (first.DecksCreated, first.Added, first.Updated, first.Unchanged, first.Images));

        // Study a card and edit another in Rev, then import the same file again.
        var studied = db.GetCards().First(c => c.Front == "Atipamezole");
        studied.SchedulingState = ReviewState() with { Lapses = 4 };
        db.UpdateCard(studied);
        var edited = db.GetCards().First(c => c.Front == "Signs of NSAID toxicity?");
        db.SaveEdit(edited, edited.Front, "changed in Rev", []);

        var again = AnkiImporter.Import(db, Read("vet-new.apkg"), Now);
        Assert.Equal((0, 0, 1, 9), (again.DecksCreated, again.Added, again.Updated, again.Unchanged));
        Assert.Equal(2, db.GetDecks().Count);
        Assert.Equal(10, db.GetCards().Count);
        Assert.Equal(4, db.GetCard(studied.Id)!.Lapses);                    // Rev's progress is kept
        Assert.Equal("• vomiting\n• GI ulcers\n• kidney injury", db.GetCard(edited.Id)!.Back);   // Anki's text comes back
    }

    [Fact]
    public void TheOldAndNewFormatsOfOneCollectionAreTheSameCards()
    {
        AnkiImporter.Import(db, Read("vet-legacy.apkg"), Now);
        var result = AnkiImporter.Import(db, Read("vet-new.colpkg"), Now);
        Assert.Equal((0, 0, 10), (result.Added, result.Updated, result.Unchanged));
    }

    [Fact]
    public void CardsImportedBeforeTheyHadAnIdentityAreMatchedByQuestion()
    {
        // e.g. a card that came from an older Rev backup, with a made-up note id.
        var deck = db.AddDeck("Vet › Pharmacology");
        db.AddCard(new Card { DeckId = deck.Id, Front = "Class of meloxicam?", Back = "old answer", NoteId = "old-id" });

        var result = AnkiImporter.Import(db, Read("vet-new.apkg"), Now);
        Assert.Equal((9, 1), (result.Added, result.Updated));
        Assert.Equal("NSAID (*COX-2 preferential*)", db.GetCards().Single(c => c.NoteId == "old-id").Back);
    }

    [Fact]
    public void ImportedCardsCanBeStudiedAndEdited()
    {
        AnkiImporter.Import(db, Read("vet-new.apkg"), Now);
        var c1 = db.GetCards().Single(c => c.Kind == CardKind.Cloze && c.ClozeNumber == 1);
        db.SaveEdit(c1, "{{c1::Fasciola hepatica}} lives in the {{c2::liver::organ}} of {{c3::sheep}}", "Mud snail", ["parasites"]);
        Assert.Equal(3, db.GetCards().Count(c => c.Kind == CardKind.Cloze));
        Assert.All(db.GetCards().Where(c => c.Kind == CardKind.Cloze), c => Assert.Equal(c1.NoteId, c.NoteId));
    }

    [Fact]
    public void AFileThatIsNotAnAnkiFileIsRefused()
    {
        Directory.CreateDirectory(folder);
        string notAnki = Path.Combine(folder, "notes.apkg");
        File.WriteAllText(notAnki, "hello");
        Assert.Throws<InvalidDataException>(() => ApkgReader.Read(notAnki, Media, Now));
    }

    [Theory]
    [InlineData("<div>one</div><div>two</div>", "one\ntwo")]
    [InlineData("a<br><br>b", "a\n\nb")]
    [InlineData("<ol><li>first</li><li>second</li></ol>", "1. first\n2. second")]
    [InlineData("<b>bold</b> and <strong>strong</strong>", "**bold** and **strong**")]
    [InlineData("caf&eacute; &amp; &#176; &#x2192;", "caf&eacute; & ° →")]
    [InlineData("x [sound:dog.mp3] y", "x  y")]
    [InlineData("<img src=\"my%20pic.jpg\">", "[img:my pic.jpg]")]
    [InlineData("<style>.a{}</style>text", "text")]
    public void AnkiHtmlBecomesRevText(string html, string expected) =>
        Assert.Equal(expected, AnkiText.PlainText(html));

    [Fact]
    public void RevTextBecomesAnkiHtml() =>
        Assert.Equal("<b>Bold</b> &amp; <i>italic</i><div><img src=\"a.png\"></div>line 3<br><br>line 5",
                     AnkiText.HtmlFromPlain("**Bold** & *italic*\n[img:a.png]\nline 3\n\nline 5"));

    [Theory]
    [InlineData("[img:a.png]\nA heart")]
    [InlineData("[img:a.png]\n\nA heart")]
    [InlineData("Look:\n[img:a.png]\n[img:b.png]")]
    [InlineData("• one\n• two\n\n1. first")]
    [InlineData("**bold** and *italic* & <tags>")]
    public void RevTextSurvivesTheTripToAnkiAndBack(string text) =>
        Assert.Equal(text, AnkiText.PlainText(AnkiText.HtmlFromPlain(text)));

    [Fact]
    public void TemplatesShowOptionalPartsOnlyWhenTheFieldHasText()
    {
        var fields = new Dictionary<string, string> { ["Front"] = "Q", ["Hint"] = "", ["Extra"] = "more" };
        Assert.Equal("Q", AnkiText.Render("{{Front}}{{#Hint}} hint: {{Hint}}{{/Hint}}", fields, Cloze.Side.Front, null));
        Assert.Equal("more", AnkiText.Render("{{FrontSide}}<hr id=answer>{{Extra}}", fields, Cloze.Side.Back, null));
        Assert.Equal("Q (no hint)", AnkiText.Render("{{Front}}{{^Hint}} (no hint){{/Hint}}", fields, Cloze.Side.Front, null));
    }
}
