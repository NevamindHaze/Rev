// Makes a database full of sample veterinary decks, for trying Rev out and for screenshots.
// It never touches your real data: you give it an empty folder, and it refuses to overwrite a database.
//
//   dotnet run --project tools/Rev.Demo -- C:\Temp\rev-demo
//
// Then start Rev with that folder:
//   $env:REV_DATA_DIR = "C:\Temp\rev-demo"; dotnet run --project src/Rev.App

using Rev.Core.Anki;
using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Scheduling;

//   --review-test  makes a small "Review test" deck instead, with one card of each special case
//                  (an almost-leech, a list with bold text, an image, a type-in card and a plain card).
//   --export       also adds a card with a picture and writes the decks to <folder>\export.apkg
//                  (used to check that Anki accepts Rev's export: see tools/check-anki-export.py).

//
// Commands for testing sync on an existing (test) data folder, used by tools/check-sync.py:
//   --sync <shared folder>            one sync, as the app does it (prints the summary)
//   --edit <front> <new back>         edit a card's answer
//   --study <front>                   answer a card "Good" as a graduated card, with a review in the history
//   --add <deck> <front> <back>       add a basic card
//   --delete <front>                  delete a card
//   --dump                            print every card as JSON

if (args.Length >= 2 && args[1] is "--sync" or "--edit" or "--study" or "--add" or "--delete" or "--delete-deck" or "--decks" or "--dump")
    return SyncCommands.Run(args);

if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] is not ("--review-test" or "--export")))
{
    Console.WriteLine("Usage: Rev.Demo <empty folder> [--review-test | --export]");
    return 1;
}

string path = Path.Combine(args[0], "rev.db");
if (File.Exists(path))
{
    Console.WriteLine($"{path} already exists; not touching it.");
    return 1;
}

using var db = new RevDatabase(path);
var now = DateTime.Now;
var today = now.StartOfStudyDay();
var random = new Random(7);
db.SaveSettings(new AppSettings { UserName = "Sam" });

if (args.Length == 2 && args[1] == "--review-test")
{
    var test = db.AddDeck("Review test", now.AddDays(-10));
    // A graduated card forgotten 7 times already: one more "Again" makes it a leech.
    var leech = new Card { DeckId = test.Id, Front = "Leech test card", Back = "Forgotten again", CreatedAt = now.AddDays(-9) };
    leech.SchedulingState = new SchedulingState
    {
        Phase = CardPhase.Review, IntervalDays = 2, Ease = 1.3, Reps = 20, Lapses = 7, Stability = 2, Difficulty = 9,
        Due = today, LastReviewed = today.AddDays(-2),
    };
    db.AddCard(leech);
    // New cards, in this order.
    db.AddNote(test.Id, CardKind.Basic, "Signs of **canine parvovirus**?",
        "• vomiting\n• bloody diarrhoea\n• lethargy that goes on for long enough to wrap onto a second line of the answer\n\nIt's *highly* contagious.",
        [], now.AddDays(-8));
    db.AddNote(test.Id, CardKind.Basic, "What does this picture show?", "[img:heart.png]\nRev's icon", [], now.AddDays(-7));
    db.AddNote(test.Id, CardKind.TypeIn, "Reversal agent for medetomidine?", "Atipamezole.", [], now.AddDays(-6));
    db.AddNote(test.Id, CardKind.Basic, "Q4", "A4", [], now.AddDays(-5));
    Console.WriteLine($"Made {path} with the Review test deck ({db.GetCards().Count} cards).");
    return 0;
}

// (deck name, questions and answers, how many are already studied)
var decks = new (string Name, (string Q, string A)[] Cards, int Studied)[]
{
    ("Pharmacology", [
        ("Class of meloxicam?", "NSAID (COX-2 preferential)"),
        ("Reversal agent for medetomidine?", "Atipamezole"),
        ("Main toxicity of aminoglycosides?", "Nephrotoxicity and ototoxicity"),
        ("Drug of choice for status epilepticus in dogs?", "Diazepam (or midazolam)"),
        ("Why avoid ivermectin in Collies?", "MDR1 (ABCB1) mutation → neurotoxicity"),
        ("Antidote for paracetamol in cats?", "N-acetylcysteine"),
    ], 4),
    ("Parasitology", [
        ("Intermediate host of *Fasciola hepatica*?", "Mud snail (Galba truncatula)"),
        ("Zoonotic roundworm of dogs?", "Toxocara canis"),
        ("Vector of *Babesia canis*?", "Dermacentor ticks"),
    ], 3),
    ("Large Animal Surgery", [
        ("Most common site of a displaced abomasum?", "Left (LDA)"),
        ("Approach for a bovine caesarean?", "Left flank, standing"),
    ], 0),
    ("Anatomy › Forelimb", [
        ("Nerve damaged in radial paralysis?", "Radial nerve"),
        ("Muscle that extends the elbow?", "Triceps brachii"),
        ("Bones of the canine carpus (rows)?", "Proximal row: radial, ulnar, accessory carpal; distal row: carpals I–IV"),
    ], 1),
};

for (int d = 0; d < decks.Length; d++)
{
    var deck = db.AddDeck(decks[d].Name, now.AddDays(-30 + d));
    for (int i = 0; i < decks[d].Cards.Length; i++)
    {
        var (q, a) = decks[d].Cards[i];
        var card = new Card { DeckId = deck.Id, Front = q, Back = a, CreatedAt = now.AddDays(-20).AddMinutes(i) };
        if (i < decks[d].Studied)
        {
            // A graduated card: due today for the first two, later for the rest.
            card.SchedulingState = new SchedulingState
            {
                Phase = CardPhase.Review, IntervalDays = 3 + i * 2, Ease = 2.5, Reps = 3 + i, Stability = 3 + i * 2,
                Difficulty = 5, Due = i < 2 ? today : today.AddDays(i), LastReviewed = today.AddDays(-3 - i * 2),
            };
        }
        db.AddCard(card);
    }
}

// Tags, one leech and one suspended card, so the card browser's filters have something to show.
foreach (var card in db.GetCards())
{
    string deckName = db.GetDecks().First(d => d.Id == card.DeckId).Name;
    card.TagsText = deckName switch
    {
        "Pharmacology" => card.Front.Contains("toxicity") || card.Front.Contains("Antidote") ? "pharm toxicology" : "pharm",
        "Parasitology" => "parasites paper_1",
        _ => "",
    };
    if (card.Front.StartsWith("Why avoid ivermectin")) card.IsLeech = true;
    if (card.Front.StartsWith("Approach for a bovine")) card.IsSuspended = true;
    db.UpdateCard(card);
}

// A few answers given today, so "Done today" isn't zero.
foreach (var card in db.GetCards().Take(5))
    db.AddReviewLog(new ReviewLog { CardId = card.Id, Date = today.AddHours(2 + random.NextDouble()), Rating = Rating.Good });

if (args.Length == 2 && args[1] == "--export")
{
    // A tiny 2x2 PNG picture on one card, then the whole collection as an Anki package.
    string media = Path.Combine(args[0], "Media");
    Directory.CreateDirectory(media);
    File.WriteAllBytes(Path.Combine(media, "heart.png"), Convert.FromHexString(
        "89504e470d0a1a0a0000000d4948445200000002000000020802000000fdd49a730000001649444154789c63f8cfc0f01f0c18fe" +
        "33300c0c0c00002f0a05fb8ad26ba50000000049454e44ae426082"));
    var pharm = db.GetDecks().First(d => d.Name == "Pharmacology");
    db.AddNote(pharm.Id, CardKind.Basic, "What does this **show**?", "[img:heart.png]\nA *heart* & lungs", ["pictures"], now);
    var result = ApkgWriter.Write(Path.Combine(args[0], "export.apkg"), ApkgWriter.AllDecks(db), media, now);
    Console.WriteLine($"Exported {result.Decks} decks, {result.Cards} cards and {result.Images} image to export.apkg.");
}

Console.WriteLine($"Made {path} with {db.GetDecks().Count} decks and {db.GetCards().Count} cards.");
return 0;
