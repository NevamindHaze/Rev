// Makes a database full of sample veterinary decks, for trying Rev out and for screenshots.
// It never touches your real data: you give it an empty folder, and it refuses to overwrite a database.
//
//   dotnet run --project tools/Rev.Demo -- C:\Temp\rev-demo
//
// Then start Rev with that folder:
//   $env:REV_DATA_DIR = "C:\Temp\rev-demo"; dotnet run --project src/Rev.App

using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Scheduling;

if (args.Length != 1)
{
    Console.WriteLine("Usage: Rev.Demo <empty folder>");
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

// A few answers given today, so "Done today" isn't zero.
foreach (var card in db.GetCards().Take(5))
    db.AddReviewLog(new ReviewLog { CardId = card.Id, Date = today.AddHours(2 + random.NextDouble()), Rating = Rating.Good });

Console.WriteLine($"Made {path} with {db.GetDecks().Count} decks and {db.GetCards().Count} cards.");
return 0;
